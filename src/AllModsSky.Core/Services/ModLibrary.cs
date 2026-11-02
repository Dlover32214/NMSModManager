namespace AllModsSky.Core.Services;

/// <summary>
/// library.json 的内存模型。
/// 模组库是全局池子（<see cref="Mods"/>），配置是池子的有序子集（<see cref="ModProfile.Entries"/>）。
/// 只有当前配置里的模组会出现在主列表、参与部署；其余的留在模组库里，文件停在 MODS_disabled。
/// </summary>
public sealed class ModLibrary
{
    private readonly List<ModRecord> _mods = new();
    private readonly List<ModProfile> _profiles = new();

    /// <summary>内置「无模组」：空配置，永远在菜单最上面，不可改名/删除，也不写进 library.json。</summary>
    private readonly ModProfile _none = new() { Id = ModProfile.NoneId, Name = L.T("无模组") };

    private string _activeId = ModProfile.NoneId;

    public IReadOnlyList<ModRecord> Mods => _mods;
    public IReadOnlyList<ModProfile> Profiles => _profiles;
    public string? LoadMessage { get; private set; }
    public string? LastSaveError { get; private set; }

    private bool _corrupt;

    /// <summary>本次加载发现 library.json 损坏，写盘被锁定，直到 UI 确认。</summary>
    public bool IsCorrupt => _corrupt;

    /// <summary>用户确认「以空库继续」后解锁写入。</summary>
    public void AcknowledgeCorruption() => _corrupt = false;

    public void Load()
    {
        _mods.Clear();
        _profiles.Clear();
        _activeId = ModProfile.NoneId;
        _corrupt = false;
        LoadMessage = null;

        // 解析失败时先备份原文件并锁死写入，避免空列表被 Save() 覆盖回去。
        if (!JsonStore.TryLoad<LibraryState>(AppPaths.DataFile, out var state, out _))
        {
            var backup = NextBackupPath();
            try
            {
                File.Move(AppPaths.DataFile, backup);
                AppLog.Warn("library.json 无法解析，已备份到 " + backup);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "备份损坏的 library.json");
                backup = AppPaths.DataFile;
            }

            _corrupt = true;
            LoadMessage = $"library.json 无法解析，已备份到 {backup}。本次以空库启动，不会覆盖原文件。";
            return;
        }

        var migrated = false;

        if (state == null)
        {
            var legacy = LegacyDataMigrator.Read();
            if (legacy.Mods.Count > 0)
            {
                state = new LibraryState { Mods = legacy.Mods };
                migrated = true;
                LoadMessage = $"已从旧版迁移 {legacy.Mods.Count} 个模组记录";
            }
        }

        if (state?.Mods != null)
        {
            foreach (var record in state.Mods)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.Name)) continue;
                _mods.Add(record);
            }
        }

        if (state?.Profiles != null)
        {
            foreach (var profile in state.Profiles)
            {
                if (profile == null || string.IsNullOrWhiteSpace(profile.Name)) continue;
                if (profile.BuiltIn) continue; // 内置项不落盘，读到说明是脏数据
                if (string.IsNullOrWhiteSpace(profile.Id)) profile.Id = Guid.NewGuid().ToString("N");
                if (MigrateProfile(profile)) migrated = true;
                _profiles.Add(profile);
            }
        }

        // 第一次升级到「配置」模型：老库里只有一份启停状态，把它收成一套「默认」配置，
        // 免得升级后所有模组都掉进模组库、部署时被整体停用。
        var seeded = false;
        if (_profiles.Count == 0 && _mods.Any(m => m.Enabled))
        {
            var profile = new ModProfile { Name = L.T("默认") };
            foreach (var record in _mods)
                profile.Entries.Add(new ConfigEntry { ModId = record.Id, Enabled = record.Enabled });
            _profiles.Add(profile);
            seeded = true;
        }

        _activeId = state?.ActiveProfileId ?? "";
        if (!IsKnownProfile(_activeId))
            _activeId = _profiles.Count > 0 ? _profiles[0].Id : ModProfile.NoneId;

        RefreshDeletable();
        var normalized = Normalize() + PruneMissingEntries();

        // 配置是唯一真相：启动时把启停状态对齐到当前配置，避免「配置里没有、磁盘上却开着」的模组。
        var changed = Project();
        if (changed > 0)
        {
            AppLog.Info($"按配置「{ActiveProfile.Name}」对齐启停状态：{changed} 个模组改变");
            if (LoadMessage == null)
                LoadMessage = $"已按配置「{ActiveProfile.Name}」对齐启停状态（{changed} 个模组）";
        }

        if (migrated || seeded || normalized > 0 || changed > 0) Save();

        if (state == null) LoadMessage ??= L.T("这是第一次运行，模组库为空");
    }

    /// <summary>把旧版的 EnabledIds 迁进 Entries，并丢掉指向已不存在模组的条目。</summary>
    private bool MigrateProfile(ModProfile profile)
    {
        var changed = false;
        profile.Entries ??= new List<ConfigEntry>();

        if (profile.Entries.Count == 0 && profile.LegacyEnabledIds is { Count: > 0 } legacy)
        {
            var enabled = new HashSet<string>(legacy, StringComparer.OrdinalIgnoreCase);
            foreach (var record in _mods.Where(m => enabled.Contains(m.Id)))
                profile.Entries.Add(new ConfigEntry { ModId = record.Id, Enabled = true });
            changed = true;
        }

        if (profile.LegacyEnabledIds != null)
        {
            profile.LegacyEnabledIds = null;
            changed = true;
        }

        var known = new HashSet<string>(_mods.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var kept = profile.Entries
            .Where(e => e != null && !string.IsNullOrEmpty(e.ModId))
            .Where(e => known.Contains(e.ModId) && seen.Add(e.ModId))
            .ToList();

        if (kept.Count != profile.Entries.Count)
        {
            profile.Entries = kept;
            changed = true;
        }

        return changed;
    }

    /// <summary>损坏备份名：backup_library_20261102125300.json.bak，与 library.json 同目录。</summary>
    private static string NextBackupPath()
    {
        var dir = Path.GetDirectoryName(AppPaths.DataFile) ?? AppPaths.Root;
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var path = Path.Combine(dir, $"backup_library_{stamp}.json.bak");

        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(dir, $"backup_library_{stamp}_{n}.json.bak");

        return path;
    }

    public bool Save()
    {
        if (_corrupt)
        {
            LastSaveError = "模组库处于损坏保护状态，已跳过写入";
            return false;
        }

        try
        {
            AppPaths.Ensure();
            JsonStore.Save(AppPaths.DataFile, new LibraryState
            {
                Mods = _mods.ToList(),
                Profiles = _profiles.ToList(),
                ActiveProfileId = _activeId
            });
            LastSaveError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastSaveError = ex.Message;
            AppLog.Error(ex, "保存模组库");
            return false;
        }
    }

    /// <summary>补齐缺失的 Id / 目录名，并保证目录名不重复。返回修正条数。</summary>
    public int Normalize()
    {
        var fixes = 0;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in _mods)
        {
            if (string.IsNullOrWhiteSpace(record.Name))
            {
                record.Name = "未命名模组";
                fixes++;
            }

            if (string.IsNullOrWhiteSpace(record.Id) || !ids.Add(record.Id))
            {
                record.Id = Guid.NewGuid().ToString("N");
                ids.Add(record.Id);
                fixes++;
            }

            // 磁盘上的目录名一定是 SafeFileName 净化过的；旧索引里的 FolderName 可能还带 : ? * 等字符，
            // 两边不统一就永远匹配不上，模组在界面上会变成「不存在」。
            var desiredFolder = FileOps.SafeFileName(
                string.IsNullOrWhiteSpace(record.FolderName) ? record.Name : record.FolderName, 80);

            if (!string.Equals(desiredFolder, record.FolderName, StringComparison.Ordinal))
            {
                record.FolderName = desiredFolder;
                fixes++;
            }

            if (!folders.Add(record.FolderName))
            {
                var baseName = record.FolderName;
                var n = 2;
                while (!folders.Add(baseName + " (" + n + ")")) n++;
                record.FolderName = baseName + " (" + n + ")";
                fixes++;
            }

            record.PakFiles ??= new List<string>();
            if (record.ImportedAt == default) record.ImportedAt = DateTimeOffset.Now;
        }

        // 配置名超长的（旧数据或手改过 json）也一起收进来，界面宽度是按 20 个字排的
        foreach (var profile in _profiles)
        {
            if (profile.BuiltIn) continue;

            var clamped = TextLimit.Clamp(profile.Name, ModProfile.MaxNameLength);
            if (clamped.Length == 0 || clamped == profile.Name) continue;

            profile.Name = clamped;
            fixes++;
        }

        return fixes;
    }

    public void Add(ModRecord record)
    {
        record.Id = string.IsNullOrWhiteSpace(record.Id) ? Guid.NewGuid().ToString("N") : record.Id;
        _mods.Add(record);
    }

    /// <summary>
    /// 摘掉配置里指向已删模组的条目。
    /// 留着没有意义（解析、部署都跳过它），却会让菜单显示成「1 / 1 个启用」——跟实际列表对不上。
    /// 返回清理条数。
    /// </summary>
    public int PruneMissingEntries()
    {
        var known = new HashSet<string>(_mods.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        var removed = 0;

        foreach (var profile in _profiles)
            removed += profile.Entries.RemoveAll(e => e == null || !known.Contains(e.ModId));

        return removed;
    }

    /// <summary>从模组库彻底移除记录，并把它从所有配置里摘掉。</summary>
    public bool Remove(ModRecord record)
    {
        if (!_mods.Remove(record)) return false;

        foreach (var profile in _profiles)
            profile.Entries.RemoveAll(e => string.Equals(e.ModId, record.Id, StringComparison.OrdinalIgnoreCase));

        return true;
    }

    public ModRecord? ById(string? id) =>
        string.IsNullOrEmpty(id) ? null : _mods.FirstOrDefault(m => m.Id == id);

    public bool NameExists(string name) =>
        _mods.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool FolderExists(string folderName) =>
        _mods.Any(m => string.Equals(m.EffectiveFolder, folderName, StringComparison.OrdinalIgnoreCase));

    // ---- 配置 ----

    /// <summary>菜单里要显示的配置：内置「无模组」永远排第一。</summary>
    public IEnumerable<ModProfile> MenuProfiles => new[] { _none }.Concat(_profiles);

    public ModProfile ActiveProfile => FindProfile(_activeId) ?? _none;

    public string ActiveProfileId => ActiveProfile.Id;

    /// <summary>当前配置里的模组，顺序即加载顺序。</summary>
    public IReadOnlyList<ModRecord> ActiveMods => Resolve(ActiveProfile);

    /// <summary>模组库里不在当前配置中的模组（顺序按池子）。</summary>
    public IReadOnlyList<ModRecord> PoolMods
    {
        get
        {
            var inProfile = new HashSet<string>(
                ActiveProfile.Entries.Select(e => e.ModId), StringComparer.OrdinalIgnoreCase);
            return _mods.Where(m => !inProfile.Contains(m.Id)).ToList();
        }
    }

    public bool IsKnownProfile(string? id) => FindProfile(id) != null;

    private ModProfile? FindProfile(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (id == ModProfile.NoneId) return _none;
        return _profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>把配置里的引用解析成记录，顺序保持配置里的顺序；已删除的引用直接跳过。</summary>
    private List<ModRecord> Resolve(ModProfile profile)
    {
        if (profile.BuiltIn || profile.Entries.Count == 0) return new List<ModRecord>();

        var map = new Dictionary<string, ModRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in _mods) map[record.Id] = record;

        var list = new List<ModRecord>(profile.Entries.Count);
        foreach (var entry in profile.Entries)
            if (map.TryGetValue(entry.ModId, out var record)) list.Add(record);

        return list;
    }

    /// <summary>
    /// 切换当前配置：把它记下的启停状态铺到模组记录上（不在配置里的模组一律停用）。
    /// 返回实际改变的模组数量。
    /// </summary>
    public int SetActiveProfile(string id)
    {
        if (!IsKnownProfile(id)) return 0;
        _activeId = id;
        return Project();
    }

    /// <summary>把当前配置的启停状态铺到模组记录上，返回改变数量。</summary>
    private int Project()
    {
        var wanted = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (!ActiveProfile.BuiltIn)
            foreach (var entry in ActiveProfile.Entries) wanted[entry.ModId] = entry.Enabled;

        var changed = 0;
        foreach (var record in _mods)
        {
            var want = wanted.TryGetValue(record.Id, out var enabled) && enabled;
            if (record.Enabled == want) continue;
            record.Enabled = want;
            changed++;
        }

        return changed;
    }

    /// <summary>
    /// 新建一套配置。copyActive 为真时把当前配置整份复制过来（含顺序与启停状态）。
    /// 名字超过 <see cref="ModProfile.MaxNameLength"/> 个字会被截断。
    /// </summary>
    public ModProfile CreateProfile(string name, bool copyActive = true)
    {
        var profile = new ModProfile
        {
            Name = TextLimit.Clamp(name, ModProfile.MaxNameLength),
            UpdatedAt = DateTimeOffset.Now
        };

        if (copyActive)
            foreach (var entry in ActiveProfile.Entries)
                profile.Entries.Add(new ConfigEntry { ModId = entry.ModId, Enabled = entry.Enabled });

        _profiles.Add(profile);
        RefreshDeletable();
        return profile;
    }

    /// <summary>库里只剩一套配置时，那一套不能删——总得留个地方放模组。</summary>
    private void RefreshDeletable()
    {
        var only = _profiles.Count <= 1;
        foreach (var profile in _profiles) profile.Deletable = !only;
    }

    /// <summary>删除配置。删掉的正好是当前配置时，切到剩下的第一套（没有就切「无模组」）。</summary>
    public bool RemoveProfile(ModProfile profile)
    {
        if (profile.BuiltIn || !profile.Deletable) return false;
        if (!_profiles.Remove(profile)) return false;

        if (string.Equals(_activeId, profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            _activeId = _profiles.Count > 0 ? _profiles[0].Id : ModProfile.NoneId;
            Project();
        }

        RefreshDeletable();
        return true;
    }

    public bool RenameProfile(ModProfile profile, string name)
    {
        if (profile.BuiltIn) return false;

        var clamped = TextLimit.Clamp(name, ModProfile.MaxNameLength);
        if (clamped.Length == 0) return false;

        profile.Name = clamped;
        profile.UpdatedAt = DateTimeOffset.Now;
        return true;
    }

    public bool ProfileNameExists(string name, ModProfile? except = null) =>
        MenuProfiles.Any(p => !ReferenceEquals(p, except)
                              && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 把模组加进当前配置。
    /// 内置「无模组」不放模组（它的定义就是全停用直接启动原版），此时返回 Added = 0，
    /// 由调用方提示用户先新建一套配置。
    /// </summary>
    public (int Added, ModProfile Profile) AddToActiveProfile(IEnumerable<ModRecord> records)
    {
        var profile = ActiveProfile;
        if (profile.BuiltIn) return (0, profile);

        var existing = new HashSet<string>(profile.Entries.Select(e => e.ModId), StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var record in records)
        {
            if (!existing.Add(record.Id)) continue;
            profile.Entries.Add(new ConfigEntry { ModId = record.Id, Enabled = true });
            record.Enabled = true;
            added++;
        }

        if (added > 0) profile.UpdatedAt = DateTimeOffset.Now;
        return (added, profile);
    }

    /// <summary>同步某几个模组在当前配置里的启停状态（记录与配置条目一起改）。</summary>
    public int SetEntriesEnabled(IEnumerable<ModRecord> records, bool enabled)
    {
        var profile = ActiveProfile;
        var wanted = new HashSet<string>(records.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
        var changed = 0;

        foreach (var record in _mods)
        {
            if (!wanted.Contains(record.Id)) continue;
            if (record.Enabled != enabled) changed++;
            record.Enabled = enabled;
        }

        foreach (var entry in profile.Entries)
        {
            if (!wanted.Contains(entry.ModId)) continue;
            entry.Enabled = enabled;
        }

        if (changed > 0) profile.UpdatedAt = DateTimeOffset.Now;
        return changed;
    }

    /// <summary>把模组移出当前配置（放回模组库），并停用。不动任何文件。</summary>
    public bool RemoveFromActiveProfile(ModRecord record)
    {
        var profile = ActiveProfile;
        if (profile.BuiltIn) return false;

        var removed = profile.Entries.RemoveAll(e => string.Equals(e.ModId, record.Id, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed) return false;

        record.Enabled = false;
        profile.UpdatedAt = DateTimeOffset.Now;
        return true;
    }

    /// <summary>配置内的重排：把 from 位置的条目挪到 to。</summary>
    public bool MoveEntry(int from, int to)
    {
        var entries = ActiveProfile.Entries;
        if (from < 0 || from >= entries.Count) return false;

        to = Math.Clamp(to, 0, entries.Count - 1);
        if (from == to) return false;

        var entry = entries[from];
        entries.RemoveAt(from);
        entries.Insert(to, entry);
        ActiveProfile.UpdatedAt = DateTimeOffset.Now;
        return true;
    }

    /// <summary>配置内的批量重排：选中的条目整体挪到最前或最后。</summary>
    public bool MoveEntriesToEdge(IReadOnlyList<ModRecord> records, bool toTop)
    {
        var profile = ActiveProfile;
        if (profile.BuiltIn || records.Count == 0) return false;

        var ids = new HashSet<string>(records.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
        var picked = profile.Entries.Where(e => ids.Contains(e.ModId)).ToList();
        if (picked.Count == 0) return false;

        profile.Entries.RemoveAll(e => ids.Contains(e.ModId));
        if (toTop) profile.Entries.InsertRange(0, picked);
        else profile.Entries.AddRange(picked);

        profile.UpdatedAt = DateTimeOffset.Now;
        return true;
    }

    /// <summary>配置引用了、但库里已不存在的模组数量。</summary>
    public int MissingProfileEntries(ModProfile profile)
    {
        var ids = new HashSet<string>(_mods.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        return profile.Entries.Count(e => !ids.Contains(e.ModId));
    }

    /// <summary>「配置 2」这种不重名的默认名字。</summary>
    public string NextProfileName()
    {
        for (var n = _profiles.Count + 1; ; n++)
        {
            var name = L.F("配置 {0}", n);
            if (!ProfileNameExists(name)) return name;
        }
    }

    public IEnumerable<ModRecord> Where(Func<ModRecord, bool> predicate) => _mods.Where(predicate);
}

namespace AllModsSky.Core;

/// <summary>library.json 的内存模型：顺序即加载顺序。</summary>
public sealed class ModLibrary
{
    private readonly List<ModRecord> _mods = new();
    private readonly List<ModProfile> _profiles = new();

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
                if (string.IsNullOrWhiteSpace(profile.Id)) profile.Id = Guid.NewGuid().ToString("N");
                profile.EnabledIds ??= new List<string>();
                _profiles.Add(profile);
            }
        }

        var normalized = Normalize();
        if (migrated) Save();
        else if (normalized > 0) Save();

        if (state == null) LoadMessage ??= "这是第一次运行，模组库为空";
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
                Profiles = _profiles.ToList()
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

        return fixes;
    }

    public void Add(ModRecord record)
    {
        record.Id = string.IsNullOrWhiteSpace(record.Id) ? Guid.NewGuid().ToString("N") : record.Id;
        _mods.Add(record);
    }

    public bool Remove(ModRecord record) => _mods.Remove(record);

    public ModRecord? ById(string? id) =>
        string.IsNullOrEmpty(id) ? null : _mods.FirstOrDefault(m => m.Id == id);

    public bool NameExists(string name) =>
        _mods.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool FolderExists(string folderName) =>
        _mods.Any(m => string.Equals(m.EffectiveFolder, folderName, StringComparison.OrdinalIgnoreCase));

    public int IndexOf(ModRecord record) => _mods.IndexOf(record);

    public void Move(int from, int to)
    {
        if (from < 0 || from >= _mods.Count) return;
        to = Math.Clamp(to, 0, _mods.Count - 1);
        if (from == to) return;

        var item = _mods[from];
        _mods.RemoveAt(from);
        _mods.Insert(to, item);
    }

    public void MoveMany(IReadOnlyList<ModRecord> items, bool toTop)
    {
        if (items.Count == 0) return;

        var set = new HashSet<ModRecord>(items);
        var ordered = _mods.Where(m => set.Contains(m)).ToList();
        _mods.RemoveAll(m => set.Contains(m));

        if (toTop) _mods.InsertRange(0, ordered);
        else _mods.AddRange(ordered);
    }

    public void Reverse() => _mods.Reverse();

    // ---- 配置 ----

    /// <summary>用当前启停状态生成一个配置（不加入集合）。</summary>
    public ModProfile CaptureProfile(string name) => new()
    {
        Name = name,
        EnabledIds = _mods.Where(m => m.Enabled).Select(m => m.Id).ToList(),
        UpdatedAt = DateTimeOffset.Now
    };

    public void AddProfile(ModProfile profile) => _profiles.Add(profile);

    public bool RemoveProfile(ModProfile profile) => _profiles.Remove(profile);

    public bool ProfileNameExists(string name, ModProfile? except = null) =>
        _profiles.Any(p => !ReferenceEquals(p, except)
                           && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>用当前启停状态覆盖已有配置。</summary>
    public void RefreshProfile(ModProfile profile)
    {
        profile.EnabledIds = _mods.Where(m => m.Enabled).Select(m => m.Id).ToList();
        profile.UpdatedAt = DateTimeOffset.Now;
    }

    /// <summary>按配置设置启停状态。配置里已不存在的模组 Id 会被忽略，返回实际改变的条数。</summary>
    public int ApplyProfile(ModProfile profile)
    {
        var enabled = new HashSet<string>(profile.EnabledIds, StringComparer.OrdinalIgnoreCase);
        var changed = 0;

        foreach (var record in _mods)
        {
            var want = enabled.Contains(record.Id);
            if (record.Enabled == want) continue;
            record.Enabled = want;
            changed++;
        }

        return changed;
    }

    /// <summary>配置引用了、但库里已不存在的模组数量。</summary>
    public int MissingProfileEntries(ModProfile profile)
    {
        var ids = new HashSet<string>(_mods.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        return profile.EnabledIds.Count(id => !ids.Contains(id));
    }

    public void ReplaceAll(IEnumerable<ModRecord> records)
    {
        _mods.Clear();
        _mods.AddRange(records);
        Normalize();
    }

    public IEnumerable<ModRecord> Where(Func<ModRecord, bool> predicate) => _mods.Where(predicate);
}

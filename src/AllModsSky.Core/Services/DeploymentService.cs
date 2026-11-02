namespace AllModsSky.Core.Services;

public enum ModPresence
{
    InMods,
    Parked,
    Both,
    Missing
}

/// <summary>一次状态同步的统计。</summary>
public sealed class DeployReport
{
    private readonly List<string> _messages = new();

    public int Added { get; set; }
    public int Removed { get; set; }
    public int Unchanged { get; set; }
    public int Missing { get; set; }
    public int Failed { get; set; }

    public IReadOnlyList<string> Messages => _messages;

    public void Message(string message)
    {
        if (_messages.Count >= 40) return;
        if (!_messages.Contains(message, StringComparer.OrdinalIgnoreCase)) _messages.Add(message);
    }

    public bool HasIssues => Failed > 0 || Missing > 0;

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Added > 0) parts.Add($"启用 {Added}");
            if (Removed > 0) parts.Add($"停用 {Removed}");
            if (Unchanged > 0) parts.Add($"未变 {Unchanged}");
            if (Missing > 0) parts.Add($"缺失 {Missing}");
            if (Failed > 0) parts.Add($"失败 {Failed}");
            return parts.Count == 0 ? "没有需要同步的变化" : string.Join(" · ", parts);
        }
    }
}

/// <summary>MODS 里不由本管理器管理的模组。</summary>
public sealed class ExternalMod
{
    public string Name { get; init; } = "";
    public bool Enabled { get; init; }
    public bool IsFile { get; init; }

    public string StateText => Enabled ? "启用中" : "已停用";
    public string ActionText => Enabled ? "停用" : "启用";
    public string KindText => IsFile ? "pak 文件" : "文件夹";
}

/// <summary>
/// 把库里的启停状态同步到 GAMEDATA\MODS。
/// 库就是 MODS 本身：启用 = 文件在 MODS 里，停用 = 挪到同级的 MODS_disabled。
/// </summary>
public sealed class DeploymentService
{
    public DeployReport Sync(string? gameDir, IReadOnlyList<ModRecord> mods, bool createDisableFile)
    {
        var report = new DeployReport();

        if (string.IsNullOrWhiteSpace(gameDir) || !GameLocator.IsValid(gameDir))
        {
            report.Failed++;
            report.Message("游戏目录无效，无法同步");
            return report;
        }

        var modsDir = GameLocator.ModsDir(gameDir);
        var parkedDir = GameLocator.ParkedDir(gameDir);

        try
        {
            Directory.CreateDirectory(modsDir);
            Directory.CreateDirectory(parkedDir);
        }
        catch (Exception ex)
        {
            report.Failed++;
            report.Message("无法访问 MODS 目录：" + ex.Message);
            return report;
        }

        // 索引里只放磁盘上真实存在的条目，后面所有定位都从这里查，不拼路径。
        var modsIndex = PathGuard.IndexEntries(modsDir);
        var parkedIndex = PathGuard.IndexEntries(parkedDir);

        foreach (var record in mods)
        {
            try
            {
                if (record.IsLegacyPak) SyncPak(record, modsDir, parkedDir, modsIndex, report);
                else SyncFolder(record, modsDir, parkedDir, modsIndex, parkedIndex, report);
            }
            catch (Exception ex)
            {
                report.Failed++;
                report.Message(record.Name + "：" + ex.Message);
                AppLog.Error(ex, "同步模组 " + record.Name);
            }
        }

        ApplyDisableModsFlag(gameDir, createDisableFile, mods.Any(m => m.Enabled));
        return report;
    }

    private static void SyncFolder(
        ModRecord record, string modsDir, string parkedDir,
        Dictionary<string, PathGuard.Entry> modsIndex,
        Dictionary<string, PathGuard.Entry> parkedIndex,
        DeployReport report)
    {
        var key = record.EffectiveFolder;

        if (!PathGuard.IsSafeSegment(key))
        {
            report.Failed++;
            report.Message($"{record.Name}：目录名非法，已跳过（{PathGuard.Describe(key)}）");
            AppLog.Warn($"跳过非法目录名：{PathGuard.Describe(key)}");
            return;
        }

        // 只认磁盘上真实存在的目录：索引里的名字匹配不上，就等于不存在。
        var live = modsIndex.TryGetValue(key, out var liveEntry) && liveEntry.IsDirectory ? liveEntry.Path : null;
        var parked = parkedIndex.TryGetValue(key, out var parkedEntry) && parkedEntry.IsDirectory ? parkedEntry.Path : null;

        if (record.Enabled)
        {
            if (live != null)
            {
                if (parked != null) FileOps.TryDeleteDirectory(parked); // 以 MODS 里的副本为准
                report.Unchanged++;
                return;
            }

            if (parked != null)
            {
                var target = Path.Combine(modsDir, key);
                FileOps.MoveDirectory(parked, target);
                parkedIndex.Remove(key);
                modsIndex[key] = new PathGuard.Entry(target, true);
                report.Added++;
                return;
            }

            report.Missing++;
            report.Message($"{record.Name}：目录不存在（{key}）");
            return;
        }

        if (parked != null && live == null)
        {
            report.Unchanged++;
            return;
        }

        if (live != null)
        {
            if (parked != null) FileOps.TryDeleteDirectory(parked);
            var target = Path.Combine(parkedDir, key);
            FileOps.MoveDirectory(live, target);
            modsIndex.Remove(key);
            parkedIndex[key] = new PathGuard.Entry(target, true);
            report.Removed++;
            return;
        }

        report.Missing++;
        report.Message($"{record.Name}：目录不存在（{key}）");
    }

    private static void SyncPak(
        ModRecord record, string modsDir, string parkedDir,
        Dictionary<string, PathGuard.Entry> modsIndex,
        DeployReport report)
    {
        var key = record.EffectiveFolder;

        if (!PathGuard.IsSafeSegment(key))
        {
            report.Failed++;
            report.Message($"{record.Name}：目录名非法，已跳过（{PathGuard.Describe(key)}）");
            AppLog.Warn($"跳过非法目录名：{PathGuard.Describe(key)}");
            return;
        }

        // 子目录是否存在也从磁盘枚举，不靠拼接判断。
        var parkedFolder = PathGuard.FindExisting(parkedDir, key);
        var stagingFolder = PathGuard.FindExisting(modsDir, key);
        var parkedTarget = parkedFolder ?? Path.Combine(parkedDir, key);
        var stagingTarget = stagingFolder ?? Path.Combine(modsDir, key);

        foreach (var relative in record.PakFiles)
        {
            var fileName = Path.GetFileName(relative);
            if (!PathGuard.IsSafeSegment(fileName)) continue;

            // MODS 根下的散装 pak 从索引里查
            var live = modsIndex.TryGetValue(fileName, out var liveEntry) && !liveEntry.IsDirectory
                ? liveEntry.Path
                : null;

            var parked = parkedFolder == null ? null : PathGuard.FindExisting(parkedFolder, fileName, wantFile: true);
            var staged = stagingFolder == null ? null : PathGuard.FindExisting(stagingFolder, fileName, wantFile: true);

            if (record.Enabled)
            {
                if (live != null)
                {
                    report.Unchanged++;
                    continue;
                }

                var target = Path.Combine(modsDir, fileName);

                if (parked != null)
                {
                    FileOps.MoveFile(parked, target);
                    report.Added++;
                    continue;
                }

                if (staged != null)
                {
                    FileOps.MoveFile(staged, target);
                    report.Added++;
                    continue;
                }

                report.Missing++;
                report.Message($"{record.Name}：缺少 {fileName}");
                continue;
            }

            if (parked != null)
            {
                report.Unchanged++;
                continue;
            }

            var source = live ?? staged;
            if (source == null)
            {
                report.Missing++;
                report.Message($"{record.Name}：缺少 {fileName}");
                continue;
            }

            Directory.CreateDirectory(parkedTarget);
            FileOps.MoveFile(source, Path.Combine(parkedTarget, fileName));
            report.Removed++;
        }

        // 清理空的临时目录
        if (Directory.Exists(stagingTarget) &&
            !Directory.EnumerateFileSystemEntries(stagingTarget).Any())
            FileOps.TryDeleteDirectory(stagingTarget);

        if (Directory.Exists(parkedTarget) &&
            !Directory.EnumerateFileSystemEntries(parkedTarget).Any())
            FileOps.TryDeleteDirectory(parkedTarget);
    }

    public static void ApplyDisableModsFlag(string gameDir, bool createWhenAllDisabled, bool anyEnabled)
    {
        try
        {
            var flag = GameLocator.DisableModsFile(gameDir);
            if (anyEnabled)
            {
                if (File.Exists(flag)) File.Delete(flag);
                return;
            }

            if (!createWhenAllDisabled) return;
            Directory.CreateDirectory(Path.GetDirectoryName(flag)!);
            if (!File.Exists(flag)) File.WriteAllText(flag, "");
        }
        catch (Exception ex)
        {
            AppLog.Warn("更新 DISABLEMODS.TXT 失败：" + ex.Message);
        }
    }

    public ModPresence Presence(string gameDir, ModRecord record)
    {
        if (!GameLocator.IsValid(gameDir)) return ModPresence.Missing;

        var modsDir = GameLocator.ModsDir(gameDir);
        var parkedDir = GameLocator.ParkedDir(gameDir);
        var key = record.EffectiveFolder;

        if (PathGuard.HasPathSeparator(key)) return ModPresence.Missing;

        // 存在性来自磁盘枚举，而不是拼接后 Directory.Exists。
        var liveFolder = PathGuard.FindExisting(modsDir, key);
        var parkedFolder = PathGuard.FindExisting(parkedDir, key);

        if (record.IsLegacyPak)
        {
            var live = record.PakFiles.Any(p =>
                PathGuard.FindExisting(modsDir, Path.GetFileName(p), wantFile: true) != null);
            var parked = parkedFolder != null && record.PakFiles.Any(p =>
                PathGuard.FindExisting(parkedFolder, Path.GetFileName(p), wantFile: true) != null);

            return (live, parked) switch
            {
                (true, true) => ModPresence.Both,
                (true, false) => ModPresence.InMods,
                (false, true) => ModPresence.Parked,
                _ => ModPresence.Missing
            };
        }

        return (liveFolder != null, parkedFolder != null) switch
        {
            (true, true) => ModPresence.Both,
            (true, false) => ModPresence.InMods,
            (false, true) => ModPresence.Parked,
            _ => ModPresence.Missing
        };
    }

    public IReadOnlyList<ExternalMod> ListExternal(string? gameDir, IReadOnlyList<ModRecord> mods)
    {
        var result = new List<ExternalMod>();
        if (string.IsNullOrWhiteSpace(gameDir) || !GameLocator.IsValid(gameDir)) return result;

        var modsDir = GameLocator.ModsDir(gameDir);
        var parkedDir = GameLocator.ParkedDir(gameDir);

        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in mods)
        {
            owned.Add(record.EffectiveFolder);
            foreach (var pak in record.PakFiles)
            {
                var name = Path.GetFileName(pak);
                if (!string.IsNullOrWhiteSpace(name)) owned.Add(name);
            }
        }

        try
        {
            foreach (var dir in Directory.GetDirectories(modsDir))
            {
                var name = Path.GetFileName(dir);
                if (!owned.Contains(name)) result.Add(new ExternalMod { Name = name, Enabled = true });
            }

            foreach (var file in Directory.GetFiles(modsDir))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith('.')) continue;
                if (!owned.Contains(name)) result.Add(new ExternalMod { Name = name, Enabled = true, IsFile = true });
            }

            if (Directory.Exists(parkedDir))
            {
                foreach (var dir in Directory.GetDirectories(parkedDir))
                {
                    var name = Path.GetFileName(dir);
                    if (!owned.Contains(name)) result.Add(new ExternalMod { Name = name, Enabled = false });
                }

                foreach (var file in Directory.GetFiles(parkedDir))
                {
                    var name = Path.GetFileName(file);
                    if (!name.StartsWith('.')) result.Add(new ExternalMod { Name = name, Enabled = false, IsFile = true });
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("枚举外部模组失败：" + ex.Message);
        }

        return result.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>启用/停用一个外部模组：在 MODS 与停用区之间移动。</summary>
    public void SetExternal(string gameDir, ExternalMod external, bool enable)
    {
        var modsDir = GameLocator.ModsDir(gameDir);
        var parkedDir = GameLocator.ParkedDir(gameDir);
        Directory.CreateDirectory(modsDir);
        Directory.CreateDirectory(parkedDir);

        if (!PathGuard.IsSafeSegment(external.Name))
            throw new InvalidOperationException("模组名非法：" + PathGuard.Describe(external.Name));

        var sourceDir = enable ? parkedDir : modsDir;
        var targetDir = enable ? modsDir : parkedDir;

        // 源和目标都只认磁盘上真实存在的条目
        var source = PathGuard.FindExisting(sourceDir, external.Name)
                     ?? PathGuard.FindExisting(sourceDir, external.Name, wantFile: true);

        if (source == null)
            throw new InvalidOperationException("找不到：" + external.Name);

        var existingTarget = PathGuard.FindExisting(targetDir, external.Name)
                             ?? PathGuard.FindExisting(targetDir, external.Name, wantFile: true);

        if (existingTarget != null)
        {
            if (Directory.Exists(existingTarget)) Directory.Delete(existingTarget, true);
            else File.Delete(existingTarget);
        }

        var target = Path.Combine(targetDir, external.Name);
        if (Directory.Exists(source)) FileOps.MoveDirectory(source, target);
        else FileOps.MoveFile(source, target);
    }

    /// <summary>把一个外部模组登记进库（文件夹留在原处，不复制）。</summary>
    public ModRecord Adopt(string gameDir, ExternalMod external, Func<string, bool>? nameExists = null)
    {
        var modsDir = GameLocator.ModsDir(gameDir);
        var parkedDir = GameLocator.ParkedDir(gameDir);
        var livePath = PathGuard.FindExisting(modsDir, external.Name)
                       ?? PathGuard.FindExisting(modsDir, external.Name, wantFile: true);
        var parkedPath = PathGuard.FindExisting(parkedDir, external.Name)
                         ?? PathGuard.FindExisting(parkedDir, external.Name, wantFile: true);

        if (!Directory.Exists(livePath) && !Directory.Exists(parkedPath) && !File.Exists(livePath) && !File.Exists(parkedPath))
            throw new InvalidOperationException("找不到要收编的模组");

        var enabled = Directory.Exists(livePath) || File.Exists(livePath);
        var display = Importer.CleanName(Path.GetFileNameWithoutExtension(external.Name));
        var baseName = display;
        var n = 2;
        while (nameExists?.Invoke(display) == true) display = baseName + " (" + n++ + ")";

        var folderName = external.Name;
        var pakFiles = new List<string>();
        long size = 0;

        if (external.IsFile)
        {
            // 根目录的散装 pak：收进同名文件夹，便于启停
            folderName = FileOps.SafeFileName(display, 80);
            var folder = Path.Combine(enabled ? modsDir : parkedDir, folderName);
            Directory.CreateDirectory(folder);
            var file = enabled ? livePath : parkedPath;
            if (File.Exists(file)) FileOps.MoveFile(file, Path.Combine(folder, external.Name));
            pakFiles.Add(external.Name);
            size = new FileInfo(Path.Combine(folder, external.Name)).Length;
        }
        else
        {
            var folder = (enabled ? livePath : parkedPath)
                         ?? throw new InvalidOperationException("找不到要收编的模组");
            size = FileOps.DirSize(folder);
            pakFiles = DetectLegacyPaks(folder);
            folderName = external.Name;
        }

        var parsed = VersionParser.Parse(external.Name);

        return new ModRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = display,
            Source = "外部模组",
            Version = parsed?.Raw ?? "",
            FolderName = folderName,
            PakFiles = pakFiles,
            Enabled = enabled,
            Size = size,
            ImportedAt = DateTimeOffset.Now,
            Note = "从 MODS 收编"
        };
    }

    private static List<string> DetectLegacyPaks(string folder)
    {
        var paks = new List<string>();
        var hasExmlOrMbin = false;
        var hasNested = false;

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (ext.Equals(".pak", StringComparison.OrdinalIgnoreCase))
                {
                    paks.Add(Path.GetRelativePath(folder, file));
                    if (Path.GetDirectoryName(Path.GetRelativePath(folder, file)) is { Length: > 0 }) hasNested = true;
                }
                else if (ext.Equals(".EXML", StringComparison.OrdinalIgnoreCase) ||
                         ext.Equals(".MBIN", StringComparison.OrdinalIgnoreCase))
                {
                    hasExmlOrMbin = true;
                }
            }
        }
        catch { }

        return hasExmlOrMbin || hasNested ? new List<string>() : paks;
    }

    /// <summary>删除模组的实际文件（MODS 与停用区里的副本）。只删磁盘上真实枚举到的条目。</summary>
    public void DeleteFiles(string gameDir, ModRecord record)
    {
        if (string.IsNullOrWhiteSpace(gameDir) || !GameLocator.IsValid(gameDir)) return;

        var modsDir = GameLocator.ModsDir(gameDir);
        var parkedDir = GameLocator.ParkedDir(gameDir);
        var key = record.EffectiveFolder;

        if (PathGuard.HasPathSeparator(key))
        {
            AppLog.Warn($"跳过非法目录名，未删除任何文件：{PathGuard.Describe(key)}");
            return;
        }

        // 删除目标一律来自枚举结果，绝不使用拼接路径。
        var live = PathGuard.FindExisting(modsDir, key);
        var parked = PathGuard.FindExisting(parkedDir, key);

        if (record.IsLegacyPak)
        {
            foreach (var pak in record.PakFiles)
            {
                var name = Path.GetFileName(pak);
                if (!PathGuard.IsSafeSegment(name)) continue;

                var liveFile = PathGuard.FindExisting(modsDir, name, wantFile: true);
                if (liveFile != null) FileOps.TryDeleteFile(liveFile);

                if (parked != null)
                {
                    var parkedFile = PathGuard.FindExisting(parked, name, wantFile: true);
                    if (parkedFile != null) FileOps.TryDeleteFile(parkedFile);
                }
            }

            if (parked != null) FileOps.TryDeleteDirectory(parked);
            if (live != null) FileOps.TryDeleteDirectory(live);
            return;
        }

        if (live != null) FileOps.TryDeleteDirectory(live);
        if (parked != null) FileOps.TryDeleteDirectory(parked);
    }
}

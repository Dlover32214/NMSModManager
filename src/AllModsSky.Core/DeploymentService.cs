namespace AllModsSky.Core;

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

        foreach (var record in mods)
        {
            try
            {
                if (record.IsLegacyPak) SyncPak(record, modsDir, parkedDir, report);
                else SyncFolder(record, modsDir, parkedDir, report);
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

    private static void SyncFolder(ModRecord record, string modsDir, string parkedDir, DeployReport report)
    {
        var key = record.EffectiveFolder;
        var live = Path.Combine(modsDir, key);
        var parked = Path.Combine(parkedDir, key);

        var hasLive = Directory.Exists(live);
        var hasParked = Directory.Exists(parked);

        if (record.Enabled)
        {
            if (hasLive)
            {
                if (hasParked) FileOps.TryDeleteDirectory(parked); // 以 MODS 里的副本为准
                report.Unchanged++;
                return;
            }

            if (hasParked)
            {
                FileOps.MoveDirectory(parked, live);
                report.Added++;
                return;
            }

            report.Missing++;
            report.Message($"{record.Name}：目录不存在（{key}）");
            return;
        }

        if (hasParked && !hasLive)
        {
            report.Unchanged++;
            return;
        }

        if (hasLive)
        {
            if (hasParked) FileOps.TryDeleteDirectory(parked);
            FileOps.MoveDirectory(live, parked);
            report.Removed++;
            return;
        }

        report.Missing++;
        report.Message($"{record.Name}：目录不存在（{key}）");
    }

    private static void SyncPak(ModRecord record, string modsDir, string parkedDir, DeployReport report)
    {
        var key = record.EffectiveFolder;
        var parkedFolder = Path.Combine(parkedDir, key);
        var stagingFolder = Path.Combine(modsDir, key);

        foreach (var relative in record.PakFiles)
        {
            var fileName = Path.GetFileName(relative);
            if (string.IsNullOrWhiteSpace(fileName)) continue;

            var live = Path.Combine(modsDir, fileName);
            var parked = Path.Combine(parkedFolder, fileName);
            var staged = Path.Combine(stagingFolder, fileName);

            if (record.Enabled)
            {
                if (File.Exists(live))
                {
                    report.Unchanged++;
                    continue;
                }

                if (File.Exists(parked))
                {
                    FileOps.MoveFile(parked, live);
                    report.Added++;
                    continue;
                }

                if (File.Exists(staged))
                {
                    FileOps.MoveFile(staged, live);
                    report.Added++;
                    continue;
                }

                report.Missing++;
                report.Message($"{record.Name}：缺少 {fileName}");
                continue;
            }

            if (File.Exists(parked))
            {
                report.Unchanged++;
                continue;
            }

            var source = File.Exists(live) ? live : File.Exists(staged) ? staged : null;
            if (source == null)
            {
                report.Missing++;
                report.Message($"{record.Name}：缺少 {fileName}");
                continue;
            }

            Directory.CreateDirectory(parkedFolder);
            FileOps.MoveFile(source, parked);
            report.Removed++;
        }

        // 清理空的临时目录
        if (Directory.Exists(stagingFolder) &&
            !Directory.EnumerateFileSystemEntries(stagingFolder).Any())
            FileOps.TryDeleteDirectory(stagingFolder);

        if (Directory.Exists(parkedFolder) &&
            !Directory.EnumerateFileSystemEntries(parkedFolder).Any())
            FileOps.TryDeleteDirectory(parkedFolder);
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

        if (record.IsLegacyPak)
        {
            var live = record.PakFiles.Any(p => File.Exists(Path.Combine(modsDir, Path.GetFileName(p))));
            var parked = record.PakFiles.Any(p => File.Exists(Path.Combine(parkedDir, key, Path.GetFileName(p))));
            return (live, parked) switch
            {
                (true, true) => ModPresence.Both,
                (true, false) => ModPresence.InMods,
                (false, true) => ModPresence.Parked,
                _ => ModPresence.Missing
            };
        }

        var hasLive = Directory.Exists(Path.Combine(modsDir, key));
        var hasParked = Directory.Exists(Path.Combine(parkedDir, key));
        return (hasLive, hasParked) switch
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

        var source = Path.Combine(enable ? parkedDir : modsDir, external.Name);
        var target = Path.Combine(enable ? modsDir : parkedDir, external.Name);

        if (!Directory.Exists(source) && !File.Exists(source))
            throw new InvalidOperationException("找不到：" + external.Name);

        if (Directory.Exists(target)) Directory.Delete(target, true);
        else if (File.Exists(target)) File.Delete(target);

        if (Directory.Exists(source)) FileOps.MoveDirectory(source, target);
        else FileOps.MoveFile(source, target);
    }

    /// <summary>把一个外部模组登记进库（文件夹留在原处，不复制）。</summary>
    public ModRecord Adopt(string gameDir, ExternalMod external, Func<string, bool>? nameExists = null)
    {
        var modsDir = GameLocator.ModsDir(gameDir);
        var parkedDir = GameLocator.ParkedDir(gameDir);
        var livePath = Path.Combine(modsDir, external.Name);
        var parkedPath = Path.Combine(parkedDir, external.Name);

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
            var folder = enabled ? livePath : parkedPath;
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

    /// <summary>删除模组的实际文件（MODS 与停用区里的副本）。</summary>
    public void DeleteFiles(string gameDir, ModRecord record)
    {
        if (string.IsNullOrWhiteSpace(gameDir) || !GameLocator.IsValid(gameDir)) return;

        var modsDir = GameLocator.ModsDir(gameDir);
        var parkedDir = GameLocator.ParkedDir(gameDir);
        var key = record.EffectiveFolder;

        if (record.IsLegacyPak)
        {
            foreach (var pak in record.PakFiles)
            {
                var name = Path.GetFileName(pak);
                if (string.IsNullOrEmpty(name)) continue;
                FileOps.TryDeleteFile(Path.Combine(modsDir, name));
                FileOps.TryDeleteFile(Path.Combine(parkedDir, key, name));
            }
            FileOps.TryDeleteDirectory(Path.Combine(parkedDir, key));
            FileOps.TryDeleteDirectory(Path.Combine(modsDir, key));
            return;
        }

        FileOps.TryDeleteDirectory(Path.Combine(modsDir, key));
        FileOps.TryDeleteDirectory(Path.Combine(parkedDir, key));
    }
}

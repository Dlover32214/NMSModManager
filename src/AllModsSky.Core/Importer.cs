using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace AllModsSky.Core;

public sealed class ImportOutcome
{
    public bool Ok { get; init; }
    public string Source { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Message { get; init; } = "";
    public ModRecord? Record { get; init; }

    public static ImportOutcome Fail(string source, string displayName, string message) =>
        new() { Ok = false, Source = source, DisplayName = displayName, Message = message };
}

public sealed class ImportOptions
{
    public string SourceLabel { get; init; } = "本地导入";
    public Func<string, bool>? NameExists { get; init; }
    public IProgress<string>? Progress { get; init; }
    public CancellationToken Cancellation { get; init; } = CancellationToken.None;
}

/// <summary>把压缩包 / 文件夹 / 单个 .pak 收进模组库（库 = GAMEDATA\MODS）。</summary>
public static class Importer
{
    private static readonly string[] ArchiveExtensions = { ".zip", ".rar", ".7z" };
    private static readonly string[] JunkNames = { "__MACOSX", ".git", ".svn", ".DS_Store", "Thumbs.db", "desktop.ini" };

    public static bool IsSupported(string path) =>
        Directory.Exists(path) || IsArchive(path) || IsPak(path);

    public static bool IsArchive(string path) =>
        ArchiveExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static bool IsPak(string path) =>
        path.EndsWith(".pak", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<ImportOutcome> ImportMany(
        IEnumerable<string> paths, string libraryRoot, ImportOptions? options = null)
    {
        options ??= new ImportOptions();
        var results = new List<ImportOutcome>();

        foreach (var path in paths)
        {
            if (options.Cancellation.IsCancellationRequested) break;

            options.Progress?.Report("正在导入 " + Path.GetFileName(path));
            try
            {
                results.Add(ImportOne(path, libraryRoot, options));
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "导入 " + path);
                results.Add(ImportOutcome.Fail(path, Path.GetFileNameWithoutExtension(path), ex.Message));
            }
        }

        return results;
    }

    public static ImportOutcome ImportOne(string path, string libraryRoot, ImportOptions? options = null)
    {
        options ??= new ImportOptions();
        var source = path;

        if (string.IsNullOrWhiteSpace(path))
            return ImportOutcome.Fail(source, "", "路径为空");

        if (!Directory.Exists(path) && !File.Exists(path))
            return ImportOutcome.Fail(source, Path.GetFileName(path), "文件不存在");

        if (!Directory.Exists(path))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".zip" or ".rar" or ".7z" or ".pak"))
                return ImportOutcome.Fail(source, Path.GetFileName(path), "不支持的格式：" + ext);
        }

        Directory.CreateDirectory(libraryRoot);
        var staging = Path.Combine(AppPaths.ImportTempDir, "imp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        var createdDest = (string?)null;

        try
        {
            string contentRoot;
            string rawName;

            if (Directory.Exists(path))
            {
                contentRoot = FindContentRoot(path);
                rawName = new DirectoryInfo(path.TrimEnd('\\')).Name;
            }
            else if (IsPak(path))
            {
                var target = Path.Combine(staging, Path.GetFileName(path));
                File.Copy(path, target, true);
                contentRoot = staging;
                rawName = Path.GetFileNameWithoutExtension(path);
            }
            else if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                options.Progress?.Report("正在解压 " + Path.GetFileName(path));
                ZipFile.ExtractToDirectory(path, staging, true);
                contentRoot = FindContentRoot(staging);
                rawName = Path.GetFileNameWithoutExtension(path);
            }
            else
            {
                options.Progress?.Report("正在解压 " + Path.GetFileName(path));
                ExtractWith7Zip(path, staging);
                contentRoot = FindContentRoot(staging);
                rawName = Path.GetFileNameWithoutExtension(path);
            }

            options.Cancellation.ThrowIfCancellationRequested();

            var display = CleanName(rawName);
            var baseName = display;
            var counter = 2;
            while (options.NameExists?.Invoke(display) == true)
                display = baseName + " (" + counter++ + ")";

            var folderName = SafeFolderName(display);
            var dest = Path.Combine(libraryRoot, folderName);
            var suffix = 2;
            while (Directory.Exists(dest) || File.Exists(dest))
            {
                folderName = SafeFolderName(display + " (" + suffix++ + ")");
                dest = Path.Combine(libraryRoot, folderName);
            }

            options.Progress?.Report("正在写入 " + display);
            CopyContent(contentRoot, dest);
            createdDest = dest;

            var record = BuildRecord(display, folderName, dest, options.SourceLabel, rawName);

            return new ImportOutcome
            {
                Ok = true,
                Source = source,
                DisplayName = display,
                Message = record.IsLegacyPak ? "旧式 pak（5.50+ 不生效）" : "文件夹模组",
                Record = record
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (createdDest != null) FileOps.TryDeleteDirectory(createdDest);
            AppLog.Error(ex, "导入 " + source);
            return ImportOutcome.Fail(source, Path.GetFileNameWithoutExtension(source), ex.Message);
        }
        finally
        {
            FileOps.TryDeleteDirectory(staging);
        }
    }

    private static void CopyContent(string contentRoot, string dest)
    {
        Directory.CreateDirectory(dest);
        FileOps.CopyTree(contentRoot, dest, path =>
        {
            var name = Path.GetFileName(path);
            return JunkNames.Contains(name, StringComparer.OrdinalIgnoreCase);
        });
    }

    private static ModRecord BuildRecord(string display, string folderName, string dest, string source, string rawName)
    {
        var paks = new List<string>();
        var hasExmlOrMbin = false;

        try
        {
            foreach (var file in Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (ext.Equals(".pak", StringComparison.OrdinalIgnoreCase))
                    paks.Add(Path.GetRelativePath(dest, file));
                else if (ext.Equals(".EXML", StringComparison.OrdinalIgnoreCase) ||
                         ext.Equals(".MBIN", StringComparison.OrdinalIgnoreCase))
                    hasExmlOrMbin = true;
            }
        }
        catch { }

        var legacyPaks = paks.Count > 0 && !hasExmlOrMbin ? paks : new List<string>();
        var parsed = VersionParser.Parse(rawName);

        return new ModRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = display,
            Source = source,
            Version = parsed?.Raw ?? "",
            FolderName = folderName,
            PakFiles = legacyPaks,
            Enabled = true,
            Size = FileOps.DirSize(dest),
            ImportedAt = DateTimeOffset.Now,
            Note = legacyPaks.Count > 0
                ? "旧式 pak 模组，5.50 之后不再加载"
                : ""
        };
    }

    /// <summary>压缩包常带一层同名根目录；也兼容 GAMEDATA\MODS\&lt;mod&gt; 这种整包结构。</summary>
    private static string FindContentRoot(string root)
    {
        try
        {
            var current = root;

            for (var depth = 0; depth < 4; depth++)
            {
                var dirs = Directory.GetDirectories(current);
                var files = Directory.GetFiles(current);

                if (dirs.Length == 0) return current;

                if (files.Length == 0 && dirs.Length == 1)
                {
                    current = dirs[0];
                    continue;
                }

                // GAMEDATA\MODS\<mod> / GAMEDATA\PCBANKS\MODS\<mod>
                var gamedata = dirs.FirstOrDefault(d =>
                    Path.GetFileName(d).Equals("GAMEDATA", StringComparison.OrdinalIgnoreCase));
                if (gamedata != null && files.Length == 0)
                {
                    var mods = Path.Combine(gamedata, "MODS");
                    if (Directory.Exists(mods))
                    {
                        var inner = Directory.GetDirectories(mods);
                        if (inner.Length == 1) { current = inner[0]; continue; }
                        if (inner.Length > 1) return mods;
                    }
                }

                break;
            }

            // 若唯一子目录是 __MACOSX 之类的垃圾，继续往下剥
            while (Directory.GetFiles(current).Length == 0 &&
                   Directory.GetDirectories(current).Length == 1 &&
                   JunkNames.Contains(Path.GetFileName(Directory.GetDirectories(current)[0]), StringComparer.OrdinalIgnoreCase))
            {
                current = Directory.GetDirectories(current)[0];
            }

            return current;
        }
        catch
        {
            return root;
        }
    }

    private static void ExtractWith7Zip(string archive, string destination)
    {
        var sevenZip = Find7Zip();
        if (sevenZip == null)
            throw new InvalidOperationException("RAR/7z 需要本机安装 7-Zip。请把压缩包转成 zip，或安装 7-Zip 后重试。");

        var psi = new ProcessStartInfo
        {
            FileName = sevenZip,
            Arguments = $"x \"{archive}\" -o\"{destination}\" -y",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 7-Zip");
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();

        if (!process.WaitForExit(180_000))
        {
            try { process.Kill(true); } catch { }
            throw new InvalidOperationException("7-Zip 解压超时");
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException("7-Zip 解压失败，退出码 " + process.ExitCode);
    }

    private static string? Find7Zip()
    {
        var candidates = new[]
        {
            @"C:\Program Files\7-Zip\7z.exe",
            @"C:\Program Files (x86)\7-Zip\7z.exe"
        };

        foreach (var candidate in candidates)
            if (File.Exists(candidate)) return candidate;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var exe = Path.Combine(dir.Trim().Trim('"'), "7z.exe");
                if (File.Exists(exe)) return exe;
            }
            catch { }
        }

        return null;
    }

    public static string CleanName(string? raw)
    {
        var s = string.IsNullOrWhiteSpace(raw) ? "未命名模组" : raw;
        s = s.Replace('_', ' ');

        // Nexus 下载名里的时间戳与哈希：2026-09-11T21-57Z RH1TdJIDx
        s = Regex.Replace(s, @"\s*\d{4}-\d{2}-\d{2}T[\d\-]+Z(\s+[A-Za-z0-9]{6,})?", "");
        // 末尾的纯数字 ID：-1944 / 4241
        s = Regex.Replace(s, @"[\s\-]+\d{3,5}$", "");
        s = Regex.Replace(s, @"\s{2,}", " ").Trim();

        if (s.Length > 70) s = s[..70].Trim();
        return string.IsNullOrWhiteSpace(s) ? "未命名模组" : s;
    }

    public static string SafeFolderName(string name) => FileOps.SafeFileName(name, 80);
}

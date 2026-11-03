using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AllModsSky.Core.Infrastructure;

/// <summary>应用的各个数据目录（索引、设置、缓存、日志）。</summary>
public static class AppPaths
{
    private static string? _rootOverride;
    private static string? _resolvedRoot;
    private static string? _legacyOverride;

    /// <summary>覆盖数据根目录（仅用于自检/便携模式）。</summary>
    public static void UseRoot(string root)
    {
        _rootOverride = root;
        _resolvedRoot = null;
    }

    /// <summary>覆盖旧版数据目录（仅用于自检）。</summary>
    public static void UseLegacyRoot(string root) => _legacyOverride = root;

    /// <summary>
    /// 数据根目录。优先级：--data 参数 → 环境变量 → 旧版兼容的 %APPDATA%\NMSModManager
    /// → %APPDATA%\AllModsSky → 本地 AppData → exe 旁的 NMSModManager.data / data → 临时目录。
    /// 逐项探测可写；已有旧版数据时优先复用，保证 library.json / settings.json 可直接互读。
    /// </summary>
    public static string Root
    {
        get
        {
            if (_rootOverride != null) return _rootOverride;
            return _resolvedRoot ??= ResolveRoot();
        }
    }

    private static string ResolveRoot()
    {
        foreach (var candidate in RootCandidates())
        {
            if (TryPrepare(candidate)) return candidate;
        }

        return Path.Combine(Path.GetTempPath(), "AllModsSky");
    }

    private static bool HasData(string dir)
    {
        try
        {
            return File.Exists(Path.Combine(dir, "settings.json"))
                   || File.Exists(Path.Combine(dir, "library.json"));
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> RootCandidates()
    {
        string[] args;
        try { args = Environment.GetCommandLineArgs(); }
        catch { args = Array.Empty<string>(); }

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--data=", StringComparison.OrdinalIgnoreCase) && arg.Length > 7)
                yield return arg[7..].Trim('"');
            else if (arg.Equals("--data", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                yield return args[i + 1];
        }

        var fromEnv = Environment.GetEnvironmentVariable("ALLMODSSKY_DATA");
        if (!string.IsNullOrWhiteSpace(fromEnv)) yield return fromEnv;

        var exeDir = AppContext.BaseDirectory;

        // 旧版 NMSModManager 的数据格式/目录优先，便于直接读取原来的 library.json。
        // 如果旧目录没有数据、而 AllModsSky 已有数据，则先保住新版数据，再回退旧目录。
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(roaming))
        {
            var oldRoot = Path.Combine(roaming, "NMSModManager");
            var newRoot = Path.Combine(roaming, "AllModsSky");
            var oldHasData = HasData(oldRoot);
            var newHasData = HasData(newRoot);

            if (oldHasData)
            {
                yield return oldRoot;
                yield return newRoot;
            }
            else if (newHasData)
            {
                yield return newRoot;
                yield return oldRoot;
            }
            else
            {
                yield return oldRoot;
                yield return newRoot;
            }
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local))
        {
            yield return Path.Combine(local, "NMSModManager");
            yield return Path.Combine(local, "AllModsSky");
        }

        yield return Path.Combine(exeDir, "NMSModManager.data");
        yield return Path.Combine(exeDir, "data");

        string? tempRoot = null;
        try { tempRoot = Path.Combine(Path.GetTempPath(), "AllModsSky"); }
        catch { }

        if (tempRoot != null) yield return tempRoot;
    }

    private static bool TryPrepare(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;

        try
        {
            var full = Path.GetFullPath(dir);
            Directory.CreateDirectory(full);
            var probe = Path.Combine(full, ".allmodssky_probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string DataFile => Path.Combine(Root, "library.json");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string CacheDir => Path.Combine(Root, "cache");
    public static string ImportTempDir => Path.Combine(CacheDir, "import");
    public static string DownloadDir => Path.Combine(CacheDir, "download");

    /// <summary>日志目录：现场日志 + 归档包都放这里，不和 library.json 混在根目录。</summary>
    public static string LogsDir => Path.Combine(Root, "logs");

    /// <summary>当前会话的日志，每次启动重新开一份。</summary>
    public static string LogFile => Path.Combine(LogsDir, "latest.log");

    public static string CrashFile => Path.Combine(LogsDir, "crash.log");

    /// <summary>旧版 All Mod's Sky / NMSModManager 的数据目录，用于一次性迁移。</summary>
    public static string LegacyRoot => _legacyOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NMSModManager");

    public static void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(ImportTempDir);
        Directory.CreateDirectory(DownloadDir);
        Directory.CreateDirectory(LogsDir);
    }

    /// <summary>最近一次目录写入探测失败的原因。</summary>
    public static string? LastProbeError { get; private set; }

    /// <summary>创建并删除一个探针文件，确认目录可写。</summary>
    public static bool CanWrite(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
        {
            LastProbeError = "目录为空";
            return false;
        }

        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".allmodssky_probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            LastProbeError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastProbeError = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }
}

/// <summary>JSON 读写：缩进、中文不转义、原子落盘并保留 .bak。</summary>
public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (Exception ex)
        {
            AppLog.Warn("JSON 解析失败：" + ex.Message);
            return null;
        }
    }

    public static T? Load<T>(string path) where T : class
    {
        var text = ReadText(path);
        return string.IsNullOrWhiteSpace(text) ? null : Deserialize<T>(text);
    }

    /// <summary>
    /// 区分「文件不存在」「解析失败」的读取。返回 false 表示文件存在但内容无法解析（损坏）；
    /// fileExists 用于区分首次运行与损坏两种情况。
    /// </summary>
    public static bool TryLoad<T>(string path, out T? value, out bool fileExists) where T : class
    {
        value = null;
        fileExists = false;

        string? text;
        try
        {
            if (!File.Exists(path)) return true;
            fileExists = true;
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            // 读不到不等于损坏（可能是权限/占用），交给调用方按「文件不存在」处理。
            AppLog.Warn($"读取失败 {path}：{ex.Message}");
            fileExists = false;
            return true;
        }

        // 0 字节通常是被截断：断电或写入未落盘。
        if (string.IsNullOrWhiteSpace(text)) return false;

        try
        {
            value = JsonSerializer.Deserialize<T>(text, Options);
            return value != null;
        }
        catch (Exception ex)
        {
            AppLog.Warn("JSON 解析失败：" + ex.Message);
            return false;
        }
    }

    public static string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"读取失败 {path}：{ex.Message}");
            return null;
        }
    }

    public static void Save<T>(string path, T value) => WriteText(path, Serialize(value));

    /// <summary>先写临时文件再替换，避免断电/崩溃留下半个 JSON。</summary>
    public static void WriteText(string path, string text)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";

        // 显式 fsync：File.WriteAllText 不保证落盘，断电后会留下 0 字节文件。
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(fs, new UTF8Encoding(false)))
        {
            writer.Write(text);
            writer.Flush();
            fs.Flush(true);
        }

        if (File.Exists(path))
        {
            try
            {
                File.Replace(tmp, path, path + ".bak", true);
                return;
            }
            catch (IOException) { }
            catch (PlatformNotSupportedException) { }
        }

        File.Move(tmp, path, true);
    }
}

/// <summary>跨卷安全的文件/目录操作。</summary>
public static class FileOps
{
    public static void MoveDirectory(string src, string dst)
    {
        if (!Directory.Exists(src)) return;

        var parent = Path.GetDirectoryName(dst);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

        try
        {
            Directory.Move(src, dst);
            return;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        // 跨卷：先完整复制到暂存目录，成功后再替换目标、最后删源。
        // 反过来（先删 dst 再复制）在复制中途失败时会两头都残缺。
        var staging = dst + ".nmsmm_tmp";
        try
        {
            TryDeleteDirectory(staging);
            CopyTree(src, staging);
            if (Directory.Exists(dst)) Directory.Delete(dst, true);
            Directory.Move(staging, dst);
            Directory.Delete(src, true);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    public static void MoveFile(string src, string dst)
    {
        if (!File.Exists(src)) return;

        var dir = Path.GetDirectoryName(dst);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        try
        {
            File.Move(src, dst, true);
            return;
        }
        catch (IOException) { }

        File.Copy(src, dst, true);
        File.Delete(src);
    }

    public static void CopyTree(string source, string dest, Func<string, bool>? skip = null)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (skip != null && skip(file)) continue;
            var target = Path.Combine(dest, Path.GetFileName(file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }

        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            if (skip != null && skip(dir)) continue;

            // 不跟随 junction / 符号链接：否则可能一路复制到系统目录，或陷入递归。
            if (IsReparsePoint(dir)) continue;

            CopyTree(dir, Path.Combine(dest, Path.GetFileName(dir)), skip);
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true; // 读不到属性时按「不安全」处理，跳过
        }
    }

    public static long DirSize(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return 0;
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch { }
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }

    public static void TryDeleteDirectory(string? dir)
    {
        if (string.IsNullOrEmpty(dir)) return;
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"删除目录失败 {dir}：{ex.Message}");
        }
    }

    public static void TryDeleteFile(string? file)
    {
        if (string.IsNullOrEmpty(file)) return;
        try
        {
            if (File.Exists(file)) File.Delete(file);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"删除文件失败 {file}：{ex.Message}");
        }
    }

    /// <summary>去掉非法文件名字符，限制长度，并防止以点/空格结尾。</summary>
    public static string SafeFileName(string? name, int maxLength = 80)
    {
        var raw = (name ?? "").Trim();
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (c < 32 || c == '\\' || c == '/' || c == ':' || c == '*' || c == '?' ||
                c == '"' || c == '<' || c == '>' || c == '|')
                sb.Append('_');
            else
                sb.Append(c);
        }

        var s = sb.ToString().Trim().TrimEnd('.');
        if (s.Length > maxLength) s = s[..maxLength].Trim();
        if (s.Length == 0) s = "mod_" + Guid.NewGuid().ToString("N")[..6];
        return s;
    }

    public static bool PathEquals(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd('\\'),
                Path.GetFullPath(b).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>数字/大小/时间的统一格式化。</summary>
public static class Humanize
{
    public static string Size(long bytes)
    {
        if (bytes <= 0) return "0 KB";
        double kb = bytes / 1024.0;
        if (kb < 1024)
            return (kb < 10 ? kb.ToString("0.#", CultureInfo.InvariantCulture) : kb.ToString("0", CultureInfo.InvariantCulture)) + " KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        return (mb / 1024.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
    }

    public static string Delta(double bytes)
    {
        var sign = bytes < 0 ? "-" : "";
        return sign + Size((long)Math.Abs(bytes));
    }

    public static string DateTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>
/// 轻量文件日志，写失败也绝不影响主流程。
/// 现场日志写 logs/latest.log；每次启动、以及超过 2MB 时把它压成
/// logs/2026-11-02-143000.log.gz，只留最近 <see cref="KeepArchives"/> 份。
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private const long MaxBytes = 2 * 1024 * 1024;
    private const int KeepArchives = 5;
    private const string ArchiveSuffix = ".log.gz";

    /// <summary>上次尝试归档的时刻。压缩失败时别每写一行都重试一遍。</summary>
    private static DateTime _lastArchiveAttemptUtc = DateTime.MinValue;

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(Exception ex, string? context = null)
    {
        var title = string.IsNullOrEmpty(context) ? "异常" : context;
        Write("ERROR", title + " :: " + ex);
    }

    /// <summary>
    /// 启动时调用：把旧版散在根目录的日志收进 logs/，归档上一次会话，清掉过老的包。
    /// 任何一步失败都只是少一份历史日志，不影响启动。
    /// </summary>
    public static void StartSession()
    {
        try
        {
            AppPaths.Ensure();
            lock (Gate)
            {
                ImportLegacyFiles();
                ArchiveCurrent();
                Prune();
                File.AppendAllText(AppPaths.LogFile,
                    $"{Stamp()} [INFO] ===== 会话开始 ====={Environment.NewLine}");
            }
        }
        catch
        {
            // 日志目录不可写也不能挡住启动
        }
    }

    /// <summary>现有归档包，按时间从新到旧。</summary>
    public static IReadOnlyList<FileInfo> ListArchives()
    {
        try
        {
            return new DirectoryInfo(AppPaths.LogsDir)
                .GetFiles("*" + ArchiveSuffix)
                .OrderByDescending(f => f.Name, StringComparer.Ordinal)
                .ToList();
        }
        catch
        {
            return Array.Empty<FileInfo>();
        }
    }

    private static void Write(string level, string message)
    {
        try
        {
            AppPaths.Ensure();
            lock (Gate)
            {
                var file = new FileInfo(AppPaths.LogFile);
                if (file.Exists && file.Length > MaxBytes
                    && DateTime.UtcNow - _lastArchiveAttemptUtc > TimeSpan.FromMinutes(5))
                {
                    ArchiveCurrent();
                    Prune();
                }

                File.AppendAllText(AppPaths.LogFile,
                    $"{Stamp()} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志本身不应抛异常
        }
    }

    private static string Stamp() =>
        DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>把当前日志压成包；空文件直接忽略。</summary>
    private static void ArchiveCurrent()
    {
        try
        {
            var file = new FileInfo(AppPaths.LogFile);
            if (!file.Exists || file.Length == 0) return;

            // 只有真的尝试过才计入节流，否则空文件也会把下一次归档推迟 5 分钟
            _lastArchiveAttemptUtc = DateTime.UtcNow;
            Compress(AppPaths.LogFile, ArchivePath(file.LastWriteTime));
        }
        catch
        {
            // 归档失败就继续往原文件里写
        }
    }

    /// <summary>
    /// 旧版把 app.log / app.log.1 / crash.log 直接放在数据根目录。
    /// 这里一次性收进 logs/：两个日志压成归档包（文件名用它们自己的最后写入时间），
    /// 崩溃记录并进 logs/crash.log，收完删掉原件。
    /// </summary>
    private static void ImportLegacyFiles()
    {
        var root = AppPaths.Root;
        var legacyCurrent = Path.Combine(root, "app.log");
        var legacyPrevious = Path.Combine(root, "app.log.1");
        var legacyCrash = Path.Combine(root, "crash.log");

        foreach (var legacy in new[] { legacyCurrent, legacyPrevious })
        {
            if (!File.Exists(legacy)) continue;
            Compress(legacy, ArchivePath(File.GetLastWriteTime(legacy)));
        }

        if (File.Exists(legacyCrash))
        {
            try
            {
                File.AppendAllText(AppPaths.CrashFile, File.ReadAllText(legacyCrash));
                File.Delete(legacyCrash);
            }
            catch
            {
                // 收编失败就留在原地，下次启动再试
            }
        }
    }

    /// <summary>归档路径：2026-11-02-143000.log.gz，同一秒重名时加 -2、-3。</summary>
    private static string ArchivePath(DateTime when)
    {
        var stamp = when.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(AppPaths.LogsDir, stamp + ArchiveSuffix);

        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(AppPaths.LogsDir, $"{stamp}-{n}{ArchiveSuffix}");

        return path;
    }

    /// <summary>压缩后删掉原文件；压缩失败保留原文件，绝不吞掉日志。</summary>
    private static bool Compress(string source, string target)
    {
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize))
            {
                input.CopyTo(gzip);
            }

            File.Delete(source);
            return true;
        }
        catch (Exception ex)
        {
            TryDelete(target);
            // 这里不能走 AppLog.Warn：写日志又可能触发归档，压缩一直失败就会递归
            System.Diagnostics.Debug.WriteLine($"归档日志失败，保留原文件 {source}：{ex.Message}");
            return false;
        }
    }

    /// <summary>只留最近 KeepArchives 份。文件名是定宽时间戳，按名字倒序就是按时间倒序。</summary>
    private static void Prune()
    {
        foreach (var stale in ListArchives().Skip(KeepArchives))
        {
            try { stale.Delete(); } catch { }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

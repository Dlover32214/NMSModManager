using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AllModsSky.Core;

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
    public static string LogFile => Path.Combine(Root, "app.log");
    public static string CrashFile => Path.Combine(Root, "crash.log");

    /// <summary>旧版 All Mod's Sky / NMSModManager 的数据目录，用于一次性迁移。</summary>
    public static string LegacyRoot => _legacyOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NMSModManager");

    public static void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(ImportTempDir);
        Directory.CreateDirectory(DownloadDir);
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
        File.WriteAllText(tmp, text, new UTF8Encoding(false));

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

        if (Directory.Exists(dst)) Directory.Delete(dst, true);
        CopyTree(src, dst);
        Directory.Delete(src, true);
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
            CopyTree(dir, Path.Combine(dest, Path.GetFileName(dir)), skip);
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

/// <summary>轻量文件日志，写失败也绝不影响主流程。</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private const long MaxBytes = 2 * 1024 * 1024;

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(Exception ex, string? context = null)
    {
        var title = string.IsNullOrEmpty(context) ? "异常" : context;
        Write("ERROR", title + " :: " + ex);
    }

    private static void Write(string level, string message)
    {
        try
        {
            AppPaths.Ensure();
            lock (Gate)
            {
                var file = new FileInfo(AppPaths.LogFile);
                if (file.Exists && file.Length > MaxBytes)
                {
                    try { File.Move(AppPaths.LogFile, AppPaths.LogFile + ".1", true); }
                    catch { }
                }

                File.AppendAllText(AppPaths.LogFile,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志本身不应抛异常
        }
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AllModsSky.Core;

/// <summary>设置文件的读写（单例缓存）。</summary>
public static class SettingsStore
{
    private static AppSettings? _current;

    public static AppSettings Current => _current ??= Load();

    public static string? LastSaveError { get; private set; }

    public static AppSettings Load()
    {
        var loaded = JsonStore.Load<AppSettings>(AppPaths.SettingsFile);

        // 新格式不存在/解析失败时，尝试沿用旧版设置；下次保存会写成兼容格式
        if (loaded == null)
            loaded = LegacyDataMigrator.ReadSettings();

        _current = loaded ?? new AppSettings();
        return _current;
    }

    public static void Use(AppSettings settings) => _current = settings;

    public static void Reset() => _current = null;

    public static bool Save()
    {
        try
        {
            AppPaths.Ensure();

            // 兼容旧版格式：旧设置里有一个独立 LibraryPath 字段。
            // 新版实际库就是游戏 MODS，这里只补一个可读路径，供旧版读取。
            if (string.IsNullOrWhiteSpace(Current.LibraryPath))
                Current.LibraryPath = Path.Combine(AppPaths.Root, "library");

            JsonStore.Save(AppPaths.SettingsFile, Current);
            LastSaveError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastSaveError = ex.Message;
            AppLog.Error(ex, "保存设置");
            return false;
        }
    }
}

/// <summary>定位无人深空安装目录、版本线索与存档目录。</summary>
public static class GameLocator
{
    public const string SteamAppId = "275850";
    public const string GameFolderName = "No Man's Sky";

    public static bool IsValid(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        try
        {
            return Directory.Exists(Path.Combine(dir, "GAMEDATA", "PCBANKS"))
                || File.Exists(Path.Combine(dir, "Binaries", "NMS.exe"));
        }
        catch
        {
            return false;
        }
    }

    public static string ModsDir(string gameDir) => Path.Combine(gameDir, "GAMEDATA", "MODS");
    public static string ParkedDir(string gameDir) => Path.Combine(gameDir, "GAMEDATA", "MODS_disabled");
    public static string PcbanksDir(string gameDir) => Path.Combine(gameDir, "GAMEDATA", "PCBANKS");
    public static string DisableModsFile(string gameDir) => Path.Combine(PcbanksDir(gameDir), "DISABLEMODS.TXT");

    public static string? Detect(string? preferred = null)
    {
        if (IsValid(preferred)) return Clean(preferred!);

        foreach (var library in SteamLibraries())
        {
            var candidate = Path.Combine(library, "steamapps", "common", GameFolderName);
            if (IsValid(candidate)) return Clean(candidate);
        }

        foreach (var candidate in CommonInstallPaths())
            if (IsValid(candidate)) return Clean(candidate);

        return null;
    }

    public static IEnumerable<string> SteamLibraries()
    {
        var libraries = new List<string>();
        var steamRoots = new List<string>();

        void AddRoot(string? root)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            root = root.Replace('/', '\\').TrimEnd('\\');
            if (Directory.Exists(root) && !steamRoots.Contains(root, StringComparer.OrdinalIgnoreCase))
                steamRoots.Add(root);
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            AddRoot(key?.GetValue("SteamPath") as string);
        }
        catch { }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            AddRoot(key?.GetValue("InstallPath") as string);
        }
        catch { }

        AddRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        AddRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));

        foreach (var drive in SafeDrives())
        {
            AddRoot(Path.Combine(drive, "Steam"));
            AddRoot(Path.Combine(drive, "SteamLibrary"));
            AddRoot(Path.Combine(drive, "Program Files (x86)", "Steam"));
            AddRoot(Path.Combine(drive, "Games", "Steam"));
        }

        foreach (var root in steamRoots)
        {
            if (!libraries.Contains(root, StringComparer.OrdinalIgnoreCase)) libraries.Add(root);

            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            try
            {
                if (!File.Exists(vdf)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    var path = m.Groups[1].Value.Replace("\\\\", "\\").TrimEnd('\\');
                    if (Directory.Exists(path) && !libraries.Contains(path, StringComparer.OrdinalIgnoreCase))
                        libraries.Add(path);
                }
            }
            catch { }
        }

        return libraries;
    }

    private static IEnumerable<string> CommonInstallPaths()
    {
        foreach (var drive in SafeDrives())
        {
            yield return Path.Combine(drive, "SteamLibrary", "steamapps", "common", GameFolderName);
            yield return Path.Combine(drive, "Steam", "steamapps", "common", GameFolderName);
            yield return Path.Combine(drive, "Games", "Steam", "steamapps", "common", GameFolderName);
            yield return Path.Combine(drive, "GOG Games", GameFolderName);
        }

        yield return @"C:\Program Files (x86)\Steam\steamapps\common\No Man's Sky";
        yield return @"C:\Program Files\Steam\steamapps\common\No Man's Sky";
        yield return @"C:\Program Files\Epic Games\NoMansSky";
    }

    private static IEnumerable<string> SafeDrives()
    {
        string[] drives;
        try { drives = Directory.GetLogicalDrives(); }
        catch { yield break; }

        foreach (var drive in drives)
        {
            if (string.IsNullOrWhiteSpace(drive)) continue;
            yield return drive.TrimEnd('\\');
        }
    }

    private static string Clean(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd('\\');
        // 用户可能选到了 Binaries 或 GAMEDATA 这一层，往上找一层包含 Binaries 的目录
        var probe = new DirectoryInfo(full);
        for (var i = 0; i < 3 && probe != null; i++)
        {
            if (IsValid(probe.FullName)) return probe.FullName;
            probe = probe.Parent;
        }
        return full;
    }

    /// <summary>游戏版本：优先用户设置，其次从存档备份文件名推断（backup.steam.*.560.zip → 5.60）。</summary>
    public static string DetectGameVersion(string? gameDir, string? configuredVersion = null)
    {
        var configured = configuredVersion ?? SettingsStore.Current.GameVersion;
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();

        try
        {
            var saveRoot = SaveRoot();
            if (!Directory.Exists(saveRoot)) return "";

            var versions = new List<double>();
            foreach (var file in Directory.EnumerateFiles(saveRoot, "backup.*.zip", SearchOption.AllDirectories))
            {
                var m = Regex.Match(Path.GetFileName(file), @"\.(\d{3,4})\.zip$", RegexOptions.IgnoreCase);
                if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Number,
                        CultureInfo.InvariantCulture, out var raw))
                    versions.Add(raw / 100.0);
            }

            return versions.Count == 0 ? "" : versions.Max().ToString("0.00", CultureInfo.InvariantCulture);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Steam 清单里的 buildid。</summary>
    public static string DetectBuildId(string? gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir)) return "";
        try
        {
            var dir = new DirectoryInfo(Path.GetFullPath(gameDir).TrimEnd('\\'));
            for (var i = 0; i < 6 && dir != null; i++)
            {
                var acf = Path.Combine(dir.FullName, "appmanifest_" + SteamAppId + ".acf");
                if (File.Exists(acf))
                {
                    var m = Regex.Match(File.ReadAllText(acf), "\"buildid\"\\s+\"(\\d+)\"");
                    if (m.Success) return m.Groups[1].Value;
                }
                dir = dir.Parent;
            }
        }
        catch { }
        return "";
    }

    /// <summary>NMS.exe 的文件版本（内部构建号）。</summary>
    public static string DetectExeBuild(string? gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir)) return "";
        try
        {
            var exe = Path.Combine(gameDir, "Binaries", "NMS.exe");
            if (!File.Exists(exe)) return "";
            var info = FileVersionInfo.GetVersionInfo(exe);
            return info.FileVersion ?? "";
        }
        catch
        {
            return "";
        }
    }

    public static string SaveRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HelloGames", "NMS");

    /// <summary>存档目录：%APPDATA%\HelloGames\NMS\st_*，取最近写入的那个。</summary>
    public static string? FindSaveDir()
    {
        try
        {
            var root = SaveRoot();
            if (!Directory.Exists(root)) return null;

            string? best = null;
            var bestTime = DateTime.MinValue;

            foreach (var dir in Directory.GetDirectories(root, "st_*"))
            {
                var newest = DateTime.MinValue;
                foreach (var file in Directory.GetFiles(dir, "*.hg"))
                {
                    var write = File.GetLastWriteTime(file);
                    if (write > newest) newest = write;
                }

                if (newest > bestTime)
                {
                    bestTime = newest;
                    best = dir;
                }
            }

            return best;
        }
        catch
        {
            return null;
        }
    }

    public static string? FindGameExe(string gameDir)
    {
        var exe = Path.Combine(gameDir, "Binaries", "NMS.exe");
        return File.Exists(exe) ? exe : null;
    }

    /// <summary>汇总一次游戏状态，供主界面与设置页共用。</summary>
    public static GameInfo Inspect(string? gamePath, string? versionOverride = null)
    {
        var valid = IsValid(gamePath);
        if (!valid)
        {
            return new GameInfo { GamePath = gamePath ?? "", IsValid = false };
        }

        var path = Clean(gamePath!);
        var mods = ModsDir(path);
        var writable = AppPaths.CanWrite(mods);
        if (!writable)
            AppLog.Warn($"MODS 写入检查失败：{mods}；{AppPaths.LastProbeError}");

        return new GameInfo
        {
            GamePath = path,
            IsValid = true,
            ModsPath = mods,
            ParkedPath = ParkedDir(path),
            Version = DetectGameVersion(path, versionOverride),
            BuildId = DetectBuildId(path),
            ExeBuild = DetectExeBuild(path),
            DisableModsFileExists = File.Exists(DisableModsFile(path)),
            ModsWritable = writable
        };
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AllModsSky.Core;

public sealed class LegacyData
{
    public bool LibraryFound { get; init; }
    public bool SettingsFound { get; init; }
    public List<ModRecord> Mods { get; init; } = new();
    public AppSettings? Settings { get; init; }
}

/// <summary>
/// 读取旧版 NMSModManager 的 library.json / settings.json，让第一次启动能直接接着用。
/// 旧格式字段：LibraryPath（目录名）、DeployName、PakFiles、ImportedAt 等。
/// </summary>
public static class LegacyDataMigrator
{
    public static LegacyData Read()
    {
        // 旧版目录里只要还有数据，就优先按旧版格式读取（用于首次迁移）；
        // 否则读当前数据根，支持 --data 指向一份旧格式数据。
        var legacyRoot = AppPaths.LegacyRoot;
        var legacyHasData = File.Exists(Path.Combine(legacyRoot, "library.json"))
                            || File.Exists(Path.Combine(legacyRoot, "settings.json"));
        var root = legacyHasData ? legacyRoot : AppPaths.Root;

        var libraryFile = Path.Combine(root, "library.json");
        var settingsFile = Path.Combine(root, "settings.json");

        var libraryFound = File.Exists(libraryFile);
        var settingsFound = File.Exists(settingsFile);

        return new LegacyData
        {
            LibraryFound = libraryFound,
            SettingsFound = settingsFound,
            Mods = libraryFound ? ReadLibrary(libraryFile) : new List<ModRecord>(),
            Settings = settingsFound ? ReadSettings(settingsFile) : null
        };
    }

    public static AppSettings? ReadSettings(string? path = null)
    {
        var file = path ?? Path.Combine(AppPaths.Root, "settings.json");
        if (!File.Exists(file))
            file = Path.Combine(AppPaths.LegacyRoot, "settings.json");

        try
        {
            var text = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            var root = doc.RootElement;

            return new AppSettings
            {
                GamePath = Str(root, "GamePath"),
                GameVersion = Str(root, "GameVersion"),
                AmumssPath = Str(root, "AmumssPath"),
                NexusApiKey = Str(root, "NexusApiKey"),
                Theme = string.IsNullOrWhiteSpace(Str(root, "Theme")) ? "Dark" : Str(root, "Theme"),
                CreateDisableModsFile = Bool(root, "CreateDisableModsFile", true),
                LibraryPath = Str(root, "LibraryPath"),
                BackupOnDeploy = Bool(root, "BackupOnDeploy", true)
            };
        }
        catch (Exception ex)
        {
            AppLog.Warn("读取旧版设置失败：" + ex.Message);
            return null;
        }
    }

    private static List<ModRecord> ReadLibrary(string file)
    {
        var result = new List<ModRecord>();
        try
        {
            var text = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (!doc.RootElement.TryGetProperty("Mods", out var mods) || mods.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var element in mods.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;

                var name = Str(element, "Name");
                if (string.IsNullOrWhiteSpace(name)) continue;

                var folder = Str(element, "LibraryPath");
                if (string.IsNullOrWhiteSpace(folder)) folder = Str(element, "DeployName");
                if (string.IsNullOrWhiteSpace(folder)) folder = FileOps.SafeFileName(name, 80);

                var paks = new List<string>();
                if (element.TryGetProperty("PakFiles", out var pakArray) && pakArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var pak in pakArray.EnumerateArray())
                    {
                        var value = pak.ValueKind == JsonValueKind.String ? pak.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(value)) paks.Add(value!);
                    }
                }

                result.Add(new ModRecord
                {
                    Id = ValueOrEmpty(Str(element, "Id"), Guid.NewGuid().ToString("N")),
                    Name = name,
                    Source = string.IsNullOrWhiteSpace(Str(element, "Source")) ? "本地导入" : Str(element, "Source"),
                    Version = Str(element, "Version"),
                    FolderName = folder,
                    PakFiles = paks,
                    Enabled = Bool(element, "Enabled", true),
                    Size = Long(element, "Size"),
                    ImportedAt = Date(element, "ImportedAt"),
                    Note = Str(element, "Note")
                });
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("读取旧版模组库失败：" + ex.Message);
        }

        return result;
    }

    private static string ValueOrEmpty(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string Str(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => ""
        };
    }

    private static bool Bool(JsonElement obj, string name, bool fallback)
    {
        if (!obj.TryGetProperty(name, out var value)) return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var b) ? b : fallback,
            _ => fallback
        };
    }

    private static long Long(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var l)) return l;
        if (value.ValueKind == JsonValueKind.String &&
            long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return 0;
    }

    private static DateTimeOffset Date(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value)) return DateTimeOffset.Now;

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString() ?? "";

            // JavaScriptSerializer 的 /Date(1730000000000)/
            var match = Regex.Match(text, @"/Date\((-?\d+)\)/");
            if (match.Success && long.TryParse(match.Groups[1].Value, out var ms))
                return DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime();

            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out var parsed))
                return parsed;
        }

        return DateTimeOffset.Now;
    }
}

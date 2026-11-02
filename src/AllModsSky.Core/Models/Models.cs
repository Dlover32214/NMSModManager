using System.Text.Json.Serialization;

namespace AllModsSky.Core.Models;

/// <summary>模组的物理形态。5.50 之后游戏读取散装目录，旧式 .pak 仅作兼容展示。</summary>
public enum ModKind
{
    Folder,
    LegacyPak
}

/// <summary>库中的一条模组记录。模组文件本身就在 GAMEDATA\MODS 里，这里只是索引。</summary>
public sealed class ModRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Source { get; set; } = "本地导入";
    public string Version { get; set; } = "";

    /// <summary>MODS 目录下的文件夹名（也是停用区里的文件夹名）。</summary>
    public string FolderName { get; set; } = "";

    /// <summary>
    /// 旧版 NMSModManager 的库内目录字段。新版仍以 FolderName 为准，
    /// 读写时同步这个字段，让旧版 library.json 可以直接互读。
    /// </summary>
    public string LibraryPath
    {
        get => FolderName;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(FolderName))
                FolderName = value;
        }
    }

    /// <summary>旧版部署目录字段；pak 模组旧版写空字符串。</summary>
    public string DeployName
    {
        get => PakFiles is { Count: > 0 } ? "" : FolderName;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(FolderName))
                FolderName = value;
        }
    }

    /// <summary>旧式 pak 相对文件名；为空表示普通文件夹模组。</summary>
    public List<string> PakFiles { get; set; } = new();

    public bool Enabled { get; set; } = true;
    public long Size { get; set; }
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.Now;
    public string Note { get; set; } = "";

    /// <summary>模组页面链接（Nexus / oogc / 论坛等），用户手动填。空串表示没设。</summary>
    public string Url { get; set; } = "";

    /// <summary>
    /// 整理用户输入的链接：没写协议就补 https://，只接受 http/https。
    /// 返回空串表示「清除链接」，返回 null 表示输入不合法。
    /// </summary>
    public static string? NormalizeUrl(string? input)
    {
        var text = (input ?? "").Trim();
        if (text.Length == 0) return "";

        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        // 只认带点的域名：中文串也能被当成 IDN 域名收下，但那不是用户想要的链接
        var host = uri.Host;
        if (host.Length == 0 || !host.Contains('.', StringComparison.Ordinal)) return null;
        if (host.StartsWith('.') || host.EndsWith('.')) return null;

        return uri.ToString();
    }

    [JsonIgnore]
    public ModKind Kind => PakFiles.Count > 0 ? ModKind.LegacyPak : ModKind.Folder;

    [JsonIgnore]
    public bool IsLegacyPak => Kind == ModKind.LegacyPak;

    [JsonIgnore]
    public string EffectiveFolder => string.IsNullOrWhiteSpace(FolderName) ? Name : FolderName;

    [JsonIgnore]
    public string VersionText => string.IsNullOrWhiteSpace(Version) ? "—" : Version;

    [JsonIgnore]
    public string SizeText => Humanize.Size(Size);
}

/// <summary>持久化设置。</summary>
public sealed class AppSettings
{
    public string GamePath { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public string AmumssPath { get; set; } = "";
    public string NexusApiKey { get; set; } = "";
    public string Theme { get; set; } = "Dark";
    public bool CreateDisableModsFile { get; set; } = true;

    /// <summary>旧版字段：独立库根目录。新版模组就在 MODS 里，保留仅为格式兼容。</summary>
    public string LibraryPath { get; set; } = "";

    /// <summary>旧版字段：部署前备份。新版没有该开关，保留仅为格式兼容。</summary>
    public bool BackupOnDeploy { get; set; } = true;

    [JsonIgnore]
    public bool IsDark => !string.Equals(Theme, "Light", StringComparison.OrdinalIgnoreCase);

    public AppSettings Clone() => new()
    {
        GamePath = GamePath,
        GameVersion = GameVersion,
        AmumssPath = AmumssPath,
        NexusApiKey = NexusApiKey,
        Theme = Theme,
        CreateDisableModsFile = CreateDisableModsFile,
        LibraryPath = LibraryPath,
        BackupOnDeploy = BackupOnDeploy
    };
}

/// <summary>配置里的一条模组：引用 + 在这套配置里的启停状态。顺序即加载顺序。</summary>
public sealed class ConfigEntry
{
    public string ModId { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// 一套配置：一组模组，以及它们各自的启停状态。
/// 配置是独立集合——切到某套配置后，列表只显示它包含的模组，其余的进「模组库」。
/// </summary>
public sealed class ModProfile
{
    /// <summary>内置「无模组」的固定 Id。</summary>
    public const string NoneId = "builtin-none";

    /// <summary>
    /// 配置名的字数上限。按「字」算（一个汉字、一个字母都算 1），不按 UTF-16 长度算，
    /// 否则 emoji 会被当成 2 个字，名字还没满就被截了。
    /// </summary>
    public const int MaxNameLength = 20;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<ConfigEntry> Entries { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>旧版字段：只记启用的模组 Id。读取时迁移进 Entries，写盘时保持 null 不再输出。</summary>
    [JsonPropertyName("EnabledIds")]
    public List<string>? LegacyEnabledIds { get; set; }

    /// <summary>内置配置：不可改名、不可删除，也不写进 library.json。</summary>
    [JsonIgnore]
    public bool BuiltIn => Id == NoneId;

    /// <summary>
    /// 能不能删。库里只剩这一套时不能删——总得留一套配置干活，否则「添加模组」没有地方放。
    /// 由 <see cref="ModLibrary"/> 在配置增删时维护，不落盘。
    /// </summary>
    [JsonIgnore]
    public bool Deletable { get; set; } = true;

    [JsonIgnore]
    public int EnabledCount => Entries.Count(e => e.Enabled);

    [JsonIgnore]
    public string UpdatedText => Humanize.DateTime(UpdatedAt);

    /// <summary>菜单里一行显示的信息。</summary>
    [JsonIgnore]
    public string SummaryText => BuiltIn
        ? "全部停用 · 直接启动原版"
        : Entries.Count == 0
            ? "没有模组启用"
            : $"{EnabledCount} / {Entries.Count} 个启用 · {UpdatedText}";
}

/// <summary>library.json 的根对象。</summary>
public sealed class LibraryState
{
    public int SchemaVersion { get; set; } = 3;

    /// <summary>模组库：全局池子，顺序只影响池子里的展示。</summary>
    public List<ModRecord> Mods { get; set; } = new();

    /// <summary>用户配置（不含内置「无模组」，它永远存在、不写盘）。</summary>
    public List<ModProfile> Profiles { get; set; } = new();

    /// <summary>当前生效的配置 Id；为空或找不到时回退到第一套配置，再回退到「无模组」。</summary>
    public string ActiveProfileId { get; set; } = "";
}

/// <summary>游戏目录与版本信息快照，供界面展示。</summary>
public sealed class GameInfo
{
    public string GamePath { get; init; } = "";
    public bool IsValid { get; init; }
    public string ModsPath { get; init; } = "";
    public string ParkedPath { get; init; } = "";
    public string Version { get; init; } = "";
    public string BuildId { get; init; } = "";
    public string ExeBuild { get; init; } = "";
    public bool DisableModsFileExists { get; init; }
    public bool ModsWritable { get; init; }

    public string VersionLine
    {
        get
        {
            var parts = new List<string>();
            parts.Add("版本 " + (string.IsNullOrWhiteSpace(Version) ? "未知" : Version));
            if (!string.IsNullOrWhiteSpace(BuildId)) parts.Add("build " + BuildId);
            if (!string.IsNullOrWhiteSpace(ExeBuild)) parts.Add("exe " + ExeBuild);
            return string.Join(" · ", parts);
        }
    }
}

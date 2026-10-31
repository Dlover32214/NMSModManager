using System.Text.Json.Serialization;

namespace AllModsSky.Core;

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

/// <summary>library.json 的根对象。</summary>
public sealed class LibraryState
{
    public int SchemaVersion { get; set; } = 2;
    public List<ModRecord> Mods { get; set; } = new();
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

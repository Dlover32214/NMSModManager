namespace AllModsSky.Core.Services;

/// <summary>模组目录的格式统计（5.50+ 的 EXML 补丁 / MBIN 替换模型）。</summary>
public sealed class ModFormatInfo
{
    public int Exml { get; set; }
    public int Mbin { get; set; }
    public int Pak { get; set; }
    public int Mxml { get; set; }
    public int Lua { get; set; }
    public int Files { get; set; }
    public bool GlobalsMisplaced { get; set; }

    public bool HasNothing => Exml == 0 && Mbin == 0 && Pak == 0;

    public string Type =>
        Exml > 0 && Mbin > 0 ? L.T("混合（EXML + MBIN）")
        : Exml > 0 ? L.T("补丁（EXML）")
        : Mbin > 0 ? L.T("替换（MBIN）")
        : Pak > 0 ? L.T("旧式 .pak")
        : L.T("无有效文件");

    public string Counts
    {
        get
        {
            var parts = new List<string>();
            if (Exml > 0) parts.Add($"EXML {Exml}");
            if (Mbin > 0) parts.Add($"MBIN {Mbin}");
            if (Pak > 0) parts.Add($"pak {Pak}");
            if (Mxml > 0) parts.Add($"MXML {Mxml}");
            if (Lua > 0) parts.Add($"lua {Lua}");
            return parts.Count == 0 ? L.F("{0} 个文件", Files) : string.Join(" · ", parts);
        }
    }
}

/// <summary>一条模组在界面上的健康状态：格式、警告、版本适配。</summary>
public sealed class ModHealth
{
    public string Summary { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Warning { get; init; } = "";
    public bool HasWarning => !string.IsNullOrEmpty(Warning);
    public string CompatState { get; init; } = "Unknown";
    public string CompatText { get; init; } = "";
    public bool HasCompatWarning => CompatState == "Warn";
}

/// <summary>检查模组目录是不是游戏当前版本能加载的形态。</summary>
public static class ModInspector
{
    public const int MaxFolderNameLength = 122;

    public static ModFormatInfo Inspect(string? folder)
    {
        var info = new ModFormatInfo();
        if (string.IsNullOrWhiteSpace(folder)) return info;

        try
        {
            if (!Directory.Exists(folder)) return info;

            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                info.Files++;
                var ext = Path.GetExtension(file);

                if (ext.Equals(".EXML", StringComparison.OrdinalIgnoreCase))
                {
                    info.Exml++;
                    var rel = Path.GetRelativePath(folder, file);
                    var name = Path.GetFileName(file);
                    if (!rel.Contains(Path.DirectorySeparatorChar) &&
                        name.StartsWith("GC", StringComparison.OrdinalIgnoreCase))
                        info.GlobalsMisplaced = true;
                }
                else if (ext.Equals(".MBIN", StringComparison.OrdinalIgnoreCase)) info.Mbin++;
                else if (ext.Equals(".pak", StringComparison.OrdinalIgnoreCase)) info.Pak++;
                else if (ext.Equals(".MXML", StringComparison.OrdinalIgnoreCase)) info.Mxml++;
                else if (ext.Equals(".lua", StringComparison.OrdinalIgnoreCase)) info.Lua++;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"检查模组目录失败 {folder}：{ex.Message}");
        }

        return info;
    }

    public static ModHealth Evaluate(ModRecord record, string? folder, string? gameVersion)
    {
        var info = Inspect(folder);
        var warnings = new List<string>();

        if (info.Pak > 0 && info.Exml == 0 && info.Mbin == 0)
            warnings.Add(L.T("5.50 之后游戏不再读取 .pak，这个模组不会生效"));

        if (info.HasNothing && info.Mxml == 0 && info.Lua == 0)
            warnings.Add(L.T("没有 EXML/MBIN 文件，游戏不会加载"));

        if (info.Mxml > 0 && info.Exml == 0)
            warnings.Add(L.T("游戏不识别 .MXML，需要转成 .EXML"));

        if (info.GlobalsMisplaced)
            warnings.Add(L.T("全局文件的补丁应放进 Globals 子文件夹"));

        var folderName = record.EffectiveFolder;
        if (folderName.Length > MaxFolderNameLength)
            warnings.Add(L.F("文件夹名 {0} 字符，超过 122 上限", folderName.Length));

        var compat = VersionParser.Evaluate(record.Version, gameVersion);
        if (compat.IsWarning) warnings.Add(compat.Text);

        var summary = info.Type + " · " + info.Counts;

        return new ModHealth
        {
            Summary = summary,
            Detail = info.Files > 0 ? L.F("{0} 个文件", info.Files) : L.T("目录为空或不存在"),
            Warning = string.Join("; ", warnings),
            CompatState = compat.State,
            CompatText = compat.Text
        };
    }
}

using System.Diagnostics;

namespace AllModsSky.Core;

/// <summary>
/// 5.50 之后，"模组是否加载"有两套机制同时在起作用：
/// 文件夹在不在 GAMEDATA\MODS，以及 GCMODSETTINGS.MXML 里那条的 Enabled。
/// 本管理器只动文件夹，玩家却可能在游戏内用官方界面把模组关掉——
/// 两边一旦不一致，界面显示的启停就是假的。这里负责对账。
/// </summary>
public enum ModStateDrift
{
    /// <summary>文件夹在 MODS 里（管理器显示启用），但游戏里那条是关闭的：游戏不会加载它。</summary>
    DisabledInGame,

    /// <summary>管理器已停用（文件夹挪走），游戏里那条却还是启用：下次启动游戏仍会加载它。</summary>
    EnabledInGame,

    /// <summary>MODS 里有文件夹，但 MXML 里还没有条目：游戏下一次启动才会登记，无法提前写。</summary>
    MissingInSettings
}

public sealed class ModDriftItem
{
    /// <summary>MXML 里的 Name（大写、XML 转义形式），便于直接和 Read 的结果对照。</summary>
    public string Name { get; init; } = "";

    /// <summary>管理器里的文件夹名，给界面/日志用。</summary>
    public string FolderName { get; init; } = "";

    public ModStateDrift Kind { get; init; }
    public bool ManagerEnabled { get; init; }
    public bool SettingsEnabled { get; init; }

    public string Text => Kind switch
    {
        ModStateDrift.DisabledInGame => $"{FolderName}：游戏内已关闭，管理器显示启用",
        ModStateDrift.EnabledInGame => $"{FolderName}：管理器已停用，游戏内仍是启用",
        _ => $"{FolderName}：游戏设置里还没有这个模组"
    };
}

public sealed class ModStateReport
{
    public List<ModDriftItem> Drifts { get; } = new();

    /// <summary>成功改写的条目数。</summary>
    public int Repaired { get; set; }

    /// <summary>MXML 里没有条目、只能等游戏自己登记的模组数。</summary>
    public int Skipped { get; set; }

    /// <summary>游戏正在运行，本次没有写入（退出时游戏会覆盖设置）。</summary>
    public bool BlockedByGameRunning { get; set; }

    public string? Error { get; set; }
    public bool Ok => Error == null;

    public string Summary
    {
        get
        {
            if (Error != null) return "启用状态对账失败：" + Error;
            if (BlockedByGameRunning) return "游戏正在运行，已跳过启用状态校正";
            if (Drifts.Count == 0) return "启用状态一致";
            var text = Repaired > 0 ? $"校正 {Repaired} 个启用状态" : $"发现 {Drifts.Count} 处不一致";
            if (Skipped > 0) text += $"，{Skipped} 个待游戏登记";
            return text;
        }
    }
}

/// <summary>管理器状态与游戏 GCMODSETTINGS 的 Enabled 对账。</summary>
public sealed class ModStateService
{
    private readonly LoadOrderService _loadOrder = new();
    private readonly DeploymentService _deployment = new();

    /// <summary>只读检测：不会写任何文件。</summary>
    public IReadOnlyList<ModDriftItem> Detect(string? gameDir, IReadOnlyList<ModRecord> mods)
    {
        var drifts = new List<ModDriftItem>();
        if (string.IsNullOrWhiteSpace(gameDir) || !GameLocator.IsValid(gameDir)) return drifts;

        var read = _loadOrder.Read(gameDir);
        if (!read.Ok) return drifts;

        var settings = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in read.Entries) settings[entry.Name] = entry.Enabled;

        foreach (var record in mods)
        {
            // 旧式 .pak 走 PCBANKS\DISABLEMODS.TXT，不受 MXML 的 Enabled 控制
            if (record.IsLegacyPak) continue;

            var xmlName = LoadOrderService.ToXmlName(record.EffectiveFolder);
            if (string.IsNullOrWhiteSpace(xmlName)) continue;

            if (!settings.TryGetValue(xmlName, out var settingsEnabled))
            {
                // 停用中的模组本来就可能没被游戏登记过，这不算问题
                if (record.Enabled && _deployment.Presence(gameDir, record) != ModPresence.Parked)
                    drifts.Add(new ModDriftItem
                    {
                        Name = xmlName,
                        FolderName = record.EffectiveFolder,
                        Kind = ModStateDrift.MissingInSettings,
                        ManagerEnabled = true,
                        SettingsEnabled = false
                    });
                continue;
            }

            if (settingsEnabled == record.Enabled) continue;

            drifts.Add(new ModDriftItem
            {
                Name = xmlName,
                FolderName = record.EffectiveFolder,
                Kind = settingsEnabled ? ModStateDrift.EnabledInGame : ModStateDrift.DisabledInGame,
                ManagerEnabled = record.Enabled,
                SettingsEnabled = settingsEnabled
            });
        }

        return drifts;
    }

    /// <summary>
    /// 以管理器状态为准，把 Enabled 写回 MXML。游戏正在运行时只检测、不写。
    /// 默认 write = true；界面/自检只想看差异时传 false。
    /// </summary>
    public ModStateReport Reconcile(string? gameDir, IReadOnlyList<ModRecord> mods, bool write = true)
    {
        var report = new ModStateReport();

        if (string.IsNullOrWhiteSpace(gameDir) || !GameLocator.IsValid(gameDir))
        {
            report.Error = "游戏目录无效";
            return report;
        }

        foreach (var drift in Detect(gameDir, mods)) report.Drifts.Add(drift);

        if (!write || report.Drifts.Count == 0) return report;

        if (GameProcess.IsRunning())
        {
            report.BlockedByGameRunning = true;
            return report;
        }

        var wanted = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in mods)
        {
            if (record.IsLegacyPak) continue;
            var xmlName = LoadOrderService.ToXmlName(record.EffectiveFolder);
            if (string.IsNullOrWhiteSpace(xmlName)) continue;
            if (!wanted.ContainsKey(xmlName)) wanted[xmlName] = record.Enabled;
        }

        var result = _loadOrder.ApplyEnabledStates(gameDir, wanted);
        if (!result.Ok)
        {
            report.Error = result.Error;
            return report;
        }

        report.Repaired = result.Updated;
        report.Skipped = result.Missing;
        return report;
    }
}

/// <summary>判断无人深空是否正在运行（游戏退出时会回写 GCMODSETTINGS，避免被我们覆盖）。</summary>
public static class GameProcess
{
    private static readonly string[] Names = { "NMS", "NoMansSky" };

    public static bool IsRunning()
    {
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (Names.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase)) return true;
                }
                catch
                {
                    // 受保护/已退出的进程，读不到名字就跳过
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            // 枚举进程失败时按"没在运行"处理，后续写入仍有 .bak + 原子写兜底
        }

        return false;
    }
}

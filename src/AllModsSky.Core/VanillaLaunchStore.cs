using System.Text;
using System.Text.Json;

namespace AllModsSky.Core;

/// <summary>进入原版模式前保存的 Mod 启用状态。</summary>
public sealed class VanillaLaunchState
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public List<string> EnabledIds { get; set; } = new();
}

/// <summary>
/// “启动原版”会把所有模组停用，并把当时的启用 ID 写入这个文件。
/// 下一次“启动 Mod 版”据此恢复；即使中途关闭管理器也能恢复。
/// </summary>
public static class VanillaLaunchStore
{
    public static string FilePath => Path.Combine(AppPaths.Root, "vanilla-launch.json");

    public static bool HasBackup => File.Exists(FilePath);

    public static VanillaLaunchState? Load()
    {
        if (!File.Exists(FilePath)) return null;

        try
        {
            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            var state = JsonSerializer.Deserialize<VanillaLaunchState>(json);
            if (state != null) state.EnabledIds ??= new List<string>();
            return state;
        }
        catch (Exception ex)
        {
            AppLog.Warn("读取原版启动状态失败：" + ex.Message);
            return null;
        }
    }

    public static bool Save(IEnumerable<string> enabledIds)
    {
        try
        {
            AppPaths.Ensure();
            var state = new VanillaLaunchState
            {
                CreatedAt = DateTimeOffset.Now,
                EnabledIds = enabledIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("保存原版启动状态失败：" + ex.Message);
            return false;
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch (Exception ex)
        {
            AppLog.Warn("清除原版启动状态失败：" + ex.Message);
        }
    }
}

using System.Diagnostics;
using AllModsSky.Core;

namespace AllModsSky.App.Services;

/// <summary>打开文件夹 / 浏览器 / 启动游戏。</summary>
public static class Launcher
{
    public static void OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("打开文件夹失败：" + ex.Message);
        }
    }

    public static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("打开链接失败：" + ex.Message);
        }
    }

    public static void LaunchGame(GameInfo game)
    {
        try
        {
            Process.Start(new ProcessStartInfo("steam://rungameid/275850") { UseShellExecute = true });
            return;
        }
        catch { }

        var exe = game.IsValid ? GameLocator.FindGameExe(game.GamePath) : null;
        if (string.IsNullOrEmpty(exe)) return;

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn("启动游戏失败：" + ex.Message);
        }
    }

    /// <summary>当前程序是否可写游戏 MODS 目录（不可写时提示以管理员身份重启）。</summary>
    public static void RestartAsAdmin()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;

            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
            });

            System.Windows.Application.Current?.Shutdown();
        }
        catch (Exception ex)
        {
            AppLog.Warn("以管理员身份重启失败：" + ex.Message);
        }
    }
}

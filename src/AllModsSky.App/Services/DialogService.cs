using System.Windows;
using AllModsSky.Core;
using Microsoft.Win32;

namespace AllModsSky.App.Services;

/// <summary>系统对话框：文件夹选择、模组文件选择、确认/提示。</summary>
public static class DialogService
{
    public static string? PickFolder(string title, string? initial = null)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
            dialog.InitialDirectory = initial;

        var owner = Application.Current?.MainWindow;
        var ok = owner is { IsVisible: true } ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return ok == true ? dialog.FolderName : null;
    }

    public static IReadOnlyList<string>? PickArchives()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要导入的模组（可多选）",
            Multiselect = true,
            Filter = "模组压缩包|*.zip;*.rar;*.7z;*.tar;*.gz;*.tgz;*.bz2;*.tbz2;*.xz;*.txz;*.zst;*.tzst;*.cab;*.pak|所有文件|*.*"
        };

        var owner = Application.Current?.MainWindow;
        var ok = owner is { IsVisible: true } ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return ok == true ? dialog.FileNames : null;
    }

    public static ConfirmResult Confirm(string title, string message, string primary, string? danger = null)
    {
        var dialog = new DialogWindow();
        AttachOwner(dialog);
        dialog.Configure(title, message, primary, danger, alertOnly: false);
        dialog.ShowDialog();
        return dialog.Result;
    }

    public static void Alert(string title, string message)
    {
        var dialog = new DialogWindow();
        AttachOwner(dialog);
        dialog.Configure(title, message, "知道了", null, alertOnly: true);
        dialog.ShowDialog();
    }

    private static void AttachOwner(Window dialog)
    {
        var owner = Application.Current?.MainWindow;
        if (owner is { IsVisible: true }) dialog.Owner = owner;
    }

    public static void ShowError(string title, Exception ex)
    {
        AppLog.Error(ex, title);
        Alert(title, ex.Message);
    }
}

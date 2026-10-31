namespace AllModsSky.App.Services;

public enum PageKey
{
    Library,
    Discover,
    Tools,
    Settings
}

public enum ConfirmResult
{
    None,
    Primary,
    Danger,
    Cancel
}

/// <summary>页面被切换到前台时可以做一次轻量刷新。</summary>
public interface IActivatable
{
    Task OnActivatedAsync();
}

/// <summary>页面与 ViewModel 之间的 UI 能力（提示、对话框、忙碌态、导航）。</summary>
public interface IShellUi
{
    void Toast(string message, bool success = true);

    ConfirmResult Confirm(string title, string message, string primary, string? danger = null);

    void Alert(string title, string message);

    string? PickFolder(string title, string? initial = null);

    IReadOnlyList<string>? PickArchives();

    void BeginBusy(string text = "处理中…");

    void EndBusy();

    void Navigate(PageKey page);

    Task<Core.DeployReport?> DeployAsync(bool silent = false);

    Task ImportAsync(IReadOnlyList<string> paths);

    Task RefreshAllAsync();
}

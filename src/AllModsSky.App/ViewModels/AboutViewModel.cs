using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using AllModsSky.App.Mvvm;
using AllModsSky.App.Services;

namespace AllModsSky.App.ViewModels;

/// <summary>关于页：项目信息、目录、自动检查 GitHub Release、下载并自替换更新。</summary>
public sealed class AboutViewModel : ObservableObject, IActivatable
{
    private readonly IShellUi _ui;
    private UpdateCheckResult? _check;
    private bool _autoChecked;
    private bool _isChecking;
    private bool _isDownloading;
    private bool _hasUpdate;
    private double _downloadProgress;
    private string _latestVersion = "";
    private string _statusText = L.T("进入本页面自动联网检查 GitHub Release 最新版本。");
    private string _releaseNotes = L.T("尚未检查更新。");
    private string _publishedText = "";

    public AboutViewModel(IShellUi ui)
    {
        _ui = ui;

        CheckUpdateCommand = new AsyncRelayCommand(() => CheckUpdateAsync(silent: false));
        DownloadInstallCommand = new AsyncRelayCommand(DownloadAndInstallAsync);
        OpenProjectCommand = new RelayCommand(() => Launcher.OpenUrl(UpdateService.ProjectUrl));
        OpenReleasesCommand = new RelayCommand(() => Launcher.OpenUrl(UpdateService.ReleasesUrl));
        OpenInstallFolderCommand = new RelayCommand(() => Launcher.OpenFolder(AppContext.BaseDirectory));
        OpenDataFolderCommand = new RelayCommand(() => Launcher.OpenFolder(AppPaths.Root));
    }

    public string AppTitle => "All Mod's Sky";
    public string AppSubtitle => "NMS Mod Manager";
    public string CurrentVersion { get; } = AppInfo.Version;
    public string CurrentVersionText => AppInfo.DisplayVersion;
    public string ProjectUrl => UpdateService.ProjectUrl;
    public string ReleasesUrl => UpdateService.ReleasesUrl;
    public string InstallPath => AppContext.BaseDirectory.TrimEnd('\\');
    public string DataPath => AppPaths.Root;

    public string LatestVersionText => string.IsNullOrWhiteSpace(_latestVersion) ? L.T("未检查") : "v" + _latestVersion;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ReleaseNotes
    {
        get => _releaseNotes;
        private set => SetProperty(ref _releaseNotes, value);
    }

    public string PublishedText
    {
        get => _publishedText;
        private set => SetProperty(ref _publishedText, value);
    }

    public bool HasUpdate
    {
        get => _hasUpdate;
        private set
        {
            if (!SetProperty(ref _hasUpdate, value)) return;
            Raise(nameof(CanInstall), nameof(UpdateStateText));
        }
    }

    public bool IsChecking
    {
        get => _isChecking;
        private set => SetProperty(ref _isChecking, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (!SetProperty(ref _isDownloading, value)) return;
            Raise(nameof(CanInstall));
        }
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        private set => SetProperty(ref _downloadProgress, value);
    }

    public bool CanInstall => HasUpdate && _check?.Asset != null && !IsDownloading;

    public string UpdateStateText => HasUpdate
        ? L.T("有新版本可更新")
        : L.T("当前已是最新版本");

    public ICommand CheckUpdateCommand { get; }
    public ICommand DownloadInstallCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand OpenReleasesCommand { get; }
    public ICommand OpenInstallFolderCommand { get; }
    public ICommand OpenDataFolderCommand { get; }

    public async Task OnActivatedAsync()
    {
        if (_autoChecked) return;
        _autoChecked = true;
        await CheckUpdateAsync(silent: true);
    }

    private async Task CheckUpdateAsync(bool silent)
    {
        if (IsChecking || IsDownloading) return;

        IsChecking = true;
        StatusText = L.T("正在连接 GitHub 检查更新…");

        try
        {
            var result = await UpdateService.CheckLatestAsync(CurrentVersion);
            if (!result.Ok)
            {
                _check = null;
                HasUpdate = false;
                _latestVersion = "";
                Raise(nameof(LatestVersionText), nameof(CanInstall));
                StatusText = result.Error ?? L.T("检查更新失败。");
                ReleaseNotes = L.T("可以通过「打开项目主页」或「打开发布页」手动查看。");
                PublishedText = "";
                if (!silent) _ui.Alert(L.T("检查更新"), StatusText);
                return;
            }

            _check = result;
            _latestVersion = result.LatestVersion;
            HasUpdate = result.HasUpdate;
            Raise(nameof(LatestVersionText), nameof(CanInstall));
            PublishedText = string.IsNullOrWhiteSpace(result.PublishedText)
                ? ""
                : L.F("发布时间：{0}", result.PublishedText);

            ReleaseNotes = string.IsNullOrWhiteSpace(result.Notes)
                ? L.T("这个 Release 没有填写更新说明。")
<<<<<<< HEAD
                : L.IsEnglish && result.Notes.Any(IsCjk)
                    ? L.T("更新说明由 GitHub Release 提供，当前只有中文；请点「打开发布页」查看。")
                    : result.Notes.Trim();
=======
                : result.Notes.Trim();
>>>>>>> 6576be8bf42bc977aa5dec75e25c09d16bffd31c

            StatusText = !result.HasUpdate
                ? L.F("已是最新版本（v{0}）。", CurrentVersion)
                : result.Asset == null
                    ? L.F("发现新版本 v{0}，但该 Release 没有可自动安装的 exe/zip，请打开发布页。", result.LatestVersion)
                    : L.F("发现新版本 v{0}，可点击「下载更新版本」自动替换并重启。", result.LatestVersion);

            if (!result.HasUpdate && !silent)
                _ui.Toast(L.T("当前已经是最新版本"), true);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "检查更新");
            StatusText = L.F("检查更新失败：{0}", ex.Message);
            if (!silent) _ui.Alert(L.T("检查更新失败"), ex.Message);
        }
        finally
        {
            IsChecking = false;
        }
    }

    private async Task DownloadAndInstallAsync()
    {
        if (IsDownloading) return;

        if (_check == null || !_check.HasUpdate)
        {
            _ui.Toast(L.T("请先检查更新"), false);
            return;
        }

        if (_check.Asset == null)
        {
            _ui.Alert(L.T("需要手动下载"), L.T("这个 Release 没有可直接替换的 exe/zip，已为你打开发布页。"));
            Launcher.OpenUrl(_check.ReleaseUrl);
            return;
        }

        var check = _check;
        var confirm = _ui.Confirm(
            L.T("下载更新版本"),
            L.F("将从 GitHub 下载 v{0}，替换当前程序并自动重启。\n\n", check.LatestVersion) +
            L.T("下载文件会进行 SHA256 校验（如果 Release 提供摘要）。确认继续？"),
            L.T("下载更新版本"));

        if (confirm != ConfirmResult.Primary) return;

        IsDownloading = true;
        DownloadProgress = 0;
        StatusText = L.T("正在下载更新…");

        try
        {
            var prepared = await UpdateService.DownloadAndPrepareAsync(
                check, new Progress<double>(value => DownloadProgress = value));

            var currentExe = Environment.ProcessPath
                             ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(currentExe))
                throw new InvalidOperationException(L.T("无法确定当前程序路径。"));

            if (!UpdateService.StartUpdater(prepared.ExePath, currentExe, out var error))
                throw new InvalidOperationException(error ?? L.T("无法启动更新程序。"));

            _ui.Toast(L.T("更新程序已启动，应用即将重启"), true);
            Application.Current?.Shutdown();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "安装更新");
            StatusText = L.F("安装更新失败：{0}", ex.Message);
            _ui.Alert(L.T("安装更新失败"), ex.Message);
        }
        finally
        {
            IsDownloading = false;
        }
    }
<<<<<<< HEAD

    private static bool IsCjk(char c) => c >= '\u4e00' && c <= '\u9fff';
=======
>>>>>>> 6576be8bf42bc977aa5dec75e25c09d16bffd31c
}

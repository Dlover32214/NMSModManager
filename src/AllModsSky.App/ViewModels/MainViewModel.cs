using System.Windows.Input;
using System.Windows.Threading;
using AllModsSky.App.Mvvm;
using AllModsSky.App.Services;
using AllModsSky.Core;

namespace AllModsSky.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IShellUi, IDisposable
{
    private readonly Workspace _workspace = new();
    private readonly DispatcherTimer _toastTimer;

    private PageKey _currentPage = PageKey.Library;
    private object _currentPageViewModel;
    private bool _isDark;
    private int _busyCount;
    private bool _isBusy;
    private string _busyText = "";
    private string _toastMessage = "";
    private bool _isToastVisible;
    private bool _isToastError;
    private string _statusLeft = "";
    private string _statusRight = "";
    private bool _initialized;
    private bool _launching;

    public MainViewModel()
    {
        Library = new LibraryViewModel(_workspace, this);
        Discover = new DiscoverViewModel(_workspace, this);
        Tools = new ToolsViewModel(_workspace, this);
        SettingsPage = new SettingsViewModel(_workspace, this);
        AboutPage = new AboutViewModel(this);
        _currentPageViewModel = Library;
        _isDark = _workspace.Settings.IsDark;

        NavigateCommand = new RelayCommand(parameter =>
        {
            var page = parameter switch
            {
                PageKey key => key,
                string text when Enum.TryParse<PageKey>(text, true, out var parsed) => parsed,
                _ => PageKey.Library
            };
            Navigate(page);
        });

        ImportCommand = new RelayCommand(() =>
        {
            var paths = DialogService.PickArchives();
            if (paths is { Count: > 0 }) _ = ImportAsync(paths);
        });

        LaunchModdedCommand = new AsyncRelayCommand(LaunchModdedAsync);
        LaunchVanillaCommand = new AsyncRelayCommand(LaunchVanillaAsync);
        OpenModsFolderCommand = new RelayCommand(() =>
        {
            if (_workspace.Game.IsValid) Launcher.OpenFolder(_workspace.ModsDir);
            else ShowToast("还没有设置游戏目录", false);
        });
        OpenDataFolderCommand = new RelayCommand(() => Launcher.OpenFolder(AppPaths.Root));
        RefreshCommand = new AsyncRelayCommand(() => RefreshAllAsync());

        _toastTimer = new DispatcherTimer();
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        _workspace.LibraryChanged += OnLibraryChanged;
        _workspace.GameChanged += OnGameChanged;
        _workspace.SettingsChanged += OnSettingsChanged;

        UpdateStatus();
    }

    public Workspace Workspace => _workspace;
    public LibraryViewModel Library { get; }
    public DiscoverViewModel Discover { get; }
    public ToolsViewModel Tools { get; }
    public SettingsViewModel SettingsPage { get; }
    public AboutViewModel AboutPage { get; }

    public ICommand NavigateCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand LaunchModdedCommand { get; }
    public ICommand LaunchVanillaCommand { get; }
    public ICommand OpenModsFolderCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand RefreshCommand { get; }

    public event Action? ToastShown;

    public PageKey CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (!SetProperty(ref _currentPage, value)) return;
            Raise(nameof(IsLibraryPage), nameof(IsDiscoverPage), nameof(IsToolsPage), nameof(IsSettingsPage), nameof(IsAboutPage));
        }
    }

    public object CurrentPageViewModel
    {
        get => _currentPageViewModel;
        private set => SetProperty(ref _currentPageViewModel, value);
    }

    public bool IsLibraryPage => CurrentPage == PageKey.Library;
    public bool IsDiscoverPage => CurrentPage == PageKey.Discover;
    public bool IsToolsPage => CurrentPage == PageKey.Tools;
    public bool IsSettingsPage => CurrentPage == PageKey.Settings;
    public bool IsAboutPage => CurrentPage == PageKey.About;

    public bool IsDark
    {
        get => _isDark;
        set
        {
            if (!SetProperty(ref _isDark, value)) return;
            ThemeManager.Apply(value);
            _workspace.Settings.Theme = value ? "Dark" : "Light";
            _workspace.SaveSettings();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string BusyText
    {
        get => _busyText;
        private set => SetProperty(ref _busyText, value);
    }

    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => SetProperty(ref _isToastVisible, value);
    }

    public bool IsToastError
    {
        get => _isToastError;
        private set => SetProperty(ref _isToastError, value);
    }

    public string ToastMessage
    {
        get => _toastMessage;
        private set => SetProperty(ref _toastMessage, value);
    }

    public string ToastIcon => IsToastError ? "✕" : "✓";

    public string StatusLeft
    {
        get => _statusLeft;
        private set => SetProperty(ref _statusLeft, value);
    }

    public string StatusRight
    {
        get => _statusRight;
        private set => SetProperty(ref _statusRight, value);
    }

    public bool HasGame => _workspace.Game.IsValid;
    public string GamePath => _workspace.Game.IsValid ? _workspace.Game.GamePath : "未设置游戏目录";
    public string GameVersionLine => _workspace.Game.IsValid ? _workspace.Game.VersionLine : "去「设置」里指定游戏目录";
    public string ModsPath => _workspace.Game.ModsPath;
    public string AppVersion => "2.0.6";
    public string DataPath => AppPaths.Root;
    public int ModCount => Library.TotalCount;
    public int EnabledCount => Library.EnabledCount;
    public bool HasVanillaBackup => VanillaLaunchStore.HasBackup;
    public string LaunchModeText => HasVanillaBackup
        ? "当前：原版模式（模组已停用）"
        : "当前：Mod 模式";

    public async Task InitializeAsync()
    {
        try
        {
            AppPaths.Ensure();

            if (!GameLocator.IsValid(_workspace.Settings.GamePath))
            {
                var detected = await Task.Run(() => GameLocator.Detect());
                if (!string.IsNullOrEmpty(detected))
                {
                    _workspace.Settings.GamePath = detected;
                    SettingsStore.Save();
                }
            }

            _workspace.RefreshGame();
            _workspace.Library.Load();
            var loadMessage = _workspace.Library.LoadMessage;

            Library.SyncFromLibrary();
            SettingsPage.LoadFromSettings();
            Raise(nameof(GamePath), nameof(GameVersionLine), nameof(HasGame), nameof(ModsPath));
            UpdateStatus();

            if (_workspace.Game.IsValid)
            {
                await Library.RefreshExternalAsync();
                await Library.RefreshHealthAsync();
            }

            _initialized = true;

            if (!string.IsNullOrEmpty(loadMessage)) ShowToast(loadMessage, true);
            if (!_workspace.Game.IsValid)
                ShowToast("没找到游戏目录，请到「设置」里指定", false);

            var pageArg = Environment.GetCommandLineArgs()
                .FirstOrDefault(a => a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase));
            if (pageArg != null && Enum.TryParse<PageKey>(pageArg[7..], true, out var page) && page != PageKey.Library)
                Navigate(page);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "应用初始化");
            Alert("初始化失败", ex.Message);
        }
    }

    public void Navigate(PageKey page)
    {
        if (_currentPage == page && _initialized) return;

        CurrentPage = page;
        CurrentPageViewModel = page switch
        {
            PageKey.Discover => Discover,
            PageKey.Tools => Tools,
            PageKey.Settings => SettingsPage,
            PageKey.About => AboutPage,
            _ => Library
        };

        if (CurrentPageViewModel is IActivatable activatable)
            _ = activatable.OnActivatedAsync();
    }

    public void Toast(string message, bool success = true) => ShowToast(message, success);

    public void DismissToast()
    {
        _toastTimer.Stop();
        IsToastVisible = false;
    }

    private void ShowToast(string message, bool success)
    {
        ToastMessage = message;
        IsToastError = !success;
        IsToastVisible = true;
        Raise(nameof(ToastIcon));
        ToastShown?.Invoke();

        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(success ? 2.8 : 4.6);
        _toastTimer.Start();
    }

    public ConfirmResult Confirm(string title, string message, string primary, string? danger = null) =>
        DialogService.Confirm(title, message, primary, danger);

    public void Alert(string title, string message) => DialogService.Alert(title, message);

    public string? PickFolder(string title, string? initial = null) => DialogService.PickFolder(title, initial);

    public IReadOnlyList<string>? PickArchives() => DialogService.PickArchives();

    public void BeginBusy(string text = "处理中…")
    {
        _busyCount++;
        BusyText = text;
        IsBusy = true;
    }

    public void EndBusy()
    {
        _busyCount = Math.Max(0, _busyCount - 1);
        if (_busyCount == 0)
        {
            IsBusy = false;
            BusyText = "";
        }
    }

    public async Task<DeployReport?> DeployAsync(bool silent = false)
    {
        if (!_workspace.Game.IsValid)
        {
            if (!silent) Alert("需要游戏目录", "先在「设置」里指定无人深空的安装目录。");
            return null;
        }

        BeginBusy("正在同步模组状态…");
        try
        {
            var game = _workspace.Game.GamePath;
            var mods = _workspace.Library.Mods.ToList();
            var createFlag = _workspace.Settings.CreateDisableModsFile;

            var report = await Task.Run(() => _workspace.Deployment.Sync(game, mods, createFlag));

            AppLog.Info($"部署结果：{report.Summary}" +
                        (report.Messages.Count > 0 ? "；" + string.Join("；", report.Messages.Take(6)) : ""));

            _workspace.RefreshGame();
            await Library.RefreshExternalAsync();
            UpdateStatus();
            await ReconcileModStatesAsync(report, game, mods);

            if (!silent)
            {
                var text = report.Summary;
                if (report.Messages.Count > 0)
                    text += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, report.Messages.Take(6));
                Alert(report.Failed > 0 ? "同步完成（有失败项）" : "同步完成", text);
            }
            else if (report.Failed > 0)
            {
                ShowToast($"同步有 {report.Failed} 项失败", false);
            }

            return report;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "同步模组");
            if (!silent) Alert("同步失败", ex.Message);
            return null;
        }
        finally
        {
            EndBusy();
        }
    }

    /// <summary>
    /// 部署后对账：管理器里的启停 vs 游戏 GCMODSETTINGS 的 Enabled。
    /// 只写 MXML 已有条目、游戏没在运行时才写；失败只记录，不影响部署结果。
    /// </summary>
    private async Task ReconcileModStatesAsync(DeployReport report, string game, IReadOnlyList<ModRecord> mods)
    {
        if (!_workspace.Settings.SyncEnabledFlags) return;

        try
        {
            var stateReport = await Task.Run(() => _workspace.ModStates.Reconcile(game, mods));

            foreach (var drift in stateReport.Drifts.Take(5))
                AppLog.Info("启用状态不一致：" + drift.Text);

            if (stateReport.BlockedByGameRunning)
            {
                AppLog.Warn("游戏正在运行，跳过启用状态校正（退出时游戏会覆盖 GCMODSETTINGS）");
                report.Message("游戏正在运行，已跳过启用状态校正");
            }
            else if (!stateReport.Ok)
            {
                AppLog.Warn("启用状态校正失败：" + stateReport.Error);
                report.Message("启用状态校正失败：" + stateReport.Error);
            }
            else if (stateReport.Repaired > 0)
            {
                AppLog.Info($"已按管理器状态校正 {stateReport.Repaired} 个模组的游戏内启用开关");
                report.Message($"校正游戏内启用开关 {stateReport.Repaired} 个");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "启用状态对账");
            report.Message("启用状态对账失败：" + ex.Message);
        }
    }

    public async Task ImportAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;

        if (!_workspace.Game.IsValid)
        {
            Alert("需要游戏目录", "还没有设置游戏目录，无法确定 MODS 文件夹。请先在「设置」里指定。");
            Navigate(PageKey.Settings);
            return;
        }

        var modsDir = _workspace.ModsDir;
        if (!AppPaths.CanWrite(modsDir))
        {
            AppLog.Warn($"MODS 写入检查失败，仍尝试导入：{modsDir}；{AppPaths.LastProbeError}");
            ShowToast("MODS 写入检查失败，仍尝试导入…", false);
        }

        BeginBusy("正在导入…");
        try
        {
            AppLog.Info($"导入请求：{paths.Count} 个路径");

            var existing = new HashSet<string>(_workspace.Library.Mods.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
            var progress = new Progress<string>(text => BusyText = text);

            var outcomes = await Task.Run(() => Importer.ImportMany(paths, modsDir, new ImportOptions
            {
                SourceLabel = "本地导入",
                NameExists = name => existing.Contains(name),
                Progress = progress
            }).ToList());

            var imported = 0;
            var failures = new List<string>();

            foreach (var outcome in outcomes)
            {
                if (outcome.Ok && outcome.Record != null)
                {
                    _workspace.Library.Add(outcome.Record);
                    imported++;
                    AppLog.Info($"导入成功：{outcome.DisplayName}（{outcome.Record.FolderName}）");
                }
                else
                {
                    failures.Add($"{outcome.DisplayName}：{outcome.Message}");
                    AppLog.Warn($"导入失败：{outcome.DisplayName}：{outcome.Message}");
                }
            }

            if (imported > 0)
            {
                _workspace.Library.Save();
                _workspace.NotifyLibraryChanged();
                await DeployAsync(silent: true);
                await Library.RefreshHealthAsync();
                await Library.RefreshExternalAsync();
                UpdateStatus();
            }

            ShowToast(imported > 0 ? $"已导入 {imported} 个模组" : "没有导入任何模组", imported > 0);

            if (failures.Count > 0)
                Alert("部分导入失败", string.Join(Environment.NewLine, failures.Take(8)));
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "导入模组");
            Alert("导入失败", ex.Message);
        }
        finally
        {
            EndBusy();
        }
    }

    public async Task RefreshAllAsync()
    {
        BeginBusy("正在刷新…");
        try
        {
            _workspace.RefreshGame();
            Library.SyncFromLibrary();
            SettingsPage.RefreshDerived();
            await Library.RefreshExternalAsync();
            await Library.RefreshHealthAsync();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "刷新");
        }
        finally
        {
            EndBusy();
        }
    }

    private async Task LaunchModdedAsync()
    {
        if (_launching || !EnsureGame()) return;

        _launching = true;
        try
        {
            var backup = VanillaLaunchStore.Load();
            if (backup == null && VanillaLaunchStore.HasBackup)
            {
                // 状态文件损坏时不要让界面永远停在“原版模式”。
                VanillaLaunchStore.Clear();
                RaiseLaunchMode();
            }

            if (backup != null)
            {
                var enabledIds = new HashSet<string>(backup.EnabledIds, StringComparer.OrdinalIgnoreCase);
                foreach (var mod in _workspace.Library.Mods)
                    mod.Enabled = enabledIds.Contains(mod.Id);

                PersistEnabledState();
            }

            if (!await PrepareLaunchAsync()) return;

            var anyEnabled = _workspace.Library.Mods.Any(m => m.Enabled) || AnyExternalEnabled();
            ApplyLaunchDisableFlag(anyEnabled);

            if (backup != null)
            {
                VanillaLaunchStore.Clear();
                RaiseLaunchMode();
            }

            ShowToast(backup != null ? "已恢复 Mod 状态，正在启动…" : "正在启动 Mod 版…", true);
            Launcher.LaunchGame(_workspace.Game);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "启动 Mod 版");
            ShowToast("启动失败：" + ex.Message, false);
        }
        finally
        {
            _launching = false;
        }
    }

    private async Task LaunchVanillaAsync()
    {
        if (_launching || !EnsureGame()) return;

        _launching = true;
        try
        {
            var before = _workspace.Library.Mods
                .ToDictionary(m => m.Id, m => m.Enabled, StringComparer.OrdinalIgnoreCase);
            var enabledIds = before
                .Where(pair => pair.Value)
                .Select(pair => pair.Key)
                .ToList();
            var hadBackup = VanillaLaunchStore.HasBackup;

            if (!hadBackup && !VanillaLaunchStore.Save(enabledIds))
            {
                ShowToast("无法保存原版启动状态，未启动", false);
                return;
            }

            foreach (var mod in _workspace.Library.Mods)
                mod.Enabled = false;

            PersistEnabledState();

            if (!await PrepareLaunchAsync())
            {
                RestoreEnabledStates(before);
                await DeployAsync(silent: true);

                if (!hadBackup)
                {
                    VanillaLaunchStore.Clear();
                    RaiseLaunchMode();
                }

                return;
            }

            EnsureVanillaDisableFlag();
            if (!hadBackup)
                RaiseLaunchMode();

            var message = enabledIds.Count > 0
                ? $"已停用 {enabledIds.Count} 个模组，正在启动原版…"
                : "正在启动原版…";
            ShowToast(message, true);
            Launcher.LaunchGame(_workspace.Game);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "启动原版");
            ShowToast("启动失败：" + ex.Message, false);
        }
        finally
        {
            _launching = false;
        }
    }

    private bool EnsureGame()
    {
        if (_workspace.Game.IsValid) return true;

        ShowToast("先设置游戏目录", false);
        Navigate(PageKey.Settings);
        return false;
    }

    private async Task<bool> PrepareLaunchAsync()
    {
        var report = await DeployAsync(silent: true);
        if (report == null)
        {
            ShowToast("同步失败，未启动游戏", false);
            return false;
        }

        if (report.Failed > 0)
        {
            ShowToast($"同步有 {report.Failed} 项失败，未启动游戏", false);
            return false;
        }

        return true;
    }

    private void EnsureVanillaDisableFlag() => ApplyLaunchDisableFlag(anyEnabled: false);

    private void ApplyLaunchDisableFlag(bool anyEnabled)
    {
        if (!_workspace.Game.IsValid) return;

        DeploymentService.ApplyDisableModsFlag(_workspace.Game.GamePath, true, anyEnabled);
        _workspace.RefreshGame();
    }

    private bool AnyExternalEnabled()
    {
        if (!_workspace.Game.IsValid) return false;

        var external = _workspace.Deployment.ListExternal(
            _workspace.Game.GamePath, _workspace.Library.Mods);
        return external.Any(e => e.Enabled);
    }

    private void PersistEnabledState()
    {
        _workspace.Library.Save();
        Library.SyncFromLibrary();
        _workspace.NotifyLibraryChanged();
    }

    private void RestoreEnabledStates(IReadOnlyDictionary<string, bool> states)
    {
        foreach (var mod in _workspace.Library.Mods)
        {
            if (states.TryGetValue(mod.Id, out var enabled))
                mod.Enabled = enabled;
        }

        PersistEnabledState();
    }

    private void RaiseLaunchMode()
    {
        Raise(nameof(HasVanillaBackup), nameof(LaunchModeText));
    }

    private void OnLibraryChanged()
    {
        UpdateStatus();
        Raise(nameof(ModCount), nameof(EnabledCount));
    }

    private void OnGameChanged()
    {
        Raise(nameof(GamePath), nameof(GameVersionLine), nameof(HasGame), nameof(ModsPath));
        UpdateStatus();
    }

    private void OnSettingsChanged()
    {
        Raise(nameof(GamePath), nameof(GameVersionLine), nameof(HasGame), nameof(ModsPath));
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusLeft = _workspace.Game.IsValid
            ? "MODS  ·  " + _workspace.Game.ModsPath
            : "未设置游戏目录";

        StatusRight = $"{Library.TotalCount} 个模组  ·  已启用 {Library.EnabledCount}" +
                      (_workspace.Game.IsValid && !_workspace.Game.ModsWritable ? "  ·  MODS 不可写" : "");
    }

    public void Dispose()
    {
        _toastTimer.Stop();
        _workspace.Dispose();
    }
}

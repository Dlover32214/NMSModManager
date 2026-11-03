using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using AllModsSky.App.Services;
using AllModsSky.App.ViewModels;

namespace AllModsSky.App;

public partial class App : Application
{
    private MainViewModel? _shell;
    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 双开会让两个实例同时改写 library.json 和 MODS 目录，先挡住。
        // 注意：string.GetHashCode() 在 .NET Core 每进程随机化，必须用稳定哈希。
        var dir = AppContext.BaseDirectory.ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dir)))[..16];
        _instanceMutex = new Mutex(true, @"Local\AllModsSky_" + hash, out var isFirstInstance);

        if (!isFirstInstance)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;

            try
            {
                AppPaths.Ensure();
                L.Apply(SettingsStore.Current.Language);
            }
            catch { }

            MessageBox.Show(L.T("All Mod's Sky 已经在运行了。"), "All Mod's Sky",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        // 日志：现场写 logs/latest.log，每次启动把上一次的压成 logs/yyyy-MM-dd-HHmmss.log.gz，只留最近 5 份
        AppLog.StartSession();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) WriteCrash(ex, "AppDomain");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrash(args.Exception, "TaskScheduler");
            args.SetObserved();
        };

        try
        {
            AppPaths.Ensure();
            L.Apply(SettingsStore.Current.Language);
            ThemeManager.Apply(SettingsStore.Current.IsDark);

            _shell = new MainViewModel();
            var window = new MainWindow { DataContext = _shell };
            MainWindow = window;
            window.Show();

            _ = _shell.InitializeAsync(GetStartPage());
        }
        catch (Exception ex)
        {
            WriteCrash(ex, "启动");
            MessageBox.Show(L.F("启动失败：{0}\n\n详细信息：{1}", ex.Message, AppPaths.CrashFile),
                "All Mod's Sky", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>
    /// 语言保存后立即换一套窗口和 ViewModel，保留当前页面与窗口位置。
    /// 进程不退出，所以比“改完语言请重启”少一次完整启动。
    /// </summary>
    public void RebuildShell(PageKey page, string? notice = null)
    {
        AppPaths.Ensure();
        L.Apply(SettingsStore.Current.Language);
        ThemeManager.Apply(SettingsStore.Current.IsDark);

        var oldShell = _shell;
        var oldWindow = MainWindow;

        var shell = new MainViewModel();
        var window = new MainWindow { DataContext = shell };

        if (oldWindow is { IsLoaded: true })
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = oldWindow.Left;
            window.Top = oldWindow.Top;
            window.Width = oldWindow.Width;
            window.Height = oldWindow.Height;
            window.WindowState = oldWindow.WindowState;
        }

        MainWindow = window;
        _shell = shell;
        window.Show();

        // 先显示新窗口，再关闭旧窗口，避免 ShutdownMode=OnLastWindowClose 把进程带走。
        oldWindow?.Close();
        oldShell?.Dispose();

        _ = shell.InitializeAsync(page, notice);
    }

    private static PageKey GetStartPage()
    {
        var pageArg = Environment.GetCommandLineArgs()
            .FirstOrDefault(a => a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase));
        return pageArg != null && Enum.TryParse<PageKey>(pageArg[7..], true, out var page)
            ? page
            : PageKey.Library;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shell?.Dispose();

        try { _instanceMutex?.ReleaseMutex(); } catch { }
        _instanceMutex?.Dispose();
        _instanceMutex = null;

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrash(e.Exception, "UI");
        e.Handled = true;

        try
        {
            MessageBox.Show(L.F("程序遇到未处理的错误：\n\n{0}\n\n详细信息已写入：\n{1}",
                    e.Exception.Message, AppPaths.CrashFile),
                "All Mod's Sky", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }

        Shutdown(1);
    }

    private static void WriteCrash(Exception ex, string source)
    {
        try
        {
            AppPaths.Ensure();
            File.AppendAllText(AppPaths.CrashFile,
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} [{source}]{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }

        AppLog.Error(ex, source);
    }
}

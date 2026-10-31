using System.Windows;
using System.Windows.Threading;
using AllModsSky.App.Services;
using AllModsSky.App.ViewModels;
using AllModsSky.Core;

namespace AllModsSky.App;

public partial class App : Application
{
    private MainViewModel? _shell;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
            ThemeManager.Apply(SettingsStore.Current.IsDark);

            _shell = new MainViewModel();
            var window = new MainWindow { DataContext = _shell };
            MainWindow = window;
            window.Show();

            _ = _shell.InitializeAsync();
        }
        catch (Exception ex)
        {
            WriteCrash(ex, "启动");
            MessageBox.Show("启动失败：" + ex.Message + "\n\n详细信息：" + AppPaths.CrashFile,
                "All Mod's Sky", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shell?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrash(e.Exception, "UI");
        e.Handled = true;

        try
        {
            MessageBox.Show("程序遇到未处理的错误：\n\n" + e.Exception.Message +
                            "\n\n详细信息已写入：\n" + AppPaths.CrashFile,
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

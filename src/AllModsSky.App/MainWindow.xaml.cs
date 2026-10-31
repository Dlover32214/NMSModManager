using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AllModsSky.App.Services;
using AllModsSky.App.ViewModels;
using AllModsSky.Core;

namespace AllModsSky.App;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;
    private DispatcherTimer? _logoOcclusionTimer;
    private DateTime _logoStartedUtc;

    public MainWindow()
    {
        InitializeComponent();

        // 小屏 / 高缩放时避免窗口超出工作区
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Max(MinWidth, area.Width - 24));
        Height = Math.Min(Height, Math.Max(MinHeight, area.Height - 24));

        DataContextChanged += OnDataContextChanged;
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
        Loaded += (_, _) => StartLogoOcclusion();
        Closed += (_, _) =>
        {
            _logoOcclusionTimer?.Stop();
            if (_viewModel != null) _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        };
    }

    /// <summary>
    /// 卫星在远侧（上半圈）时，用与徽章同色的遮罩盖住它；
    /// 直接用动画的实际角度驱动，避免关键帧和主旋转不同步。
    /// </summary>
    private void StartLogoOcclusion()
    {
        _logoOcclusionTimer ??= new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };

        _logoOcclusionTimer.Tick -= LogoOcclusionTick;
        _logoOcclusionTimer.Tick += LogoOcclusionTick;
        _logoStartedUtc = DateTime.UtcNow;
        _logoOcclusionTimer.Start();
    }

    private void LogoOcclusionTick(object? sender, EventArgs e)
    {
        // 主旋转是 20 秒一圈；用启动时刻推算，避免读取动画属性时拿到基础值。
        var elapsed = (DateTime.UtcNow - _logoStartedUtc).TotalSeconds;
        var angle = (elapsed / 20.0 * 360.0) % 360.0;
        var farSide = angle <= 90 || angle >= 270;
        BackCover.Opacity = farSide ? 1 : 0;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.ToastShown -= OnToastShown;
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        _viewModel = DataContext as MainViewModel;

        if (_viewModel != null)
        {
            _viewModel.ToastShown += OnToastShown;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsDark)) ApplyTitleBarTheme();
        else if (e.PropertyName == nameof(MainViewModel.IsToastVisible) && _viewModel?.IsToastVisible == false)
            HideToastAnimated();
    }

    private void OnToastShown()
    {
        ToastHost.BeginAnimation(OpacityProperty, null);
        ToastScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        ToastScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        ToastTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);

        ToastHost.Visibility = Visibility.Visible;
        ToastHost.Opacity = 0;
        ToastScale.ScaleX = 0.9;
        ToastScale.ScaleY = 0.9;
        ToastTranslate.Y = 18;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ToastHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        ToastScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
        ToastScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
        ToastTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
    }

    private void HideToastAnimated()
    {
        if (ToastHost.Visibility != Visibility.Visible) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease };
        fade.Completed += (_, _) => ToastHost.Visibility = Visibility.Collapsed;

        ToastHost.BeginAnimation(OpacityProperty, fade);
        ToastScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.92, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        ToastScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.92, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        ToastTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(10, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        // 文件拖入必须在 Window 的 Preview 阶段截获：
        // 列表控件会把非模组拖拽的 Effects 设为 None，导致资源管理器直接禁止放下。
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        e.Handled = true;
        await ImportDroppedAsync(e);
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        await ImportDroppedAsync(e);
    }

    private async Task ImportDroppedAsync(DragEventArgs e)
    {
        if (_viewModel == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;

        var usable = paths
            .Where(p => Directory.Exists(p) || Importer.IsSupported(p))
            .ToArray();

        if (usable.Length == 0)
        {
            _viewModel.Toast("拖入 zip / 文件夹 / pak 才能导入", false);
            return;
        }

        await _viewModel.ImportAsync(usable);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel == null) return;

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.O:
                    _viewModel.ImportCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.D1:
                    _viewModel.Navigate(PageKey.Library);
                    e.Handled = true;
                    return;
                case Key.D2:
                    _viewModel.Navigate(PageKey.Discover);
                    e.Handled = true;
                    return;
                case Key.D3:
                    _viewModel.Navigate(PageKey.Tools);
                    e.Handled = true;
                    return;
                case Key.D4:
                    _viewModel.Navigate(PageKey.Settings);
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == Key.F5)
        {
            _viewModel.RefreshCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_viewModel.Library.BatchMode)
            {
                _viewModel.Library.ExitBatch();
                e.Handled = true;
            }
            else
            {
                _viewModel.DismissToast();
            }
        }
    }

    private void ApplyTitleBarTheme()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            var dark = ThemeManager.IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
        }
        catch
        {
            // 老系统不支持深色标题栏，忽略
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

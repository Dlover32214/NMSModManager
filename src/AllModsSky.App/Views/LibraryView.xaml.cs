using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AllModsSky.App.ViewModels;

namespace AllModsSky.App.Views;

public partial class LibraryView : UserControl
{
    /// <summary>按住多久算「长按」——到点就把行浮起来，提示可以拖了。</summary>
    private static readonly TimeSpan PressDelay = TimeSpan.FromMilliseconds(170);

    private readonly DispatcherTimer _pressTimer;

    private Point _dragStart;
    private ModItemViewModel? _dragItem;
    private bool _armed;
    private bool _dragging;
    private bool _lifted;
    private DragGhostAdorner? _ghost;

    public LibraryView()
    {
        InitializeComponent();

        _pressTimer = new DispatcherTimer { Interval = PressDelay };
        _pressTimer.Tick += (_, _) =>
        {
            _pressTimer.Stop();
            if (!_armed || _dragging || _dragItem == null) return;
            if (Mouse.LeftButton != MouseButtonState.Pressed) return;

            LiftItem(_dragItem);
            _lifted = true;
        };
    }

    private LibraryViewModel? ViewModel => DataContext as LibraryViewModel;

    /// <summary>点菜单外的任意位置关闭配置菜单（弹层本身是独立窗口，不受这层遮罩影响）。</summary>
    private void ProfileMenuScrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel == null) return;
        ViewModel.IsProfileMenuOpen = false;
        e.Handled = true;
    }

    /// <summary>切页/关闭视图时收起菜单，避免弹层留在屏幕上。</summary>
    private void LibraryView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.IsProfileMenuOpen = false;
        _pressTimer.Stop();
        EndGhost();
    }

    // ---- 拖拽排序 ----

    private void ModList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(ModList);
        _dragItem = ItemAt(_dragStart);
        _armed = _dragItem != null && !IsInteractiveSource(e.OriginalSource);
        _lifted = false;

        if (!_armed) return;

        _pressTimer.Stop();
        _pressTimer.Start();
    }

    private void ModList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _pressTimer.Stop();
        _armed = false;

        if (!_dragging && _lifted && _dragItem != null) ResetLift(_dragItem);
        _lifted = false;
    }

    /// <summary>批量模式下由行本身处理点击，避免 ListBox 的 Extended 选择规则要求 Ctrl。</summary>
    private void ModRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { BatchMode: true }) return;
        if (sender is not ListBoxItem { DataContext: ModItemViewModel item }) return;
        if (IsInteractiveSource(e.OriginalSource)) return;

        item.IsSelected = !item.IsSelected;
        _armed = false;   // 批量点选之后不要接着触发拖拽
        _dragItem = null;
        e.Handled = true;
    }

    private void ModList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_armed || _dragging || _dragItem == null) return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _pressTimer.Stop();
            _armed = false;
            if (_lifted) ResetLift(_dragItem);
            _lifted = false;
            return;
        }

        var position = e.GetPosition(ModList);
        if (Math.Abs(position.Y - _dragStart.Y) < 5 && Math.Abs(position.X - _dragStart.X) < 5) return;

        _pressTimer.Stop();
        _dragging = true;
        _armed = false;

        BeginGhost(_dragItem, position);
        try
        {
            DragDrop.DoDragDrop(ModList, _dragItem, DragDropEffects.Move);
        }
        catch
        {
            // 拖拽过程中目标窗口关闭等异常，忽略
        }
        finally
        {
            EndGhost();
            ResetLift(_dragItem);
            _lifted = false;
            _dragging = false;
            _dragItem = null;
        }
    }

    private void ModList_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        if (_ghost == null) return;

        _ghost.MoveTo(Mouse.GetPosition(ModList));
        e.UseDefaultCursors = false;
        Mouse.SetCursor(Cursors.SizeAll);
    }

    private void ModList_DragOver(object sender, DragEventArgs e)
    {
        var ok = e.Data.GetDataPresent(typeof(ModItemViewModel));
        e.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;

        if (ok && _ghost != null)
        {
            var position = e.GetPosition(ModList);
            _ghost.MoveTo(position);
            _ghost.MarkerY = MarkerY(position);
        }

        e.Handled = true;
    }

    private void ModList_DragLeave(object sender, DragEventArgs e)
    {
        if (_ghost != null) _ghost.MarkerY = double.NaN;
    }

    private void ModList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ModItemViewModel)) is not ModItemViewModel source) return;

        var target = ItemAt(e.GetPosition(ModList));
        if (ReferenceEquals(target, source)) return;

        var after = false;
        if (target != null &&
            ModList.ItemContainerGenerator.ContainerFromItem(target) is ListBoxItem container)
        {
            var position = e.GetPosition(container);
            after = position.Y > container.ActualHeight / 2;
        }

        EndGhost();
        ResetLift(source);
        var before = CapturePositions();

        ViewModel?.MoveItem(source, target, after);
        e.Handled = true;

        ModList.UpdateLayout();

        // 等这一帧的布局跑完再量位置，容器刚重建时拿到的坐标可能还是旧的
        Dispatcher.BeginInvoke(new Action(() => AnimatePositions(before)), DispatcherPriority.Loaded);
    }

    private void ModList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var item = ItemAt(e.GetPosition(ModList));
        if (item == null || ViewModel == null) return;

        if (ViewModel.OpenFolderCommand.CanExecute(item))
            ViewModel.OpenFolderCommand.Execute(item);
    }

    private ModItemViewModel? ItemAt(Point point)
    {
        if (ModList.InputHitTest(point) is not DependencyObject hit) return null;

        while (hit != null && hit is not ListBoxItem)
            hit = VisualTreeHelper.GetParent(hit);

        return (hit as ListBoxItem)?.DataContext as ModItemViewModel;
    }

    private ListBoxItem? ContainerOf(ModItemViewModel? item) =>
        item == null ? null : ModList.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;

    private static bool IsInteractiveSource(object? source)
    {
        if (source is not DependencyObject current) return false;

        while (current != null && current is not ListBoxItem)
        {
            if (current is ButtonBase or TextBoxBase or ScrollBar) return true;
            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    /// <summary>插入位置提示线：落在目标行的上半 → 画在它上面，下半 → 画在它下面。</summary>
    private double MarkerY(Point point)
    {
        var container = ContainerOf(ItemAt(point));
        if (container == null) return double.NaN;

        var top = container.TranslatePoint(new Point(0, 0), ModList).Y;
        var middle = top + container.ActualHeight / 2;
        return point.Y > middle ? top + container.ActualHeight + 2 : top - 2;
    }

    // ---- 长按 / 拖拽预览 ----

    private void BeginGhost(ModItemViewModel item, Point position)
    {
        var container = ContainerOf(item);
        if (container == null) return;

        var layer = AdornerLayer.GetAdornerLayer(ModList);
        if (layer == null) return;

        var size = new Size(container.ActualWidth, container.ActualHeight);
        if (size.Width < 1 || size.Height < 1) return;

        // 先拍一张静态快照，再去压暗原件——直接拿 VisualBrush 会连压暗一起画进预览里
        var snapshot = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        snapshot.Render(container);

        _ghost = new DragGhostAdorner(ModList, snapshot, size, position, AdornerLayerBrush());
        layer.Add(_ghost);

        LiftItem(item);
        _lifted = true;
    }

    private void EndGhost()
    {
        if (_ghost == null) return;

        AdornerLayer.GetAdornerLayer(ModList)?.Remove(_ghost);
        _ghost = null;
    }

    private static Brush AdornerLayerBrush() =>
        Application.Current?.TryFindResource("Accent") as Brush ?? Brushes.DodgerBlue;

    private void LiftItem(ModItemViewModel? item)
    {
        var container = ContainerOf(item);
        if (container == null) return;

        container.Opacity = 0.32;
        container.RenderTransformOrigin = new Point(0.5, 0.5);
        container.RenderTransform = new ScaleTransform(0.985, 0.985);
    }

    private void ResetLift(ModItemViewModel? item)
    {
        var container = ContainerOf(item);
        if (container == null) return;

        container.Opacity = 1;
        container.Effect = null;
        container.RenderTransform = Transform.Identity;
    }

    private Dictionary<ModItemViewModel, double> CapturePositions()
    {
        var map = new Dictionary<ModItemViewModel, double>();

        foreach (var obj in ModList.Items)
        {
            if (obj is not ModItemViewModel item) continue;
            var container = ContainerOf(item);
            if (container == null) continue;
            map[item] = container.TranslatePoint(new Point(0, 0), ModList).Y;
        }

        return map;
    }

    /// <summary>让重排后的所有行从旧位置滑到新位置，避免瞬间“挤压”。</summary>
    private void AnimatePositions(Dictionary<ModItemViewModel, double> before)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        foreach (var pair in before)
        {
            var container = ContainerOf(pair.Key);
            if (container == null) continue;

            var newY = container.TranslatePoint(new Point(0, 0), ModList).Y;
            var delta = pair.Value - newY;
            if (Math.Abs(delta) < 0.5) continue;

            var transform = new TranslateTransform(0, delta);
            container.RenderTransform = transform;

            var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = ease
            };
            animation.Completed += (_, _) =>
            {
                if (ReferenceEquals(container.RenderTransform, transform))
                    container.RenderTransform = Transform.Identity;
            };

            transform.BeginAnimation(TranslateTransform.YProperty, animation);
        }
    }
}

/// <summary>
/// 拖拽时跟着光标走的行预览：一张压暗原行后拍下来的快照，外加一条插入位置提示线。
/// </summary>
internal sealed class DragGhostAdorner : Adorner
{
    private readonly ImageSource _snapshot;
    private readonly Size _size;
    private readonly Point _grab;
    private readonly Pen _markerPen;
    private Point _position;
    private double _markerY = double.NaN;

    public DragGhostAdorner(UIElement adorned, ImageSource snapshot, Size size, Point grab, Brush accent)
        : base(adorned)
    {
        _snapshot = snapshot;
        _size = size;
        _grab = grab;
        _position = grab;
        _markerPen = new Pen(accent, 2);
        IsHitTestVisible = false;
    }

    /// <summary>插入提示线的 Y 坐标；NaN 表示不画。</summary>
    public double MarkerY
    {
        get => _markerY;
        set
        {
            if (Math.Abs(_markerY - value) < 0.5 && !(double.IsNaN(_markerY) && double.IsNaN(value))) return;
            _markerY = value;
            InvalidateVisual();
        }
    }

    public void MoveTo(Point position)
    {
        if (Math.Abs(_position.X - position.X) < 0.5 && Math.Abs(_position.Y - position.Y) < 0.5) return;
        _position = position;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = AdornedElement.RenderSize.Width;

        if (!double.IsNaN(_markerY) && _markerY > 0)
        {
            dc.DrawLine(_markerPen, new Point(10, _markerY), new Point(width - 10, _markerY));
            dc.DrawEllipse(_markerPen.Brush, null, new Point(10, _markerY), 2.5, 2.5);
            dc.DrawEllipse(_markerPen.Brush, null, new Point(width - 10, _markerY), 2.5, 2.5);
        }

        if (_snapshot == null) return;

        var rect = new Rect(_position.X - _grab.X, _position.Y - _grab.Y, _size.Width, _size.Height);
        var shadow = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));

        dc.PushOpacity(0.96);
        dc.DrawRoundedRectangle(shadow, null, new Rect(rect.X + 4, rect.Y + 6, rect.Width, rect.Height), 12, 12);
        dc.DrawRoundedRectangle(new ImageBrush(_snapshot) { Stretch = Stretch.Fill }, null, rect, 12, 12);
        dc.Pop();
    }
}

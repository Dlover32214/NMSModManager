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

    /// <summary>
    /// 拖拽开始时量一次的行位置（上/下边界）与对应数据，两个列表一一对应。
    /// 拖拽期间列表不会变，不必每帧重算；提示线和落点都从这里取。
    /// </summary>
    private readonly List<DragGeometry.RowSpan> _rowSpans = new();
    private readonly List<ModItemViewModel> _rowItems = new();

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
        // DragLeave 会从行、ScrollViewer 这些子元素一路冒泡上来：光标只是从某一行挪到
        // 行间那 4px 空隙时也会触发，直接隐藏提示线就会「闪」。只有真的出界才收起来。
        if (_ghost == null) return;

        var position = e.GetPosition(ModList);
        var inside = position.X >= 0 && position.Y >= 0
                     && position.X <= ModList.ActualWidth && position.Y <= ModList.ActualHeight;

        if (!inside) _ghost.MarkerY = double.NaN;
    }

    private void ModList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ModItemViewModel)) is not ModItemViewModel source) return;

        // 落点跟提示线用同一份计算：以前走命中测试，掉在行间空隙里会拿到 null，
        // 结果「线指着中间，东西飞到最后面去了」。
        var slot = DragGeometry.Slot(_rowSpans, e.GetPosition(ModList).Y);
        var target = slot.Index < _rowItems.Count ? _rowItems[slot.Index] : null;
        var after = slot.After;

        EndGhost();
        ResetLift(source);

        if (ReferenceEquals(target, source) && !after) return;

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

    /// <summary>
    /// 插入位置提示线：落在行的上半 → 画在它上面，下半 → 画在它下面。
    /// 用几何位置算，不依赖命中测试——行间距 4px、列表顶部留白这些「没压在任何行上」
    /// 的地方原来会算不出位置，线就闪一下或者干脆没了。
    /// </summary>
    private double MarkerY(Point point) =>
        DragGeometry.MarkerY(_rowSpans, DragGeometry.Slot(_rowSpans, point.Y));

    /// <summary>拖拽开始时把每一行的上下边界量一遍，DragOver 里只做比较。</summary>
    private void CaptureRows()
    {
        _rowSpans.Clear();
        _rowItems.Clear();

        foreach (var obj in ModList.Items)
        {
            if (obj is not ModItemViewModel item) continue;
            var container = ContainerOf(item);
            if (container == null) continue;

            var top = container.TranslatePoint(new Point(0, 0), ModList).Y;
            _rowSpans.Add(new DragGeometry.RowSpan(top, top + container.ActualHeight));
            _rowItems.Add(item);
        }
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

        CaptureRows();

        LiftItem(item);
        _lifted = true;
    }

    private void EndGhost()
    {
        _rowSpans.Clear();
        _rowItems.Clear();

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
        var size = AdornedElement.RenderSize;
        var width = size.Width;

        if (_snapshot != null)
        {
            var rect = new Rect(_position.X - _grab.X, _position.Y - _grab.Y, _size.Width, _size.Height);
            var shadow = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));

            dc.PushOpacity(0.96);
            dc.DrawRoundedRectangle(shadow, null, new Rect(rect.X + 4, rect.Y + 6, rect.Width, rect.Height), 12, 12);
            dc.DrawRoundedRectangle(new ImageBrush(_snapshot) { Stretch = Stretch.Fill }, null, rect, 12, 12);
            dc.Pop();
        }

        if (double.IsNaN(_markerY)) return;

        // 夹进可见范围：第一行上方算出来是负值，画到外面就被裁掉，看着像「线没了」。
        // 提示线最后画，否则会被跟手的预览图盖住，光标扫过时忽隐忽现。
        var y = DragGeometry.ClampToView(_markerY, size.Height);

        dc.DrawLine(_markerPen, new Point(10, y), new Point(width - 10, y));
        dc.DrawEllipse(_markerPen.Brush, null, new Point(10, y), 2.5, 2.5);
        dc.DrawEllipse(_markerPen.Brush, null, new Point(width - 10, y), 2.5, 2.5);
    }
}

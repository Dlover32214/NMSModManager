using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace AllModsSky.App.Behaviors;

/// <summary>列表行需要能报告、切换自己的选中状态。</summary>
public interface ISelectableItem
{
    bool IsSelected { get; set; }
}

/// <summary>
/// 批量刷选时的聚合通知：一次刷一整段只刷新一次计数。
/// 由 ListBox 的 DataContext（ViewModel）实现；不实现也能用，只是每改一行都刷新一次。
/// </summary>
public interface IBulkSelectionSink
{
    void BeginBulkSelection();
    void EndBulkSelection(bool changed);
}

/// <summary>
/// 按住左键在列表里拖动，把经过的行刷成同一个选中状态。
///
/// 用法（XAML）：
///   &lt;ListBox behaviors:ListBoxBehaviors.EnableDragToSelect="True"
///            behaviors:ListBoxBehaviors.CanDragToSelect="{Binding BatchMode}" /&gt;
///
/// 状态重置：鼠标抬起、丢失捕获、窗口失去激活、列表卸载——四条路都会清掉刷选状态，
/// 所以 Alt+Tab 出去在外部松手再切回来，不会出现「一动鼠标就乱选」。
/// </summary>
public static class ListBoxBehaviors
{
    public static readonly DependencyProperty EnableDragToSelectProperty = DependencyProperty.RegisterAttached(
        "EnableDragToSelect", typeof(bool), typeof(ListBoxBehaviors),
        new PropertyMetadata(false, OnEnableDragToSelectChanged));

    public static bool GetEnableDragToSelect(DependencyObject element) =>
        (bool)element.GetValue(EnableDragToSelectProperty);

    public static void SetEnableDragToSelect(DependencyObject element, bool value) =>
        element.SetValue(EnableDragToSelectProperty, value);

    /// <summary>当前是否允许刷选。绑到 ViewModel 的批量模式；默认一直允许。</summary>
    public static readonly DependencyProperty CanDragToSelectProperty = DependencyProperty.RegisterAttached(
        "CanDragToSelect", typeof(bool), typeof(ListBoxBehaviors), new PropertyMetadata(true));

    public static bool GetCanDragToSelect(DependencyObject element) =>
        (bool)element.GetValue(CanDragToSelectProperty);

    public static void SetCanDragToSelect(DependencyObject element, bool value) =>
        element.SetValue(CanDragToSelectProperty, value);

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(PaintState), typeof(ListBoxBehaviors), new PropertyMetadata(null));

    private static PaintState? StateOf(ListBox list) => (PaintState?)list.GetValue(StateProperty);

    private static void OnEnableDragToSelectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list) return;

        if (e.NewValue is true)
        {
            if (StateOf(list) != null) return;

            var state = new PaintState();
            list.SetValue(StateProperty, state);

            list.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            list.PreviewMouseMove += OnPreviewMouseMove;
            list.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
            // 行容器（ListBoxItem）捕获/释放鼠标时，LostMouseCapture 会先被它标记为已处理
            list.AddHandler(UIElement.LostMouseCaptureEvent, new MouseEventHandler(OnLostMouseCapture), true);
            list.Loaded += OnLoaded;
            list.Unloaded += OnUnloaded;

            if (list.IsLoaded) HookWindow(list, state);
        }
        else
        {
            Detach(list);
        }
    }

    private static void Detach(ListBox list)
    {
        if (StateOf(list) is not { } state) return;

        state.Reset();
        UnhookWindow(state);

        list.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        list.PreviewMouseMove -= OnPreviewMouseMove;
        list.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
        list.RemoveHandler(UIElement.LostMouseCaptureEvent, new MouseEventHandler(OnLostMouseCapture));
        list.Loaded -= OnLoaded;
        list.Unloaded -= OnUnloaded;

        list.ClearValue(StateProperty);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListBox list && StateOf(list) is { } state) HookWindow(list, state);
    }

    /// <summary>视图切走时先清干净：回来时不会带着上一轮的锚点继续刷。</summary>
    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ListBox list || StateOf(list) is not { } state) return;

        state.Reset();
        UnhookWindow(state);
    }

    private static void HookWindow(ListBox list, PaintState state)
    {
        var window = Window.GetWindow(list);
        if (window == null || ReferenceEquals(window, state.Window)) return;

        UnhookWindow(state);
        state.Window = window;
        state.Deactivated = (_, _) => Reset(list, state);
        window.Deactivated += state.Deactivated;
    }

    private static void UnhookWindow(PaintState state)
    {
        if (state.Window == null) return;

        if (state.Deactivated != null) state.Window.Deactivated -= state.Deactivated;
        state.Deactivated = null;
        state.Window = null;
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list || StateOf(list) is not { } state) return;
        if (!GetCanDragToSelect(list)) return;

        var container = ContainerAt(list, e.GetPosition(list));
        if (container?.DataContext is not ISelectableItem item) return;
        if (IsInteractiveSource(e.OriginalSource)) return;

        var index = list.Items.IndexOf(container.DataContext);
        if (index < 0) return;

        // 按下这一行的结果就是「刷」的目标状态：往下滑时经过的行都跟它对齐，
        // 而不是每行各翻一次，否则连成一片就成了斑马纹。
        item.IsSelected = !item.IsSelected;

        state.Painting = true;
        state.PaintValue = item.IsSelected;
        state.Anchor = index;

        // 捕获鼠标：滑出列表边界、甚至滑出窗口时还能继续刷。
        list.CaptureMouse();
        e.Handled = true;
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not ListBox list || StateOf(list) is not { } state) return;
        if (!state.Painting) return;

        // 松手在列表外时收不到 MouseUp，这里用按键状态兜底；中途退出批量模式也不能继续刷。
        if (e.LeftButton != MouseButtonState.Pressed || !GetCanDragToSelect(list))
        {
            Reset(list, state);
            return;
        }

        PaintTo(list, state, e.GetPosition(list));
    }

    private static void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox list && StateOf(list) is { } state) Reset(list, state);
    }

    private static void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is ListBox list && StateOf(list) is { } state) Reset(list, state);
    }

    /// <summary>清掉刷选状态并归还鼠标捕获。可重入：ReleaseMouseCapture 会再触发一次 LostMouseCapture。</summary>
    private static void Reset(ListBox list, PaintState state)
    {
        if (!state.Painting) return;

        state.Reset();
        if (list.IsMouseCaptured) list.ReleaseMouseCapture();
    }

    /// <summary>
    /// 把起始行到光标所在行之间整段设成同一个状态。用索引区间而不是逐帧命中，
    /// 快速甩鼠标时中间被跳过的那几行也不会漏掉。
    /// </summary>
    private static void PaintTo(ListBox list, PaintState state, Point position)
    {
        var container = ContainerAt(list, position);
        if (container?.DataContext is null) return;

        var index = list.Items.IndexOf(container.DataContext);
        if (index < 0) return;

        var start = Math.Min(state.Anchor, index);
        var end = Math.Max(state.Anchor, index);

        var sink = list.DataContext as IBulkSelectionSink;
        var changed = false;

        sink?.BeginBulkSelection();
        try
        {
            for (var i = start; i <= end && i < list.Items.Count; i++)
            {
                if (list.Items[i] is not ISelectableItem row) continue;
                if (row.IsSelected == state.PaintValue) continue;

                row.IsSelected = state.PaintValue;
                changed = true;
            }
        }
        finally
        {
            sink?.EndBulkSelection(changed);
        }
    }

    /// <summary>光标下是哪一行；行间空隙、滚动条、列表外一律返回 null。</summary>
    private static ListBoxItem? ContainerAt(ListBox list, Point point)
    {
        DependencyObject? hit = list.InputHitTest(point) as DependencyObject;

        while (hit != null && hit is not ListBoxItem)
            hit = ParentOf(hit);

        return hit as ListBoxItem;
    }

    /// <summary>按钮、输入框、滚动条上的按下不算刷选，交给控件自己处理。</summary>
    private static bool IsInteractiveSource(object? source)
    {
        var current = source as DependencyObject;

        while (current != null && current is not ListBoxItem)
        {
            if (current is ButtonBase or TextBoxBase or ScrollBar) return true;
            current = ParentOf(current);
        }

        return false;
    }

    /// <summary>Run 这类 ContentElement 不在视觉树上，直接调 VisualTreeHelper 会抛异常。</summary>
    private static DependencyObject? ParentOf(DependencyObject node) =>
        node is Visual or Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);

    private sealed class PaintState
    {
        public bool Painting;
        public bool PaintValue;
        public int Anchor;
        public Window? Window;
        public EventHandler? Deactivated;

        public void Reset()
        {
            Painting = false;
            Anchor = 0;
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AllModsSky.App.ViewModels;

namespace AllModsSky.App.Views;

public partial class ModPoolView : UserControl
{
    /// <summary>批量模式下按住左键拖动：从起始行一路刷到光标所在行。</summary>
    private bool _painting;
    private bool _paintValue;
    private int _paintAnchor;

    public ModPoolView()
    {
        InitializeComponent();
    }

    private ModPoolViewModel? ViewModel => DataContext as ModPoolViewModel;

    /// <summary>批量模式下点整行切换选中，跟模组页一致；点按钮/输入框不算。</summary>
    private void PoolRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { BatchMode: true }) return;
        if (sender is not ListBoxItem { DataContext: PoolItemViewModel item }) return;
        if (IsInteractiveSource(e.OriginalSource)) return;

        item.IsSelected = !item.IsSelected;

        // 按下这一行的结果就是「刷」的目标状态：往下滑时经过的行都跟它对齐。
        _painting = true;
        _paintValue = item.IsSelected;
        _paintAnchor = Math.Max(0, PoolList.Items.IndexOf(item));

        ViewModel.NotifySelectionChanged();
        e.Handled = true;
    }

    private void PoolList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_painting) return;

        // 松手在列表外时收不到 MouseUp，这里用按键状态兜底。
        if (e.LeftButton != MouseButtonState.Pressed || ViewModel is not { BatchMode: true })
        {
            _painting = false;
            return;
        }

        PaintTo(e.GetPosition(PoolList));
    }

    private void PoolList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _painting = false;

    /// <summary>起始行到光标所在行整段设成同一个状态，快速甩鼠标也不会漏行。</summary>
    private void PaintTo(Point position)
    {
        if (ViewModel is not { } vm) return;
        if (ItemAt(position) is not { } item) return;

        var index = PoolList.Items.IndexOf(item);
        if (index < 0) return;

        var start = Math.Min(_paintAnchor, index);
        var end = Math.Max(_paintAnchor, index);

        var changed = false;
        for (var i = start; i <= end && i < PoolList.Items.Count; i++)
        {
            if (PoolList.Items[i] is not PoolItemViewModel row) continue;
            if (row.IsSelected == _paintValue) continue;

            row.IsSelected = _paintValue;
            changed = true;
        }

        if (changed) vm.NotifySelectionChanged();
    }

    private PoolItemViewModel? ItemAt(Point point)
    {
        if (PoolList.InputHitTest(point) is not DependencyObject hit) return null;

        while (hit != null && hit is not ListBoxItem)
        {
            hit = hit is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(hit)
                : LogicalTreeHelper.GetParent(hit);
        }

        return (hit as ListBoxItem)?.DataContext as PoolItemViewModel;
    }

    private static bool IsInteractiveSource(object? source)
    {
        var current = source as DependencyObject;

        while (current != null && current is not ListBoxItem)
        {
            if (current is ButtonBase or TextBoxBase or ScrollBar) return true;

            // Run 这类 ContentElement 不在视觉树上，直接调 VisualTreeHelper 会抛异常
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }
}

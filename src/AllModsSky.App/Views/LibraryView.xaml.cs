using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using AllModsSky.App.ViewModels;

namespace AllModsSky.App.Views;

public partial class LibraryView : UserControl
{
    private Point _dragStart;
    private ModItemViewModel? _dragItem;
    private bool _dragging;

    public LibraryView()
    {
        InitializeComponent();
    }

    private LibraryViewModel? ViewModel => DataContext as LibraryViewModel;

    private void ModList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(ModList);
        _dragItem = ItemAt(_dragStart);

        // 批量模式下：单击整行（按钮/开关/滚动条除外）切换选中，不需要 Ctrl。
        if (ViewModel is { BatchMode: true } && _dragItem != null && !IsInteractiveSource(e.OriginalSource))
        {
            _dragItem.IsSelected = !_dragItem.IsSelected;
            _dragItem = null; // 避免接着触发拖拽排序
            e.Handled = true;
        }
    }

    private void ModList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null || _dragging) return;

        var position = e.GetPosition(ModList);
        if (Math.Abs(position.Y - _dragStart.Y) < 6 && Math.Abs(position.X - _dragStart.X) < 6) return;

        _dragging = true;
        LiftItem(_dragItem);
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
            ResetLift(_dragItem);
            _dragging = false;
            _dragItem = null;
        }
    }

    private void ModList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ModItemViewModel))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
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

        ResetLift(source);
        var before = CapturePositions();

        ViewModel?.MoveItem(source, target, after);
        e.Handled = true;

        ModList.UpdateLayout();
        AnimatePositions(before);
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

    private void LiftItem(ModItemViewModel? item)
    {
        var container = ContainerOf(item);
        if (container == null) return;

        container.Opacity = 0.75;
        container.Effect = new DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 3,
            Opacity = 0.35,
            Color = Colors.Black
        };
        container.RenderTransformOrigin = new Point(0.5, 0.5);
        container.RenderTransform = new ScaleTransform(1.02, 1.02);
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

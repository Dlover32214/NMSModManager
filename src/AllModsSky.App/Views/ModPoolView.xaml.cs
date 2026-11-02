using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AllModsSky.App.ViewModels;

namespace AllModsSky.App.Views;

public partial class ModPoolView : UserControl
{
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
        ViewModel.NotifySelectionChanged();
        e.Handled = true;
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

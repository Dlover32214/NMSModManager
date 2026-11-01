using System.Windows.Controls;
using System.Windows.Input;
using AllModsSky.App.ViewModels;

namespace AllModsSky.App.Views;

public partial class DiscoverView : UserControl
{
    public DiscoverView()
    {
        InitializeComponent();
    }

    private void OogcSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (DataContext is DiscoverViewModel vm) vm.SearchCommand.Execute(null);
    }
}

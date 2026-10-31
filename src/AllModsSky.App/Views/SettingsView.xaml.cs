using System.Windows;
using System.Windows.Controls;
using AllModsSky.App.ViewModels;

namespace AllModsSky.App.Views;

public partial class SettingsView : UserControl
{
    private bool _syncing;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => SyncPasswordFromViewModel();
        DataContextChanged += (_, _) => SyncPasswordFromViewModel();
    }

    private void SyncPasswordFromViewModel()
    {
        if (DataContext is not SettingsViewModel vm) return;

        _syncing = true;
        NexusKeyBox.Password = vm.NexusKey ?? "";
        _syncing = false;
    }

    private void NexusKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        if (DataContext is SettingsViewModel vm)
            vm.NexusKey = NexusKeyBox.Password;
    }
}

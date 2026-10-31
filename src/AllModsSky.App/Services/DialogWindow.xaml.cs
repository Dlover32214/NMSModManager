using System.Windows;
using System.Windows.Input;

namespace AllModsSky.App.Services;

public partial class DialogWindow : Window
{
    public DialogWindow()
    {
        InitializeComponent();
    }

    public ConfirmResult Result { get; private set; } = ConfirmResult.None;

    public void Configure(string title, string message, string primary, string? danger, bool alertOnly)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primary;
        IconText.Text = alertOnly ? "i" : "?";

        if (string.IsNullOrWhiteSpace(danger))
        {
            DangerButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            DangerButton.Content = danger;
            DangerButton.Visibility = Visibility.Visible;
        }

        if (alertOnly) CancelButton.Visibility = Visibility.Collapsed;

        Loaded += (_, _) => PrimaryButton.Focus();
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        Result = ConfirmResult.Primary;
        DialogResult = true;
    }

    private void Danger_Click(object sender, RoutedEventArgs e)
    {
        Result = ConfirmResult.Danger;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Result = ConfirmResult.Cancel;
        DialogResult = false;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}

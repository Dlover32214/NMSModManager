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

    /// <summary>输入框里的文本；没启用输入时为空串。</summary>
    public string InputText => InputBox.Text;

    public void Configure(string title, string message, string primary, string? danger, bool alertOnly,
        string? input = null)
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

        if (input != null)
        {
            InputBox.Text = input;
            InputBox.Visibility = Visibility.Visible;

            // 输入框里回车 = 确定（TextBox 默认不吃回车，不接这一下就得去点按钮）
            InputBox.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                Primary_Click(this, new RoutedEventArgs());
            };

            Loaded += (_, _) =>
            {
                InputBox.Focus();
                InputBox.SelectAll();
            };
        }
        else
        {
            Loaded += (_, _) => PrimaryButton.Focus();
        }
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

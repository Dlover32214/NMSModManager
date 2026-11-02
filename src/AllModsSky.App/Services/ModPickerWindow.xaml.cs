using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using AllModsSky.App.Mvvm;
using AllModsSky.Core;

namespace AllModsSky.App.Services;

/// <summary>挑模组用的弹窗：把模组库里还没进当前配置的模组列出来，勾选后一次加进去。</summary>
public partial class ModPickerWindow : Window
{
    private readonly ObservableCollection<PickerItem> _visible = new();

    public ModPickerWindow(IEnumerable<ModRecord> candidates, string profileName)
    {
        InitializeComponent();

        Candidates = candidates.Select(r => new PickerItem(r)).ToList();
        foreach (var item in Candidates)
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PickerItem.IsSelected)) UpdateCount();
            };

        TitleText.Text = $"添加到「{profileName}」";
        MessageText.Text = "勾选要放进这套配置的模组。加入后会出现在列表最下面，可以拖动排序。";
        ModList.ItemsSource = _visible;

        Filter("");
        UpdateCount();
        Loaded += (_, _) => SearchBox.Focus();
    }

    public List<PickerItem> Candidates { get; }

    public IReadOnlyList<ModRecord> Selected =>
        Candidates.Where(i => i.IsSelected).Select(i => i.Record).ToList();

    private void Filter(string query)
    {
        query = (query ?? "").Trim();

        _visible.Clear();
        foreach (var item in Candidates)
        {
            if (query.Length > 0 && !item.Matches(query)) continue;
            _visible.Add(item);
        }

        var empty = Candidates.Count == 0 || _visible.Count == 0;
        ListHost.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = Candidates.Count == 0
            ? "模组库里的模组都已经在这套配置里了。"
            : "没有匹配的模组。";
        AllButton.IsEnabled = _visible.Count > 0;
    }

    private void UpdateCount()
    {
        var count = Candidates.Count(i => i.IsSelected);
        PrimaryButton.Content = count > 0 ? $"添加 ({count})" : "添加";
        PrimaryButton.IsEnabled = count > 0;
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        Filter(SearchBox.Text);

    private void Row_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBoxItem { DataContext: PickerItem item }) return;
        if (e.OriginalSource is DependencyObject source && FindCheckBox(source) != null) return;

        item.IsSelected = !item.IsSelected;
        e.Handled = true;
    }

    private static object? FindCheckBox(DependencyObject source)
    {
        var current = source;
        while (current != null)
        {
            if (current is System.Windows.Controls.CheckBox) return current;
            if (current is System.Windows.Controls.ListBoxItem) return null;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _visible) item.IsSelected = true;
        UpdateCount();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Candidates) item.IsSelected = false;
        UpdateCount();
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (Selected.Count == 0) return;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}

public sealed class PickerItem : ObservableObject
{
    private bool _isSelected;

    public PickerItem(ModRecord record) => Record = record;

    public ModRecord Record { get; }
    public string Name => Record.Name;
    public string MetaText => $"{Record.VersionText} · {Record.SizeText} · {Record.Source}";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool Matches(string query) =>
        Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Record.Source.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Record.VersionText.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Record.EffectiveFolder.Contains(query, StringComparison.OrdinalIgnoreCase);
}

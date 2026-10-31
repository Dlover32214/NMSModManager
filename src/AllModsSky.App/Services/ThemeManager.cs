using System.Windows;

namespace AllModsSky.App.Services;

public static class ThemeManager
{
    public static bool IsDark { get; private set; } = true;

    public static void Apply(bool dark)
    {
        var app = Application.Current;
        if (app == null) return;

        var source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var dictionary = new ResourceDictionary { Source = source };
        var merged = app.Resources.MergedDictionaries;

        for (var i = 0; i < merged.Count; i++)
        {
            var current = merged[i].Source?.OriginalString ?? "";
            if (current.Contains("Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
                current.Contains("Light.xaml", StringComparison.OrdinalIgnoreCase))
            {
                merged[i] = dictionary;
                IsDark = dark;
                return;
            }
        }

        merged.Insert(0, dictionary);
        IsDark = dark;
    }

    public static void Toggle() => Apply(!IsDark);
}

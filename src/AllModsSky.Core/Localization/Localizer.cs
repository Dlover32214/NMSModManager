using System.Globalization;
using System.Text.Json;

namespace AllModsSky.Core.Localization;

/// <summary>
/// 用户可见字符串的本地化入口。
///
/// 约定：
///   · 源字符串（中文）就是资源 key，和 gettext 的 msgid 一样；
///   · 每种语言一个 JSON 目录，放在 Localization/Translations/ 下；
///   · 中文目录可以只保留覆盖项，找不到翻译时回退中文源字符串；
///   · 所有用户页面上的新字符串都必须经过 <see cref="T"/> 或 <see cref="F"/>，
///     不要再把中文直接写进 XAML / ViewModel；
///   · 日志和异常技术细节可以保持中文，不进入用户界面目录。
///
/// 切换语言时调用 <see cref="Apply"/>，界面由 App 重新加载；不需要退出进程。
/// </summary>
public static class L
{
    public const string SystemLanguage = "system";
    public const string ChineseLanguage = "zh-CN";
    public const string EnglishLanguage = "en-US";

    private const string ResourcePrefix = "AllModsSky.Core.Localization.Translations.";

    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs = LoadCatalogs();

    /// <summary>语言或格式化文化变化后触发；界面层据此刷新。</summary>
    public static event Action? LanguageChanged;

    /// <summary>当前实际生效的语言：zh-CN 或 en-US。</summary>
    public static string CurrentLanguage { get; private set; } = ChineseLanguage;

    /// <summary>设置里保存的原始值：system / zh-CN / en-US。</summary>
    public static string LanguageSetting { get; private set; } = SystemLanguage;

    public static bool IsEnglish => CurrentLanguage == EnglishLanguage;

    /// <summary>按设置切换语言；system 跟随操作系统 UI 语言。</summary>
    public static void Apply(string? setting)
    {
        LanguageSetting = NormalizeSetting(setting);

        var cultureName = LanguageSetting == SystemLanguage
            ? DetectSystemLanguage()
            : LanguageSetting;

        CurrentLanguage = cultureName == EnglishLanguage ? EnglishLanguage : ChineseLanguage;

        var culture = CultureInfo.GetCultureInfo(CurrentLanguage);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        // 日期、数字和排序也跟着界面语言走；不改变文件的 UTF-8 读写。
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;

        LanguageChanged?.Invoke();
    }

    /// <summary>
    /// 取用户可见字符串。key 是中文源字符串，也是各语言目录里的 msgid。
    /// </summary>
    public static string T(string source)
    {
        if (string.IsNullOrEmpty(source)) return source;
        if (Catalogs.TryGetValue(CurrentLanguage, out var catalog))
        {
            if (catalog.TryGetValue(source, out var exact))
                return exact;

            // 调用方常把 "\n" 留在本地化字符串末尾；目录里不保存这些纯排版换行。
            // 命中时把首尾换行原样补回，保证对话框/日志的排版不丢。
            var start = 0;
            while (start < source.Length && (source[start] == '\r' || source[start] == '\n')) start++;
            var end = source.Length;
            while (end > start && (source[end - 1] == '\r' || source[end - 1] == '\n')) end--;

            if ((start > 0 || end < source.Length) &&
                catalog.TryGetValue(source[start..end], out var trimmed))
                return source[..start] + trimmed + source[end..];
        }

        return source;
    }

    /// <summary>带格式参数的本地化字符串；参数顺序与源字符串中的 {0}、{1} 一致。</summary>
    public static string F(string source, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, T(source), args);

    private static string NormalizeSetting(string? setting)
    {
        var value = (setting ?? "").Trim();
        if (value.Length == 0) return SystemLanguage;

        if (value.Equals(SystemLanguage, StringComparison.OrdinalIgnoreCase)) return SystemLanguage;
        if (value.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return EnglishLanguage;
        if (value.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return ChineseLanguage;

        return SystemLanguage;
    }

    private static string DetectSystemLanguage()
    {
        var name = CultureInfo.CurrentUICulture.Name;
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return ChineseLanguage;
        return EnglishLanguage;
    }

    private static Dictionary<string, Dictionary<string, string>> LoadCatalogs()
    {
        var catalogs = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in new[] { ChineseLanguage, EnglishLanguage })
            catalogs[language] = LoadCatalog(language);
        return catalogs;
    }

    private static Dictionary<string, string> LoadCatalog(string language)
    {
        var empty = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var assembly = typeof(L).Assembly;
            var names = assembly.GetManifestResourceNames();
            var exact = ResourcePrefix + language + ".json";
            var resourceName = names.FirstOrDefault(n => n.Equals(exact, StringComparison.OrdinalIgnoreCase))
                               ?? names.FirstOrDefault(n =>
                                   n.EndsWith(".Translations." + language + ".json",
                                       StringComparison.OrdinalIgnoreCase));
            if (resourceName == null) return empty;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return empty;

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? empty;
        }
        catch
        {
            // 单个语言目录损坏不应阻止程序启动；回退中文源字符串。
            return empty;
        }
    }
}

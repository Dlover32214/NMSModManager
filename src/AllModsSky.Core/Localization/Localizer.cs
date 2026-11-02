using System.Globalization;
using System.Text.Json;

namespace AllModsSky.Core.Localization;

/// <summary>
/// 用户可见字符串的本地化入口。
///
/// 约定：
///   · 源字符串（中文）就是资源 key，和 gettext 的 msgid 一样；
///   · 中文是默认语言，找不到英文翻译时原样返回中文；
///   · 所有用户页面上的新字符串都必须经过 <see cref="T"/> 或 <see cref="F"/>，
///     不要再把中文直接写进 XAML / ViewModel；
///   · 日志和异常技术细节可以保持中文，不进入用户界面目录。
///
/// 语言只在启动时应用一次，修改语言后重启程序生效，避免 WPF 已生成文本的刷新问题。
/// </summary>
public static class L
{
    public const string SystemLanguage = "system";
    public const string ChineseLanguage = "zh-CN";
    public const string EnglishLanguage = "en-US";

    private static readonly Dictionary<string, string> English = LoadEnglish();

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
    }

    /// <summary>
    /// 取用户可见字符串。key 是中文源字符串，也是英文目录里的 msgid。
    /// </summary>
    public static string T(string source)
    {
        if (string.IsNullOrEmpty(source)) return source;
        return IsEnglish && English.TryGetValue(source, out var translated) ? translated : source;
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

    private static Dictionary<string, string> LoadEnglish()
    {
        const string resourceName = "AllModsSky.Core.Localization.Strings.english.json";
        try
        {
            var assembly = typeof(L).Assembly;
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return new Dictionary<string, string>(StringComparer.Ordinal);

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch
        {
            // 资源损坏不应阻止程序启动；退化为中文。
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}

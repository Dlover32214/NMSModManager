using System.Windows.Markup;

namespace AllModsSky.App.Localization;

/// <summary>
/// XAML 用的本地化标记扩展：<c>Text="{loc:T 设置}"</c>。
<<<<<<< HEAD
/// Key 是中文源字符串；语言目录见 Core/Localization/Translations/。
=======
/// Key 是中文源字符串；英文目录见 Core/Localization/Strings.en.json。
>>>>>>> 6576be8bf42bc977aa5dec75e25c09d16bffd31c
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}

using System.Globalization;

namespace AllModsSky.Core.Infrastructure;

/// <summary>
/// 按「字」而不是按 UTF-16 长度截断文本。
///
/// 一个汉字、一个字母都算 1 个字；emoji（代理对）和带组合符的字（比如
/// 「é」由 e + ́ 组成）也只算 1 个字——用 <see cref="string.Length"/> 会算成 2。
/// </summary>
public static class TextLimit
{
    /// <summary>数一段文本有多少个「字」。</summary>
    public static int Count(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : StringInfo.ParseCombiningCharacters(text).Length;

    /// <summary>超过 <paramref name="maxChars"/> 个字就截断，返回截断后的文本。</summary>
    public static string Clamp(string? text, int maxChars)
    {
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0 || maxChars <= 0) return "";

        var starts = StringInfo.ParseCombiningCharacters(trimmed);
        if (starts.Length <= maxChars) return trimmed;

        // 直接按字符下标切会把代理对劈成两半，必须用「每个字的起始下标」
        return trimmed[..starts[maxChars]];
    }
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace AllModsSky.Core.Infrastructure;

public sealed record ParsedVersion(double Value, bool IsMinimum, string Raw)
{
    public override string ToString() => Raw;
}

/// <summary>适配状态：Ok / Warn / Unknown。</summary>
public sealed record VersionVerdict(string State, string Text)
{
    public bool IsWarning => State == "Warn";
    public bool IsUnknown => State == "Unknown";
}

/// <summary>
/// 从模组名/文件名里解析它声明的游戏版本，并与当前游戏版本比对。
/// 支持 7.01、5.60、7.x、7.0+、7_x、v7.01（AndUp）等写法。
/// </summary>
public static class VersionParser
{
    private static readonly Regex Wildcard = new(@"(?<!\d)(\d{1,2})\.[xX]", RegexOptions.Compiled);
    private static readonly Regex Numeric = new(@"(?<![\d.])(\d{1,2})[._](\d{1,2})(?![\d.])", RegexOptions.Compiled);
    private static readonly Regex MinimumHint = new(@"(AndUp|and up|And Up|或更高|\+)", RegexOptions.Compiled);

    public static ParsedVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var wildcard = Wildcard.Match(text);
        if (wildcard.Success && int.TryParse(wildcard.Groups[1].Value, out var majorWildcard))
        {
            return new ParsedVersion(majorWildcard, true, wildcard.Value);
        }

        var match = Numeric.Match(text);
        if (!match.Success) return null;

        if (!int.TryParse(match.Groups[1].Value, out var major)) return null;
        if (!int.TryParse(match.Groups[2].Value, out var minor)) return null;
        if (minor > 99) return null;

        return new ParsedVersion(major + minor / 100.0, MinimumHint.IsMatch(text), match.Value);
    }

    /// <summary>把版本号统一成 7.01 形式；无法解析时原样返回。</summary>
    public static string Normalize(string? text)
    {
        var parsed = Parse(text);
        if (parsed == null) return (text ?? "").Trim();
        return parsed.Value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    public static VersionVerdict Evaluate(string? modVersionText, string? gameVersionText)
    {
        var mod = Parse(modVersionText);
        if (mod == null) return new VersionVerdict("Unknown", L.T("未标注适用版本"));

        var game = Parse(gameVersionText);
        if (game == null) return new VersionVerdict("Unknown", L.F("标注 {0} · 游戏版本未知", mod.Raw));

        if (mod.IsMinimum)
        {
            return mod.Value <= game.Value + 0.005
                ? new VersionVerdict("Ok", L.F("要求 {0} 及以上", mod.Raw))
                : new VersionVerdict("Warn", L.F("要求 {0}，当前 {1}", mod.Raw, game.Raw));
        }

        var diff = game.Value - mod.Value;
        if (diff >= 1.0)
            return new VersionVerdict("Warn", L.F("模组 {0} · 游戏 {1}，落后一个大版本", mod.Raw, game.Raw));
        if (diff >= 0.5)
            return new VersionVerdict("Warn", L.F("模组 {0} · 游戏 {1}，可能已过旧", mod.Raw, game.Raw));

        return new VersionVerdict("Ok", L.F("模组 {0} · 游戏 {1}", mod.Raw, game.Raw));
    }
}

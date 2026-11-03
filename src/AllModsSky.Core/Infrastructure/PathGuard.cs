namespace AllModsSky.Core.Infrastructure;

/// <summary>
/// 不信任 library.json 里的目录名。
/// 定位已有条目一律从磁盘枚举，索引里的字符串只用来「查找」，不用来拼路径；
/// 只有「新建目标」这一种场景才允许 Path.Combine，且名字必须通过 IsSafeSegment。
/// </summary>
public static class PathGuard
{
    /// <summary>root 下的一个真实条目。</summary>
    public readonly record struct Entry(string Path, bool IsDirectory);

    /// <summary>
    /// 是否是合法的单段名字：不含路径分隔符、不是 . 或 ..。
    /// 仅用于「创建新条目」和 pak 文件名这类已知来源的短名。
    /// </summary>
    public static bool IsSafeSegment(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name is "." or "..") return false;
        return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    /// <summary>
    /// 名字里含路径分隔符 —— 这类值不可能对应磁盘上的条目，直接判定非法。
    /// <c>: ? * " &lt; &gt; |</c> 不在此列：它们在 Windows 上同样建不出文件，
    /// 但旧索引里很常见（如 <c>Better Ship: Redux</c>），交给 SafeFileName 规范化后再匹配。
    /// </summary>
    public static bool HasPathSeparator(string? name) =>
        !string.IsNullOrEmpty(name) && name.IndexOfAny(new[] { '\\', '/' }) >= 0;

    /// <summary>
    /// 在 root 下枚举真实条目并按名字匹配。
    /// 先按原名匹配；匹配不到再按 SafeFileName 规范化一次 —— 磁盘上的名字一定是净化过的，
    /// 而旧数据里的 FolderName 可能还带着 : ? * 这些字符。
    /// 找不到返回 null —— 调用方应视为「不存在」，而不是去拼路径。
    /// </summary>
    public static string? FindExisting(string? root, string? name, bool wantFile = false)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(name)) return null;
        if (HasPathSeparator(name)) return null;
        if (!Directory.Exists(root)) return null;

        var hit = Match(root, name, wantFile);
        if (hit != null) return hit;

        var normalized = FileOps.SafeFileName(name, 80);
        return string.Equals(normalized, name, StringComparison.Ordinal)
            ? null
            : Match(root, normalized, wantFile);
    }

    private static string? Match(string root, string name, bool wantFile)
    {
        try
        {
            var entries = wantFile
                ? Directory.EnumerateFiles(root)
                : Directory.EnumerateDirectories(root);

            foreach (var entry in entries)
            {
                if (string.Equals(Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"枚举 {root} 失败：{ex.Message}");
        }

        return null;
    }

    /// <summary>把 root 下的条目建成索引（名字 → 路径 + 是否目录），批量同步时避免反复枚举。</summary>
    public static Dictionary<string, Entry> IndexEntries(string? root)
    {
        var index = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return index;

        try
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(root))
                index[Path.GetFileName(path)] = new Entry(path, Directory.Exists(path));
        }
        catch (Exception ex)
        {
            AppLog.Warn($"枚举 {root} 失败：{ex.Message}");
        }

        return index;
    }

    public static string Describe(string? name) => string.IsNullOrWhiteSpace(name) ? "(空)" : name;
}

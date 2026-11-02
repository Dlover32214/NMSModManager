using System.Text.RegularExpressions;

namespace AllModsSky.Core;

public sealed class ConflictTarget
{
    public string Name { get; init; } = "";
    public string Folder { get; init; } = "";
    public bool Enabled { get; init; }
    public int Order { get; init; }
}

public sealed class ConflictEntry
{
    public string ModName { get; init; } = "";
    public bool IsPatch { get; init; }
    public bool Enabled { get; init; }
    public int Order { get; init; }
    public HashSet<string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ConflictGroup
{
    public string VanillaPath { get; init; } = "";
    public List<ConflictEntry> Entries { get; init; } = new();
    public bool HardMbin { get; init; }
    public List<string> SharedProperties { get; init; } = new();

    public string Kind =>
        HardMbin ? "MBIN 替换冲突"
        : SharedProperties.Count > 0 ? "EXML 同属性冲突"
        : "EXML 同文件（属性不同）";

    public ConflictEntry? Winner =>
        Entries.Where(e => e.Enabled).OrderBy(e => e.Order).FirstOrDefault()
        ?? Entries.OrderBy(e => e.Order).FirstOrDefault();

    public string WinnerName => Winner?.ModName ?? "";
    public string ModNames => string.Join(" / ", Entries.OrderBy(e => e.Order).Select(e => e.ModName));
}

public sealed class ConflictScanResult
{
    public List<ConflictGroup> Conflicts { get; } = new();
    public int ScannedMods { get; set; }
    public int ScannedFiles { get; set; }
    public string Summary =>
        Conflicts.Count == 0
            ? $"扫描 {ScannedMods} 个模组、{ScannedFiles} 个 EXML/MBIN 文件，没有路径重叠"
            : $"{Conflicts.Count} 处重叠 · MBIN 冲突 {Conflicts.Count(c => c.HardMbin)} · " +
              $"EXML 同属性 {Conflicts.Count(c => !c.HardMbin && c.SharedProperties.Count > 0)} · " +
              $"仅同文件 {Conflicts.Count(c => !c.HardMbin && c.SharedProperties.Count == 0)}";
}

/// <summary>
/// 5.50+ 的模组是散装 EXML / MBIN，冲突判定看「同一个 vanilla 路径」被几个模组碰到：
///   · 多个 MBIN 替换同一路径 → 只有最后加载的生效（硬冲突）
///   · 多个 EXML 补丁改同一路径的同一属性 → 也是最后加载的生效
///   · EXML 补丁 + MBIN 替换同路径 → 允许（补丁叠加在替换之上）
/// 列表顺序即加载顺序：越靠上优先级越高，冲突时覆盖靠下的。
/// </summary>
public static class ConflictAnalyzer
{
    private static readonly Regex PropertyName = new(
        "Property\\s+name\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ConflictScanResult Scan(IEnumerable<ConflictTarget> targets, CancellationToken cancellation = default)
    {
        var result = new ConflictScanResult();
        var map = new Dictionary<string, List<ConflictEntry>>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in targets)
        {
            cancellation.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(target.Folder) || !Directory.Exists(target.Folder)) continue;
            result.ScannedMods++;

            string[] files;
            try
            {
                files = Directory.GetFiles(target.Folder, "*.*", SearchOption.AllDirectories)
                    .Where(f =>
                    {
                        var ext = Path.GetExtension(f);
                        return ext.Equals(".EXML", StringComparison.OrdinalIgnoreCase)
                            || ext.Equals(".MBIN", StringComparison.OrdinalIgnoreCase);
                    })
                    .ToArray();
            }
            catch (Exception ex)
            {
                AppLog.Warn($"扫描冲突时读取失败 {target.Folder}：{ex.Message}");
                continue;
            }

            foreach (var file in files)
            {
                cancellation.ThrowIfCancellationRequested();
                result.ScannedFiles++;

                string relative;
                try { relative = Path.GetRelativePath(target.Folder, file); }
                catch { continue; }

                var key = relative.Replace('/', '\\').ToUpperInvariant();
                if (!map.TryGetValue(key, out var list))
                {
                    list = new List<ConflictEntry>();
                    map[key] = list;
                }

                var isPatch = Path.GetExtension(file).Equals(".EXML", StringComparison.OrdinalIgnoreCase);
                var entry = list.FirstOrDefault(e => string.Equals(e.ModName, target.Name, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    entry = new ConflictEntry
                    {
                        ModName = target.Name,
                        IsPatch = isPatch,
                        Enabled = target.Enabled,
                        Order = target.Order
                    };
                    list.Add(entry);
                }

                if (entry.IsPatch)
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Length <= 8 * 1024 * 1024)
                        {
                            var text = File.ReadAllText(file);
                            foreach (Match match in PropertyName.Matches(text))
                                entry.Properties.Add(match.Groups[1].Value);
                        }
                    }
                    catch { }
                }
            }
        }

        foreach (var (path, entries) in map)
        {
            if (entries.Count < 2) continue;

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries.Where(e => e.IsPatch))
            {
                foreach (var property in entry.Properties)
                {
                    counts.TryGetValue(property, out var count);
                    counts[property] = count + 1;
                }
            }

            var shared = counts.Where(kv => kv.Value > 1)
                .Select(kv => kv.Key)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();

            result.Conflicts.Add(new ConflictGroup
            {
                VanillaPath = path,
                Entries = entries.OrderBy(e => e.Order).ThenBy(e => e.ModName, StringComparer.OrdinalIgnoreCase).ToList(),
                HardMbin = entries.Count(e => !e.IsPatch) > 1,
                SharedProperties = shared
            });
        }

        result.Conflicts.Sort((a, b) =>
        {
            var byHard = b.HardMbin.CompareTo(a.HardMbin);
            if (byHard != 0) return byHard;
            var byProps = b.SharedProperties.Count.CompareTo(a.SharedProperties.Count);
            if (byProps != 0) return byProps;
            return string.Compare(a.VanillaPath, b.VanillaPath, StringComparison.OrdinalIgnoreCase);
        });

        return result;
    }
}

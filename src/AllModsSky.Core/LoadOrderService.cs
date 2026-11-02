using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AllModsSky.Core;

public sealed class LoadOrderEntry
{
    public string Name { get; init; } = "";
    public int Priority { get; init; }
    public bool Enabled { get; init; }
}

public sealed class LoadOrderReadResult
{
    public List<LoadOrderEntry> Entries { get; } = new();
    public string FilePath { get; set; } = "";
    public string? Error { get; init; }
    public bool Ok => Error == null;
}

public sealed class LoadOrderApplyResult
{
    public int Updated { get; set; }
    public int Missing { get; set; }
    public string? Error { get; set; }
    public string? BackupPath { get; set; }
    public bool Ok => Error == null && Updated > 0;
}

/// <summary>
/// 加载顺序：Binaries\SETTINGS\GCMODSETTINGS.MXML 里的 ModPriority。
/// 只改 ModPriority，Author / ID / Enabled / Dependencies 原样保留。
/// </summary>
public sealed class LoadOrderService
{
    private static readonly Regex BlockRegex = new(
        "<Property\\s+name=\"Data\"\\s+value=\"GcModSettingsInfo\"[^>]*>(.*?)</Property>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NameRegex = new(
        "name=\"Name\"\\s+value=\"([^\"]*)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PriorityRegex = new(
        "name=\"ModPriority\"\\s+value=\"(-?\\d+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EnabledRegex = new(
        "name=\"Enabled\"\\s+value=\"(true|false)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string SettingsFile(string? gameDir) =>
        string.IsNullOrWhiteSpace(gameDir) ? "" : Path.Combine(gameDir, "Binaries", "SETTINGS", "GCMODSETTINGS.MXML");

    public LoadOrderReadResult Read(string? gameDir)
    {
        var file = SettingsFile(gameDir);
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
            return new LoadOrderReadResult
            {
                FilePath = file,
                Error = "找不到 GCMODSETTINGS.MXML（先进一次游戏让它生成）"
            };

        var result = new LoadOrderReadResult { FilePath = file };
        try
        {
            var text = File.ReadAllText(file);
            foreach (Match block in BlockRegex.Matches(text))
            {
                var body = block.Groups[1].Value;
                var name = NameRegex.Match(body);
                if (!name.Success || string.IsNullOrWhiteSpace(name.Groups[1].Value)) continue;

                var priority = PriorityRegex.Match(body);
                var enabled = EnabledRegex.Match(body);

                result.Entries.Add(new LoadOrderEntry
                {
                    Name = name.Groups[1].Value,
                    Priority = priority.Success && int.TryParse(priority.Groups[1].Value, out var p) ? p : 0,
                    Enabled = !enabled.Success || enabled.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase)
                });
            }

            result.Entries.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }
        catch (Exception ex)
        {
            return new LoadOrderReadResult { FilePath = file, Error = ex.Message };
        }

        return result;
    }

    /// <summary>按传入顺序写回 ModPriority（第 0 个 = 最高优先级 = 值最小）。</summary>
    public LoadOrderApplyResult Apply(string? gameDir, IReadOnlyList<string> orderedModNames)
    {
        var result = new LoadOrderApplyResult();
        var file = SettingsFile(gameDir);

        if (string.IsNullOrEmpty(file) || !File.Exists(file))
        {
            result.Error = "找不到 GCMODSETTINGS.MXML";
            return result;
        }

        try
        {
            // File.ReadAllText 会吞掉 BOM，所以 BOM 必须从原始字节判断，否则"保留 BOM"永远不成立
            var rawBytes = File.ReadAllBytes(file);
            var hasBom = rawBytes.Length >= 3
                         && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF;
            var original = File.ReadAllText(file);
            var priorities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < orderedModNames.Count; i++)
            {
                var name = orderedModNames[i];
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!priorities.ContainsKey(name)) priorities[name] = i;
            }

            var builder = new StringBuilder(original.Length + 128);
            var lastIndex = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match block in BlockRegex.Matches(original))
            {
                builder.Append(original, lastIndex, block.Index - lastIndex);
                lastIndex = block.Index + block.Length;

                var body = block.Value;
                var nameMatch = NameRegex.Match(body);
                var name = nameMatch.Success ? nameMatch.Groups[1].Value : "";

                if (!string.IsNullOrEmpty(name) && priorities.TryGetValue(name, out var priority))
                {
                    var replaced = PriorityRegex.Replace(body,
                        m => "name=\"ModPriority\" value=\"" + priority.ToString() + "\"", 1);
                    builder.Append(replaced);
                    // 只有真的替换到了 ModPriority 才算更新，避免统计虚高、界面误报"已写入"
                    if (!string.Equals(replaced, body, StringComparison.Ordinal)) result.Updated++;
                    seen.Add(name);
                }
                else
                {
                    builder.Append(body);
                }
            }

            builder.Append(original, lastIndex, original.Length - lastIndex);

            result.Missing = priorities.Keys.Count(k => !seen.Contains(k));

            var updatedText = builder.ToString();
            if (result.Updated > 0 && !string.Equals(updatedText, original, StringComparison.Ordinal))
            {
                // 写回前先确认结果仍是合法 XML：正则改坏时宁可报错，也不要把游戏配置写脏
                try
                {
                    XDocument.Parse(updatedText);
                }
                catch (Exception ex)
                {
                    result.Error = "生成的 GCMODSETTINGS.MXML 不是合法 XML，已取消写入：" + ex.Message;
                    return result;
                }

                result.BackupPath = file + ".bak";
                try { File.Copy(file, result.BackupPath, true); }
                catch (Exception ex) { AppLog.Warn("备份 GCMODSETTINGS 失败：" + ex.Message); }

                AtomicWrite(file, updatedText, new UTF8Encoding(hasBom));
            }
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            AppLog.Error(ex, "写入加载顺序");
        }

        return result;
    }

    /// <summary>
    /// 同目录临时文件 + File.Replace 原子替换。
    /// GCMODSETTINGS.MXML 是游戏读取的 mod 清单：裸写时进程被杀/断电会把它截断，
    /// 所以先写一份完整副本，再一次性换入，失败时保留原文件。
    /// </summary>
    private static void AtomicWrite(string path, string text, Encoding encoding)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory)) directory = ".";
        var temp = Path.Combine(directory,
            Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, text, encoding);
            if (File.Exists(path)) File.Replace(temp, path, null, true);
            else File.Move(temp, path);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception ex) { AppLog.Warn("清理 GCMODSETTINGS 临时文件失败：" + ex.Message); }
        }
    }

    /// <summary>游戏里的模组名是大写的 XML 转义形式。</summary>
    public static string ToXmlName(string folderOrName) =>
        SecurityElement.Escape((folderOrName ?? "").ToUpperInvariant()) ?? "";
}

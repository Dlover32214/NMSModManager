using System.Text;
using System.Text.RegularExpressions;

namespace AllModsSky.Core;

public sealed class LogLine
{
    public string File { get; init; } = "";
    public string Text { get; init; } = "";
    public string Display => $"{File}: {Text}";
}

public sealed class DiagnosticsResult
{
    public List<string> LogFiles { get; } = new();
    public List<LogLine> Errors { get; } = new();
    public Dictionary<string, List<LogLine>> Suspects { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int ScannedLines { get; set; }
    public string? Note { get; set; }
}

/// <summary>从游戏日志里挑出报错行，并把提到某个模组的行归到那个模组上。</summary>
public static class LogDiagnostics
{
    private static readonly Regex ErrorPattern = new(
        "\\b(error|failed|failure|exception|unable to|could not|cannot|missing|corrupt|invalid|crash|assert)\\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int MaxLinesPerFile = 20_000;
    private const int TailBytes = 4 * 1024 * 1024;
    private const int MaxErrors = 4_000;

    public static IReadOnlyList<string> FindLogs(string? gameDir)
    {
        var files = new List<string>();

        void Add(string path)
        {
            try
            {
                if (File.Exists(path) && !files.Contains(path, StringComparer.OrdinalIgnoreCase))
                    files.Add(path);
            }
            catch { }
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(gameDir))
            {
                var gamedata = Path.Combine(gameDir, "GAMEDATA");
                foreach (var name in new[] { "FullLog.txt", "fullog.txt", "Log.txt" })
                    Add(Path.Combine(gamedata, name));

                if (Directory.Exists(gamedata))
                {
                    foreach (var file in Directory.GetFiles(gamedata, "*.log")) Add(file);
                    foreach (var file in Directory.GetFiles(gamedata, "*.txt"))
                    {
                        if (Path.GetFileName(file).Contains("log", StringComparison.OrdinalIgnoreCase))
                            Add(file);
                    }
                }

                var binaries = Path.Combine(gameDir, "Binaries");
                if (Directory.Exists(binaries))
                {
                    foreach (var file in Directory.GetFiles(binaries, "*.log")) Add(file);
                }
            }

            foreach (var root in new[]
                     {
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                             "HelloGames", "NMS"),
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                             "HelloGames", "NMS")
                     })
            {
                if (!Directory.Exists(root)) continue;
                foreach (var file in Directory.GetFiles(root, "*.log"))
                    Add(file);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("查找日志失败：" + ex.Message);
        }

        return files;
    }

    public static DiagnosticsResult Scan(string? gameDir, IReadOnlyList<ModRecord> mods,
        CancellationToken cancellation = default)
    {
        var result = new DiagnosticsResult();

        foreach (var file in FindLogs(gameDir))
        {
            cancellation.ThrowIfCancellationRequested();
            result.LogFiles.Add(file);

            string[] lines;
            try { lines = ReadTail(file, MaxLinesPerFile); }
            catch (Exception ex)
            {
                AppLog.Warn($"读取日志失败 {file}：{ex.Message}");
                continue;
            }

            foreach (var raw in lines)
            {
                cancellation.ThrowIfCancellationRequested();

                var line = raw.Trim();
                result.ScannedLines++;
                if (line.Length < 8 || !ErrorPattern.IsMatch(line)) continue;

                var entry = new LogLine { File = Path.GetFileName(file), Text = line };
                if (result.Errors.Count < MaxErrors) result.Errors.Add(entry);

                foreach (var record in mods)
                {
                    if (!Matches(record, line)) continue;

                    if (!result.Suspects.TryGetValue(record.Name, out var list))
                    {
                        list = new List<LogLine>();
                        result.Suspects[record.Name] = list;
                    }

                    if (list.Count < 10) list.Add(entry);
                    break;
                }
            }
        }

        if (result.LogFiles.Count == 0)
            result.Note = "没有找到游戏日志。先启动一次游戏，或让崩溃发生一次再诊断。";

        return result;
    }

    private static bool Matches(ModRecord record, string line)
    {
        foreach (var key in KeysOf(record))
        {
            if (key.Length < 3) continue;
            if (line.Contains(key, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static IEnumerable<string> KeysOf(ModRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.Name)) yield return record.Name;
        if (!string.IsNullOrWhiteSpace(record.FolderName)) yield return record.FolderName;

        foreach (var pak in record.PakFiles)
        {
            var name = Path.GetFileNameWithoutExtension(pak);
            if (!string.IsNullOrWhiteSpace(name)) yield return name;
        }
    }

    private static string[] ReadTail(string path, int maxLines)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var take = (int)Math.Min(stream.Length, TailBytes);
        var startAt = stream.Length - take;
        stream.Seek(startAt, SeekOrigin.Begin);

        var buffer = new byte[take];
        var read = 0;
        while (read < take)
        {
            var n = stream.Read(buffer, read, take - read);
            if (n <= 0) break;
            read += n;
        }

        var text = Encoding.UTF8.GetString(buffer, 0, read);
        var lines = text.Split('\n');

        var skip = startAt > 0 && lines.Length > 1 ? 1 : 0;
        if (lines.Length - skip <= maxLines)
            return lines.Skip(skip).ToArray();

        return lines.Skip(lines.Length - maxLines).ToArray();
    }
}

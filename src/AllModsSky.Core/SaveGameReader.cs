using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AllModsSky.Core;

/// <summary>
/// 存档键名映射（来自 NMSE 的 mapping.json，1400+ 条）。
/// 无人深空把存档 JSON 的键名混淆成三字符，这张表用来还原成可读名字。
/// </summary>
public static class SaveKeyMap
{
    private static readonly object Gate = new();
    private static Dictionary<string, string>? _keyToName;
    private static Dictionary<string, string>? _nameToKey;

    public static int Count
    {
        get
        {
            EnsureLoaded();
            return _keyToName!.Count;
        }
    }

    public static string NameOf(string key)
    {
        EnsureLoaded();
        return key != null && _keyToName!.TryGetValue(key, out var value) ? value : key ?? "";
    }

    public static string? KeyOf(string name)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(name)) return null;
        return _nameToKey!.TryGetValue(name, out var value) ? value : null;
    }

    private static void EnsureLoaded()
    {
        if (_keyToName != null) return;

        lock (Gate)
        {
            if (_keyToName != null) return;

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var reverse = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var assembly = typeof(SaveKeyMap).Assembly;
                var resource = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("mapping.json", StringComparison.OrdinalIgnoreCase));

                if (resource != null)
                {
                    using var stream = assembly.GetManifestResourceStream(resource);
                    if (stream != null)
                    {
                        using var document = JsonDocument.Parse(stream);
                        if (document.RootElement.TryGetProperty("Mapping", out var mapping) &&
                            mapping.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in mapping.EnumerateArray())
                            {
                                var key = item.TryGetProperty("Key", out var k) ? k.GetString() : null;
                                var name = item.TryGetProperty("Value", out var v) ? v.GetString() : null;
                                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(name)) continue;

                                map[key] = name;
                                reverse.TryAdd(name, key);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("加载存档键名映射失败：" + ex.Message);
            }

            _keyToName = map;
            _nameToKey = reverse;
        }
    }
}

public sealed class SaveInfo
{
    public string FilePath { get; init; } = "";
    public string FileName { get; init; } = "";
    public int SaveVersion { get; init; }
    public string SaveName { get; init; } = "";
    public string Platform { get; init; } = "";
    public string GameMode { get; init; } = "";
    public int PlaySeconds { get; init; }
    public DateTime Modified { get; init; }
    public long Size { get; init; }

    public string PlayText
    {
        get
        {
            if (PlaySeconds <= 0) return "";
            var hours = PlaySeconds / 3600;
            var minutes = PlaySeconds % 3600 / 60;
            return hours > 0 ? $"{hours} 小时 {minutes} 分" : $"{minutes} 分钟";
        }
    }

    public string SizeText => Humanize.Size(Size);
}

/// <summary>
/// 存档读取：.hg = 若干「16 字节块头 + LZ4 块」。
/// 块头：magic(0xFEEDA1E5) / 压缩长度 / 解压长度 / 保留。
/// 只解到能读到头部字段为止，避免把几十 MB 全解开。
/// </summary>
public static class SaveGameReader
{
    private const uint Magic = 0xFEEDA1E5;
    private const int MaxCompressedBlock = 64 * 1024 * 1024;
    private const int MaxDecompressedBlock = 256 * 1024 * 1024;

    public static (List<SaveInfo> Saves, string? Error) Scan(string? saveDir)
    {
        var saves = new List<SaveInfo>();

        if (string.IsNullOrWhiteSpace(saveDir) || !Directory.Exists(saveDir))
            return (saves, "存档目录不存在");

        string? firstError = null;

        try
        {
            foreach (var file in Directory.EnumerateFiles(saveDir, "*.hg"))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("mf_", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Contains("accountdata", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".backup", StringComparison.OrdinalIgnoreCase)) continue;

                var info = ReadOne(file, out var error);
                if (info != null) saves.Add(info);
                else firstError ??= error;
            }
        }
        catch (Exception ex)
        {
            return (saves, ex.Message);
        }

        saves.Sort((a, b) => b.Modified.CompareTo(a.Modified));
        return (saves, saves.Count == 0 ? firstError ?? "没有 .hg 存档文件" : null);
    }

    public static SaveInfo? ReadOne(string path, out string? error)
    {
        error = null;

        try
        {
            var fileInfo = new FileInfo(path);
            var text = ReadText(path, 128 * 1024);
            if (string.IsNullOrEmpty(text))
            {
                error = "文件为空或格式不对";
                return null;
            }

            var versionKey = Regex.Escape(SaveKeyMap.KeyOf("Version") ?? "F2P");
            var nameKey = Regex.Escape(SaveKeyMap.KeyOf("SaveName") ?? "Pk4");
            var playKey = Regex.Escape(SaveKeyMap.KeyOf("TotalPlayTime") ?? "Lg8");
            var platformKey = Regex.Escape(SaveKeyMap.KeyOf("Platform") ?? "8>q");
            var modeKey = Regex.Escape(SaveKeyMap.KeyOf("GameMode") ?? "idA");

            var version = Regex.Match(text, "\"" + versionKey + "\"\\s*:\\s*(\\d+)");
            var saveName = Regex.Match(text, "\"" + nameKey + "\"\\s*:\\s*\"([^\"]*)\"");
            var play = Regex.Match(text, "\"" + playKey + "\"\\s*:\\s*(\\d+)");
            var platform = Regex.Match(text, "\"" + platformKey + "\"\\s*:\\s*\"([^\"]*)\"");
            var mode = Regex.Match(text, "\"" + modeKey + "\"\\s*:\\s*(\"[^\"]*\"|\\d+)");

            var playSeconds = 0;
            if (play.Success) int.TryParse(play.Groups[1].Value, out playSeconds);

            return new SaveInfo
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                SaveVersion = version.Success && int.TryParse(version.Groups[1].Value, out var v) ? v : 0,
                SaveName = saveName.Success ? saveName.Groups[1].Value : "",
                Platform = platform.Success ? platform.Groups[1].Value : "",
                GameMode = mode.Success ? mode.Groups[1].Value.Trim('"') : "",
                PlaySeconds = playSeconds,
                Modified = fileInfo.LastWriteTime,
                Size = fileInfo.Length
            };
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>读出前 maxChars 个字符的存档 JSON。</summary>
    public static string ReadText(string path, int maxChars)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return ReadText(stream, maxChars);
    }

    private static string ReadText(Stream stream, int maxChars)
    {
        var builder = new StringBuilder(Math.Min(maxChars, 128 * 1024));
        var header = new byte[16];
        var compressedRead = 0;

        while (builder.Length < maxChars && compressedRead < 8 * 1024 * 1024)
        {
            if (!ReadExact(stream, header, 16)) break;

            var magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
            if (magic != Magic) break;

            var compressedSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
            var uncompressedSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));

            if (compressedSize <= 0 || compressedSize > MaxCompressedBlock) break;
            if (uncompressedSize <= 0 || uncompressedSize > MaxDecompressedBlock) break;

            var compressed = new byte[compressedSize];
            if (!ReadExact(stream, compressed, compressedSize)) break;
            compressedRead += compressedSize;

            var (buffer, length) = DecompressBlock(compressed, compressedSize, uncompressedSize);
            if (length <= 0) break;

            builder.Append(Encoding.UTF8.GetString(buffer, 0, length));
        }

        return builder.ToString();
    }

    private static bool ReadExact(Stream stream, byte[] buffer, int count)
    {
        var read = 0;
        while (read < count)
        {
            var n = stream.Read(buffer, read, count - read);
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }

    /// <summary>解一个 LZ4 块到指定长度。</summary>
    private static (byte[] Buffer, int Length) DecompressBlock(byte[] source, int sourceLength, int outputSize)
    {
        var output = new byte[outputSize];
        var input = 0;
        var written = 0;

        while (input < sourceLength && written < outputSize)
        {
            var token = source[input++];

            var literals = token >> 4;
            if (literals == 15)
            {
                while (input < sourceLength)
                {
                    var b = source[input++];
                    literals += b;
                    if (b != 255) break;
                }
            }

            if (literals > 0)
            {
                var take = Math.Min(literals, outputSize - written);
                take = Math.Min(take, sourceLength - input);
                Buffer.BlockCopy(source, input, output, written, take);
                input += take;
                written += take;
                if (take < literals) break;
            }

            if (input + 2 > sourceLength) break;

            var offset = source[input] | (source[input + 1] << 8);
            input += 2;
            if (offset == 0) break;

            var matchLength = token & 0xF;
            if (matchLength == 15)
            {
                while (input < sourceLength)
                {
                    var b = source[input++];
                    matchLength += b;
                    if (b != 255) break;
                }
            }
            matchLength += 4;

            var sourceIndex = written - offset;
            if (sourceIndex < 0) break;

            for (var i = 0; i < matchLength && written < outputSize; i++)
                output[written++] = output[sourceIndex + i];
        }

        return (output, written);
    }

    /// <summary>从存档名推断游戏版本（backup.steam.*.560.zip → 5.60）。</summary>
    public static string InferGameVersion(string? saveRoot)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(saveRoot) || !Directory.Exists(saveRoot)) return "";

            var versions = new List<double>();
            foreach (var file in Directory.EnumerateFiles(saveRoot, "backup.*.zip", SearchOption.AllDirectories))
            {
                var match = Regex.Match(Path.GetFileName(file), @"\.(\d{3,4})\.zip$", RegexOptions.IgnoreCase);
                if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Number,
                        CultureInfo.InvariantCulture, out var raw))
                    versions.Add(raw / 100.0);
            }

            return versions.Count == 0 ? "" : versions.Max().ToString("0.00", CultureInfo.InvariantCulture);
        }
        catch
        {
            return "";
        }
    }
}

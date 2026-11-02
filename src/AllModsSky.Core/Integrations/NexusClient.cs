using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AllModsSky.Core;

public sealed record NexusModInfo(
    string ModId,
    string Name,
    string Author,
    string Version,
    string Summary,
    string Updated,
    string PageUrl,
    string CoverUrl,
    string CategoryId);

public sealed record NexusFileInfo(
    string FileId,
    string Name,
    string Version,
    string SizeKb,
    string Category);

public sealed record NexusCategoryInfo(string Id, string Name);

/// <summary>Nexus Mods API v1 客户端（需要个人 API Key）。</summary>
public sealed class NexusClient : IDisposable
{
    public const string SiteBase = "https://www.nexusmods.com/nomanssky";
    private const string ApiBase = "https://api.nexusmods.com/v1/games/nomanssky";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public int TotalMods { get; private set; }

    public NexusClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        _ownsHttp = http == null;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(Http.UserAgent);
    }

    public async Task<(IReadOnlyList<NexusModInfo> Mods, string? Error)> GetModsAsync(
        string sort, string? apiKey, CancellationToken cancellation = default)
    {
        var key = apiKey?.Trim() ?? "";
        if (key.Length == 0) return (Array.Empty<NexusModInfo>(), "需要先在设置里填入 Nexus API Key");

        var endpoint = sort switch
        {
            "latest" => $"{ApiBase}/mods/latest_added.json",
            "updated" => $"{ApiBase}/mods/latest_updated.json",
            _ => $"{ApiBase}/mods/trending.json"
        };

        try
        {
            var json = await GetJsonAsync(endpoint, key, cancellation);
            using var document = JsonDocument.Parse(json);
            var list = new List<NexusModInfo>();

            foreach (var item in document.RootElement.EnumerateArray())
            {
                var id = Text(item, "mod_id");
                if (string.IsNullOrWhiteSpace(id)) continue;

                list.Add(new NexusModInfo(
                    id,
                    Text(item, "name"),
                    Text(item, "author"),
                    Text(item, "version"),
                    Text(item, "summary"),
                    Text(item, "updated_time"),
                    $"{SiteBase}/mods/{id}",
                    Text(item, "picture_url"),
                    Text(item, "category_id")));
            }

            return (list, null);
        }
        catch (Exception ex)
        {
            return (Array.Empty<NexusModInfo>(), Friendly(ex));
        }
    }

    /// <summary>三种排序合并去重，供分类/关键词筛选使用。</summary>
    public async Task<(IReadOnlyList<NexusModInfo> Mods, string? Error)> GetAllAsync(
        string? apiKey, CancellationToken cancellation = default)
    {
        var merged = new List<NexusModInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? firstError = null;

        foreach (var sort in new[] { "trending", "latest", "updated" })
        {
            var (mods, error) = await GetModsAsync(sort, apiKey, cancellation);
            if (error != null) firstError ??= error;
            foreach (var mod in mods)
                if (seen.Add(mod.ModId)) merged.Add(mod);
        }

        return (merged, merged.Count == 0 ? firstError ?? "接口没有返回数据" : null);
    }

    public async Task<(NexusModInfo? Mod, string? Error)> GetModAsync(
        string modId, string? apiKey, CancellationToken cancellation = default)
    {
        var key = apiKey?.Trim() ?? "";
        if (key.Length == 0) return (null, "需要先在设置里填入 Nexus API Key");

        try
        {
            var json = await GetJsonAsync($"{ApiBase}/mods/{modId}.json", key, cancellation);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var id = Text(root, "mod_id");
            if (string.IsNullOrWhiteSpace(id)) id = modId;

            return (new NexusModInfo(
                id,
                Text(root, "name"),
                Text(root, "author"),
                Text(root, "version"),
                Text(root, "summary"),
                Text(root, "updated_time"),
                $"{SiteBase}/mods/{id}",
                Text(root, "picture_url"),
                Text(root, "category_id")), null);
        }
        catch (Exception ex)
        {
            return (null, Friendly(ex));
        }
    }

    public async Task<(IReadOnlyList<NexusCategoryInfo> Categories, string? Error)> GetCategoriesAsync(
        string? apiKey, CancellationToken cancellation = default)
    {
        var key = apiKey?.Trim() ?? "";
        if (key.Length == 0) return (Array.Empty<NexusCategoryInfo>(), "需要 API Key");

        try
        {
            var json = await GetJsonAsync($"{ApiBase}.json", key, cancellation);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var list = new List<NexusCategoryInfo>();

            if (root.TryGetProperty("mods", out var total) && total.TryGetInt32(out var totalMods))
                TotalMods = totalMods;

            if (root.TryGetProperty("categories", out var categories) &&
                categories.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in categories.EnumerateArray())
                {
                    var id = Text(item, "category_id");
                    var name = Text(item, "name");
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;
                    if (id == "1") continue; // 游戏根分类
                    list.Add(new NexusCategoryInfo(id, name));
                }
            }

            return (list, null);
        }
        catch (Exception ex)
        {
            return (Array.Empty<NexusCategoryInfo>(), Friendly(ex));
        }
    }

    public async Task<(IReadOnlyList<NexusFileInfo> Files, string? Error)> GetFilesAsync(
        string modId, string? apiKey, CancellationToken cancellation = default)
    {
        var key = apiKey?.Trim() ?? "";
        if (key.Length == 0) return (Array.Empty<NexusFileInfo>(), "需要 API Key");

        try
        {
            var json = await GetJsonAsync($"{ApiBase}/mods/{modId}/files.json", key, cancellation);
            using var document = JsonDocument.Parse(json);
            var list = new List<NexusFileInfo>();

            if (document.RootElement.TryGetProperty("files", out var files) &&
                files.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in files.EnumerateArray())
                {
                    list.Add(new NexusFileInfo(
                        Text(item, "file_id"),
                        Text(item, "name"),
                        Text(item, "version"),
                        Text(item, "size_kb"),
                        Text(item, "category_name")));
                }
            }

            return (list, null);
        }
        catch (Exception ex)
        {
            return (Array.Empty<NexusFileInfo>(), Friendly(ex));
        }
    }

    private async Task<string> GetJsonAsync(string url, string apiKey, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(Describe(response.StatusCode));

        return await response.Content.ReadAsStringAsync(cancellation);
    }

    private static string Describe(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Nexus API Key 无效或已过期",
        HttpStatusCode.Forbidden => "没有权限访问该接口（检查 API Key）",
        HttpStatusCode.TooManyRequests => "请求太频繁，稍后再试",
        HttpStatusCode.NotFound => "模组不存在或已下架",
        _ => $"Nexus 返回 {(int)status} {status}"
    };

    private static string Friendly(Exception ex) => ex switch
    {
        TaskCanceledException => "请求超时（网络不通或 Nexus 无响应）",
        HttpRequestException http => http.Message,
        _ => ex.Message
    };

    private static string Text(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => ""
        };
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}

/// <summary>统一的浏览器 UA。</summary>
public static class Http
{
    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";
}

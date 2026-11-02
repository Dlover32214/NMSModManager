using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace AllModsSky.Core;

public sealed record OogcResource(
    string Fid,
    string Title,
    string Category,
    string Subtitle,
    string CoverUrl,
    string Url);

public sealed class OogcPage
{
    public List<OogcResource> Items { get; } = new();
    public int MaxPage { get; set; } = 1;
}

public sealed record OogcDetail(
    string Title,
    string Author,
    string DateText,
    string Downloads,
    string Size,
    string Format,
    string Description,
    string Attachments,
    string PageUrl,
    string CoverUrl);

/// <summary>oogc.cc 资源中心（keke_down 插件）的抓取客户端。</summary>
public sealed class OogcClient : IDisposable
{
    public const string BaseUrl = "https://oogc.cc";
    public const string GameId = "116";
    public const string ModCategory = "3";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public OogcClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient(
            new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _ownsHttp = http == null;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(Http.UserAgent);
    }

    public static string ListUrl(int page, string category = ModCategory, string sort = "latest",
        string cost = "", string query = "")
    {
        var url = $"{BaseUrl}/zy/game-{GameId}?resource_cate={Uri.EscapeDataString(category)}&resource_sort={Uri.EscapeDataString(sort)}";
        if (!string.IsNullOrEmpty(cost)) url += "&resource_cost=" + Uri.EscapeDataString(cost);
        if (!string.IsNullOrEmpty(query))
            url += "&resource_q=" + Uri.EscapeDataString(query) + "&searchsubmit=1";
        if (page > 1) url += "&content_page=" + page;
        return url;
    }

    public static string ViewUrl(string fid) => $"{BaseUrl}/plugin.php?id=keke_down&ac=view&fid={fid}";

    public async Task<(OogcPage? Page, string? Error)> ListAsync(
        int page, string category = ModCategory, string sort = "latest", string cost = "",
        string query = "", CancellationToken cancellation = default)
    {
        var result = new OogcPage();

        try
        {
            var html = await GetTextAsync(ListUrl(page, category, sort, cost, query),
                $"{BaseUrl}/zy/game-{GameId}", cancellation);

            result.MaxPage = Regex.Matches(html, "content_page=(\\d+)")
                .Select(m => int.TryParse(m.Groups[1].Value, out var value) ? value : 1)
                .DefaultIfEmpty(1)
                .Max();

            var found = new Dictionary<string, OogcResource>(StringComparer.Ordinal);

            foreach (Match match in Regex.Matches(html,
                         "<a[^>]*href=\"[^\"]*keke_down[^\"]*ac=view[^\"]*fid=(\\d+)[^\"]*\"[^>]*aria-label=\"([^\"]+)\"[^>]*>\\s*<img[^>]*src=\"([^\"]+)\"",
                         RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                var fid = match.Groups[1].Value;
                var title = WebUtility.HtmlDecode(match.Groups[2].Value).Trim();
                var cover = WebUtility.HtmlDecode(match.Groups[3].Value);

                if (!cover.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    cover = BaseUrl + "/" + cover.TrimStart('/');

                if (title.Length < 2 || found.ContainsKey(fid)) continue;

                var tail = html.Substring(match.Index, Math.Min(900, html.Length - match.Index));
                var label = Regex.Match(tail, "oogcc-resource-labels\" title=\"([^\"]+)\"");
                var categoryName = "";
                if (label.Success)
                {
                    var parts = label.Groups[1].Value.Split('/');
                    if (parts.Length >= 3) categoryName = parts[1].Trim() + " · " + parts[2].Trim();
                }

                found[fid] = new OogcResource(
                    fid,
                    title,
                    categoryName,
                    string.IsNullOrEmpty(categoryName) ? "Mod · ID " + fid : categoryName,
                    cover,
                    ViewUrl(fid));
            }

            result.Items.AddRange(found.Values);
            return (result, null);
        }
        catch (Exception ex)
        {
            return (null, Friendly(ex));
        }
    }

    public async Task<(OogcDetail? Detail, string? Error)> GetDetailAsync(
        string fid, CancellationToken cancellation = default)
    {
        var pageUrl = ViewUrl(fid);

        try
        {
            var html = await GetTextAsync(pageUrl, $"{BaseUrl}/zy/game-{GameId}", cancellation);

            var title = "";
            var h1 = Regex.Match(html, "<h1[^>]*>(.*?)</h1>", RegexOptions.Singleline);
            if (h1.Success) title = CleanHtml(h1.Groups[1].Value);

            var author = "";
            var name = Regex.Match(html, "class=\"username\"[^>]*>([^<]+)<");
            if (!name.Success)
                name = Regex.Match(html, "home\\.php\\?mod=space&(?:amp;)?uid=\\d+[^\"]*\"[^>]*>([^<]{2,30})<");
            if (name.Success) author = CleanHtml(name.Groups[1].Value);

            var date = Pick(html, "更新：\\s*([0-9]{4}-[0-9]{2}-[0-9]{2})");
            var downloads = Pick(html, "下载：\\s*([0-9,]+)\\s*次");
            var size = Pick(html, "大小：\\s*([0-9.]+\\s*[KMG]B)");
            var format = Pick(html, "格式：\\s*([A-Za-z0-9]+)");

            var description = "";
            var desc = Regex.Match(html, "id=\"kd-panel-intro\"[^>]*>(.*?)</div>\\s*<div role=\"tabpanel\"",
                RegexOptions.Singleline);
            if (!desc.Success)
                desc = Regex.Match(html, "id=\"kd-panel-intro\"[^>]*>(.*?)</div>", RegexOptions.Singleline);
            if (desc.Success) description = CleanHtml(desc.Groups[1].Value);

            var attachments = Regex.Matches(html, "<span class=\"file-name\">(.*?)</span>", RegexOptions.Singleline)
                .Select(m => CleanHtml(m.Groups[1].Value))
                .Where(s => s.Length > 0)
                .ToList();

            var cover = "";
            var coverMatch = Regex.Match(html, "<img[^>]*class=\"[^\"]*kd-cover[^\"]*\"[^>]*src=\"([^\"]+)\"",
                RegexOptions.IgnoreCase);
            if (coverMatch.Success) cover = WebUtility.HtmlDecode(coverMatch.Groups[1].Value);
            if (string.IsNullOrEmpty(cover))
            {
                coverMatch = Regex.Match(html, "<meta[^>]*property=\"og:image\"[^>]*content=\"([^\"]+)\"",
                    RegexOptions.IgnoreCase);
                if (coverMatch.Success) cover = WebUtility.HtmlDecode(coverMatch.Groups[1].Value);
            }

            return (new OogcDetail(
                title,
                author,
                date,
                downloads,
                size,
                format,
                description,
                attachments.Count > 0 ? string.Join("\n", attachments) : "（页面上没有列出附件）",
                pageUrl,
                cover), null);
        }
        catch (Exception ex)
        {
            return (null, Friendly(ex));
        }
    }

    /// <summary>尝试直接下载附件；资源中心多数附件要求登录，失败时返回提示。</summary>
    public async Task<(string? Path, string? Error)> DownloadAsync(
        string fid, CancellationToken cancellation = default)
    {
        AppPaths.Ensure();
        var directory = AppPaths.DownloadDir;
        string? lastError = null;
        var referer = ViewUrl(fid);

        foreach (var url in new[]
                 {
                     $"{BaseUrl}/plugin.php?id=keke_down&ac=download&fid={fid}",
                     $"{BaseUrl}/plugin.php?id=keke_down&ac=down&fid={fid}"
                 })
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Referrer = new Uri(referer);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    cancellation);

                response.EnsureSuccessStatusCode();

                var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                {
                    lastError = "该资源需要登录 oogc.cc";
                    continue;
                }

                var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                               ?? response.Content.Headers.ContentDisposition?.FileName;
                fileName = (fileName ?? "oogc_" + fid + ".zip").Trim('"');
                if (fileName.StartsWith("UTF-8''", StringComparison.OrdinalIgnoreCase))
                    fileName = Uri.UnescapeDataString(fileName[7..]);

                fileName = FileOps.SafeFileName(fileName, 120);
                var path = Path.Combine(directory, fileName);
                if (File.Exists(path))
                {
                    var name = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    path = Path.Combine(directory, $"{name}_{DateTime.Now:HHmmss}{ext}");
                }

                await using (var source = await response.Content.ReadAsStreamAsync(cancellation))
                await using (var target = File.Create(path))
                {
                    await source.CopyToAsync(target, cancellation);
                }

                var length = new FileInfo(path).Length;
                if (length < 512)
                {
                    FileOps.TryDeleteFile(path);
                    lastError = "下载到的文件太小，可能被要求登录";
                    continue;
                }

                return (path, null);
            }
            catch (Exception ex)
            {
                lastError = Friendly(ex);
            }
        }

        return (null, lastError ?? "无法直接下载（资源需要登录）");
    }

    private async Task<string> GetTextAsync(string url, string referer, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri(referer);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellation);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellation);
        var charset = response.Content.Headers.ContentType?.CharSet;
        return DetectEncoding(bytes, charset).GetString(bytes);
    }

    private static Encoding DetectEncoding(byte[] bytes, string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { return Encoding.GetEncoding(charset.Trim('"', '\'')); }
            catch { }
        }

        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(2048, bytes.Length)).ToLowerInvariant();
        if (head.Contains("charset=gbk") || head.Contains("charset=gb2312")) return Encoding.GetEncoding("GB18030");
        return Encoding.UTF8;
    }

    private static string Pick(string html, string pattern)
    {
        var match = Regex.Match(html, pattern);
        return match.Success ? CleanHtml(match.Groups[1].Value) : "";
    }

    private static string CleanHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var text = Regex.Replace(html, "<[^>]+>", "");
        text = WebUtility.HtmlDecode(text);
        return Regex.Replace(text, "\\s+", " ").Trim();
    }

    private static string Friendly(Exception ex) => ex switch
    {
        TaskCanceledException => "请求超时（oogc.cc 无响应或网络不通）",
        HttpRequestException http => http.Message,
        _ => ex.Message
    };

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}

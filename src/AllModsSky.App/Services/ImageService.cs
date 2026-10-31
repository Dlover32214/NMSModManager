using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AllModsSky.Core;

namespace AllModsSky.App.Services;

/// <summary>封面图下载 + 解码 + 缓存（后台线程解码后冻结，可直接给界面用）。</summary>
public sealed class ImageService : IDisposable
{
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, Task<ImageSource?>> _cache = new();

    public ImageService()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(Http.UserAgent);
    }

    public Task<ImageSource?> LoadAsync(string? url, int width = 240, string? referer = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return Task.FromResult<ImageSource?>(null);

        var requestUrl = Normalize(url, width);
        return _cache.GetOrAdd(requestUrl, _ => LoadCoreAsync(requestUrl, width, referer));
    }

    private static string Normalize(string url, int width)
    {
        // oogc 的封面常是 webp，借腾讯云 COS 的图片处理转成 jpg 缩略图
        if (url.Contains("myqcloud.com", StringComparison.OrdinalIgnoreCase) && !url.Contains('?'))
            return url + $"?imageMogr2/thumbnail/{width}x/format/jpg";
        return url;
    }

    private async Task<ImageSource?> LoadCoreAsync(string url, int width, string? referer)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
                request.Headers.Referrer = refererUri;

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead);
            if (!response.IsSuccessStatusCode) return null;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length < 128) return null;

            return await Task.Run(() => Decode(bytes, width));
        }
        catch (Exception ex)
        {
            AppLog.Warn($"封面加载失败 {url}：{ex.Message}");
            return null;
        }
    }

    private static ImageSource? Decode(byte[] bytes, int width)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            if (width > 0) bitmap.DecodePixelWidth = Math.Min(width, 1600);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}

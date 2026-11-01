using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AllModsSky.Core;

public sealed class UpdateAsset
{
    public string Name { get; init; } = "";
    public string Url { get; init; } = "";
    public long Size { get; init; }
    public string? Sha256 { get; init; }
}

public sealed class UpdateCheckResult
{
    public bool Ok { get; init; }
    public bool HasUpdate { get; init; }
    public string LatestVersion { get; init; } = "";
    public string Tag { get; init; } = "";
    public string Title { get; init; } = "";
    public string Notes { get; init; } = "";
    public string ReleaseUrl { get; init; } = "";
    public string PublishedText { get; init; } = "";
    public UpdateAsset? Asset { get; init; }
    public string? Error { get; init; }
}

public sealed class PreparedUpdate
{
    public string ExePath { get; init; } = "";
    public string Version { get; init; } = "";
}

/// <summary>GitHub Release 检查、下载、SHA256 校验与自替换更新。</summary>
public static class UpdateService
{
    public const string ProjectUrl = "https://github.com/Dlover32214/NMSModManager";
    public const string ReleasesUrl = ProjectUrl + "/releases";
    public const string LatestReleaseApi = "https://api.github.com/repos/Dlover32214/NMSModManager/releases/latest";

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AllModsSky-Updater/2.0.5");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static async Task<UpdateCheckResult> CheckLatestAsync(
        string currentVersion, CancellationToken cancellation = default)
    {
        try
        {
            using var response = await Http.GetAsync(LatestReleaseApi, cancellation);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return Fail("GitHub 仓库还没有发布 Release，可以先前往发布页查看。");

            if (!response.IsSuccessStatusCode)
                return Fail($"GitHub 返回 {(int)response.StatusCode} {response.ReasonPhrase}。");

            var json = await response.Content.ReadAsStringAsync(cancellation);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var tag = Text(root, "tag_name");
            var latest = NormalizeVersion(tag);
            var title = Text(root, "name");
            if (string.IsNullOrWhiteSpace(title)) title = tag;

            var releaseUrl = Text(root, "html_url");
            if (string.IsNullOrWhiteSpace(releaseUrl)) releaseUrl = ReleasesUrl;

            return new UpdateCheckResult
            {
                Ok = true,
                HasUpdate = CompareVersions(latest, currentVersion) > 0,
                LatestVersion = latest,
                Tag = tag,
                Title = title,
                Notes = Text(root, "body"),
                ReleaseUrl = releaseUrl,
                PublishedText = FormatPublished(Text(root, "published_at")),
                Asset = PickAsset(root)
            };
        }
        catch (OperationCanceledException)
        {
            return Fail("检查更新超时（25 秒），请检查网络后重试。");
        }
        catch (Exception ex)
        {
            return Fail("检查更新失败：" + ex.Message);
        }
    }

    public static async Task<PreparedUpdate> DownloadAndPrepareAsync(
        UpdateCheckResult check, IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        var asset = check.Asset ?? throw new InvalidOperationException("这个 Release 没有可自动安装的 exe/zip 资产。");

        var dir = Path.Combine(AppPaths.CacheDir, "update", SafeVersion(check.LatestVersion));
        Directory.CreateDirectory(dir);

        var fileName = FileOps.SafeFileName(asset.Name, 120);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "update" + Path.GetExtension(asset.Name);

        var file = Path.Combine(dir, fileName);
        if (!File.Exists(file) || new FileInfo(file).Length == 0)
        {
            using var response = await Http.GetAsync(
                asset.Url, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? asset.Size;
            await using var source = await response.Content.ReadAsStreamAsync(cancellation);
            await using var target = new FileStream(
                file, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellation);
                received += read;
                if (total > 0) progress?.Report(Math.Clamp(received / (double)total, 0, 1));
            }

            progress?.Report(1);
        }

        if (!string.IsNullOrWhiteSpace(asset.Sha256))
        {
            using var stream = File.OpenRead(file);
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!actual.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("更新文件 SHA256 校验失败，已停止安装。");
        }

        var ext = Path.GetExtension(asset.Name).ToLowerInvariant();
        if (ext == ".exe")
            return new PreparedUpdate { ExePath = file, Version = check.LatestVersion };

        if (ext == ".zip")
        {
            var extract = Path.Combine(dir, "extract");
            if (Directory.Exists(extract)) Directory.Delete(extract, true);
            ZipFile.ExtractToDirectory(file, extract, true);

            var exe = Directory.EnumerateFiles(extract, "AllModsSky.exe", SearchOption.AllDirectories)
                          .FirstOrDefault()
                      ?? Directory.EnumerateFiles(extract, "*.exe", SearchOption.AllDirectories)
                          .FirstOrDefault();

            if (string.IsNullOrEmpty(exe))
                throw new InvalidOperationException("更新 zip 中没有找到 AllModsSky.exe。");

            return new PreparedUpdate { ExePath = exe, Version = check.LatestVersion };
        }

        throw new InvalidOperationException("Release 资产不是 exe 或 zip，无法自动安装。");
    }

    /// <summary>启动一个等待当前进程退出的 PowerShell 脚本，替换 exe 后重新启动。</summary>
    public static bool StartUpdater(string newExe, string currentExe, out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(newExe)) throw new FileNotFoundException("找不到新版本文件", newExe);
            if (!File.Exists(currentExe)) throw new FileNotFoundException("找不到当前程序", currentExe);

            var dir = Path.Combine(AppPaths.CacheDir, "update");
            Directory.CreateDirectory(dir);
            var script = Path.Combine(dir, "apply-" + Guid.NewGuid().ToString("N") + ".ps1");

            var content = $$"""
                $ErrorActionPreference = 'Stop'
                $new = {{PsQuote(newExe)}}
                $cur = {{PsQuote(currentExe)}}
                $waitPid = {{Environment.ProcessId}}
                try {
                    while (Get-Process -Id $waitPid -ErrorAction SilentlyContinue) {
                        Start-Sleep -Milliseconds 500
                    }
                    Unblock-File -LiteralPath $new -ErrorAction SilentlyContinue
                    Copy-Item -LiteralPath $new -Destination $cur -Force
                    Start-Process -FilePath $cur -WorkingDirectory (Split-Path -Parent $cur)
                    Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
                } catch {
                    Set-Content -LiteralPath (Join-Path (Split-Path -Parent $cur) 'update-error.log') -Value $_.Exception.Message -Encoding UTF8
                    exit 1
                }
                """;

            File.WriteAllText(script, content, new UTF8Encoding(true));

            var powershell = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(powershell)) powershell = "powershell.exe";

            var psi = new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = dir
            };

            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static UpdateCheckResult Fail(string error) => new()
    {
        Ok = false,
        Error = error,
        ReleaseUrl = ReleasesUrl
    };

    private static UpdateAsset? PickAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<UpdateAsset>();
        foreach (var asset in assets.EnumerateArray())
        {
            var name = Text(asset, "name");
            var url = Text(asset, "browser_download_url");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;

            long size = 0;
            if (asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsed))
                size = parsed;

            var digest = Text(asset, "digest");
            string? sha = null;
            if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                sha = digest[7..].Trim();

            list.Add(new UpdateAsset { Name = name, Url = url, Size = size, Sha256 = sha });
        }

        var exe = list.FirstOrDefault(a =>
                      a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                      a.Name.Contains("AllModsSky", StringComparison.OrdinalIgnoreCase))
                  ?? list.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        if (exe != null) return exe;

        return list.FirstOrDefault(a =>
                   a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                   (a.Name.Contains("AllModsSky", StringComparison.OrdinalIgnoreCase) ||
                    a.Name.Contains("NMSModManager", StringComparison.OrdinalIgnoreCase)))
               ?? list.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeVersion(string? tag)
    {
        var text = (tag ?? "").Trim().TrimStart('v', 'V');
        var match = Regex.Match(text, @"^\d+(?:\.\d+){0,3}");
        return match.Success ? match.Value : text;
    }

    private static int CompareVersions(string? left, string? right)
    {
        var a = Version.TryParse(NormalizeVersion(left), out var va) ? va : null;
        var b = Version.TryParse(NormalizeVersion(right), out var vb) ? vb : null;
        if (a == null || b == null)
            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        return a.CompareTo(b);
    }

    private static string SafeVersion(string version) =>
        Regex.Replace(string.IsNullOrWhiteSpace(version) ? "latest" : version, @"[^\w\.\-]", "_");

    private static string FormatPublished(string value)
    {
        if (DateTimeOffset.TryParse(value, out var date))
            return date.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        return value;
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string PsQuote(string value) => "'" + value.Replace("'", "''") + "'";
}

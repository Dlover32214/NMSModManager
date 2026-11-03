namespace AllModsSky.Core.Infrastructure;

/// <summary>
/// 程序版本信息的唯一运行时入口。
///
/// 版本号只在根目录 <c>Directory.Build.props</c> 的 <c>AppVersion</c> 里维护；
/// MSBuild 会把它写入程序集元数据，这里从当前程序集读取，避免代码里再出现版本字面量。
/// </summary>
public static class AppInfo
{
    /// <summary>三段式版本，例如 2.0.8。</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>带 v 前缀的展示文本，例如 v2.0.8。</summary>
    public static string DisplayVersion => "v" + Version;

    /// <summary>更新器请求 GitHub 时使用的 User-Agent。</summary>
    public static string UserAgent => "AllModsSky-Updater/" + Version;

    private static string ReadVersion()
    {
        var version = typeof(AppInfo).Assembly.GetName().Version;
        return version is null
            ? "0.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}

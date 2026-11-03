namespace AllModsSky.Core.Services;

/// <summary>
/// library.json 的读写入口。抽成接口是为了让加载/播种逻辑能在测试里跑，不碰真实文件。
/// </summary>
public interface ILibraryStore
{
    /// <summary>读取索引。文件不存在时返回 true 且 state 为 null；内容无法解析时返回 false。</summary>
    bool TryLoad(out LibraryState? state);

    /// <summary>把无法解析的 library.json 挪到备份路径，返回备份路径（挪不动时返回原路径）。</summary>
    string BackupCorrupt();

    /// <summary>旧版 NMSModManager 的数据；没有可迁移内容时返回 null。</summary>
    LibraryState? LoadLegacy();

    void Save(LibraryState state);
}

/// <summary>真实文件实现：数据根目录下的 library.json。</summary>
public sealed class FileLibraryStore : ILibraryStore
{
    public static FileLibraryStore Instance { get; } = new();

    public bool TryLoad(out LibraryState? state) =>
        JsonStore.TryLoad<LibraryState>(AppPaths.DataFile, out state, out _);

    public string BackupCorrupt()
    {
        var backup = NextBackupPath();
        try
        {
            File.Move(AppPaths.DataFile, backup);
            AppLog.Warn("library.json 无法解析，已备份到 " + backup);
            return backup;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "备份损坏的 library.json");
            return AppPaths.DataFile;
        }
    }

    public LibraryState? LoadLegacy()
    {
        var legacy = LegacyDataMigrator.Read();
        return legacy.Mods.Count > 0 ? new LibraryState { Mods = legacy.Mods } : null;
    }

    public void Save(LibraryState state)
    {
        AppPaths.Ensure();
        JsonStore.Save(AppPaths.DataFile, state);
    }

    /// <summary>损坏备份名：backup_library_20261102125300.json.bak，与 library.json 同目录。</summary>
    private static string NextBackupPath()
    {
        var dir = Path.GetDirectoryName(AppPaths.DataFile) ?? AppPaths.Root;
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var path = Path.Combine(dir, $"backup_library_{stamp}.json.bak");

        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(dir, $"backup_library_{stamp}_{n}.json.bak");

        return path;
    }
}

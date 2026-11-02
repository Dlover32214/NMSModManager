using AllModsSky.Core;

namespace AllModsSky.App.Services;

/// <summary>整个应用共享的状态与服务。</summary>
public sealed class Workspace : IDisposable
{
    public Workspace()
    {
        Settings = SettingsStore.Current;
    }

    public AppSettings Settings { get; private set; }
    public ModLibrary Library { get; } = new();
    public DeploymentService Deployment { get; } = new();
    public LoadOrderService LoadOrder { get; } = new();
    public ModStateService ModStates { get; } = new();
    public AmumssService Amumss { get; } = new();
    public NexusClient Nexus { get; } = new();
    public OogcClient Oogc { get; } = new();
    public ImageService Images { get; } = new();
    public GameInfo Game { get; private set; } = new();

    public event Action? LibraryChanged;
    public event Action? GameChanged;
    public event Action? SettingsChanged;

    public string ModsDir => Game.IsValid ? Game.ModsPath : "";
    public string ParkedDir => Game.IsValid ? Game.ParkedPath : "";

    public void ReloadSettings() => Settings = SettingsStore.Current;

    public void RefreshGame()
    {
        Game = GameLocator.Inspect(Settings.GamePath, Settings.GameVersion);
        GameChanged?.Invoke();
    }

    public bool SaveSettings()
    {
        var ok = SettingsStore.Save();
        if (ok) SettingsChanged?.Invoke();
        return ok;
    }

    public void NotifyLibraryChanged() => LibraryChanged?.Invoke();

    public void NotifyGameChanged() => GameChanged?.Invoke();

    /// <summary>找到模组当前所在的目录（MODS 优先，其次停用区）；pak 记录返回 null。</summary>
    public string? FindModFolder(ModRecord record)
    {
        if (!Game.IsValid || record.IsLegacyPak) return null;

        var key = record.EffectiveFolder;
        var live = Path.Combine(ModsDir, key);
        if (Directory.Exists(live)) return live;

        var parked = Path.Combine(ParkedDir, key);
        if (Directory.Exists(parked)) return parked;

        return null;
    }

    public void Dispose()
    {
        Nexus.Dispose();
        Oogc.Dispose();
        Images.Dispose();
    }
}

using System.Windows.Input;
using AllModsSky.App.Mvvm;
using AllModsSky.App.Services;
using AllModsSky.Core;

namespace AllModsSky.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject, IActivatable
{
    private readonly Workspace _workspace;
    private readonly IShellUi _ui;

    private bool _loading;
    private string _gamePath = "";
    private string _gameVersion = "";
    private string _nexusKey = "";
    private string _amumssPath = "";
    private bool _createDisableModsFile;
    private bool _syncEnabledFlags;
    private bool _isDirty;
    private string _saveState = "";
    private string _saveInfoText = "还没有读取存档";
    private string _amumssLine = "";
    private bool _amumssOk;
    private string _gameHint = "";
    private bool _gameValid;
    private string _modsPath = "";
    private string _parkedPath = "";
    private int _parkedCount;
    private bool _modsWritable;
    private string _parkedInfo = "";

    public SettingsViewModel(Workspace workspace, IShellUi ui)
    {
        _workspace = workspace;
        _ui = ui;

        SaveCommand = new RelayCommand(Save);
        DetectGameCommand = new RelayCommand(DetectGame);
        BrowseGameCommand = new RelayCommand(BrowseGame);
        AutoVersionCommand = new RelayCommand(AutoVersion);
        BrowseAmumssCommand = new RelayCommand(BrowseAmumss);
        OpenModsFolderCommand = new RelayCommand(() => Launcher.OpenFolder(ModsPath));
        OpenDataFolderCommand = new RelayCommand(() => Launcher.OpenFolder(AppPaths.Root));
        OpenLogsCommand = new RelayCommand(() => Launcher.OpenFolder(AppPaths.Root));
        ReadSavesCommand = new RelayCommand(ReadSaves);
        OpenNexusApiCommand = new RelayCommand(() =>
            Launcher.OpenUrl("https://www.nexusmods.com/users/myaccount?tab=api"));
        RestartAdminCommand = new RelayCommand(Launcher.RestartAsAdmin);
        ClearSaveInfoCommand = new RelayCommand(() => SaveInfoText = "还没有读取存档");

        LoadFromSettings();
        _workspace.GameChanged += RefreshDerived;
    }

    public ICommand SaveCommand { get; }
    public ICommand DetectGameCommand { get; }
    public ICommand BrowseGameCommand { get; }
    public ICommand AutoVersionCommand { get; }
    public ICommand BrowseAmumssCommand { get; }
    public ICommand OpenModsFolderCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand ReadSavesCommand { get; }
    public ICommand OpenNexusApiCommand { get; }
    public ICommand RestartAdminCommand { get; }
    public ICommand ClearSaveInfoCommand { get; }

    public string GamePath
    {
        get => _gamePath;
        set
        {
            if (!SetProperty(ref _gamePath, value)) return;
            MarkDirty();
            RefreshDerived();
        }
    }

    public string GameVersion
    {
        get => _gameVersion;
        set
        {
            if (!SetProperty(ref _gameVersion, value)) return;
            MarkDirty();
        }
    }

    public string NexusKey
    {
        get => _nexusKey;
        set
        {
            if (!SetProperty(ref _nexusKey, value)) return;
            MarkDirty();
        }
    }

    public string AmumssPath
    {
        get => _amumssPath;
        set
        {
            if (!SetProperty(ref _amumssPath, value)) return;
            MarkDirty();
            RefreshAmumss();
        }
    }

    public bool CreateDisableModsFile
    {
        get => _createDisableModsFile;
        set
        {
            if (!SetProperty(ref _createDisableModsFile, value)) return;
            MarkDirty();
        }
    }

    public bool SyncEnabledFlags
    {
        get => _syncEnabledFlags;
        set
        {
            if (!SetProperty(ref _syncEnabledFlags, value)) return;
            MarkDirty();
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public string SaveStateText
    {
        get => _saveState;
        private set => SetProperty(ref _saveState, value);
    }

    public string SaveInfoText
    {
        get => _saveInfoText;
        private set => SetProperty(ref _saveInfoText, value);
    }

    public bool GameValid
    {
        get => _gameValid;
        private set => SetProperty(ref _gameValid, value);
    }

    public string GameHint
    {
        get => _gameHint;
        private set => SetProperty(ref _gameHint, value);
    }

    public string ModsPath
    {
        get => _modsPath;
        private set => SetProperty(ref _modsPath, value);
    }

    public string ParkedPath
    {
        get => _parkedPath;
        private set => SetProperty(ref _parkedPath, value);
    }

    public int ParkedCount
    {
        get => _parkedCount;
        private set => SetProperty(ref _parkedCount, value);
    }

    public bool ModsWritable
    {
        get => _modsWritable;
        private set
        {
            if (SetProperty(ref _modsWritable, value)) Raise(nameof(ModsStatusText));
        }
    }

    public string ModsStatusText => ModsWritable ? "MODS 可写" : "MODS 不可写";

    public string ParkedInfo
    {
        get => _parkedInfo;
        private set => SetProperty(ref _parkedInfo, value);
    }

    public string AmumssLine
    {
        get => _amumssLine;
        private set => SetProperty(ref _amumssLine, value);
    }

    public bool AmumssOk
    {
        get => _amumssOk;
        private set => SetProperty(ref _amumssOk, value);
    }

    public string DataPath => AppPaths.Root;
    public string AppVersion => "2.0.6";

    public void LoadFromSettings()
    {
        _loading = true;
        var settings = _workspace.Settings;

        GamePath = settings.GamePath;
        GameVersion = settings.GameVersion;
        NexusKey = settings.NexusApiKey;
        AmumssPath = settings.AmumssPath;
        CreateDisableModsFile = settings.CreateDisableModsFile;
        SyncEnabledFlags = settings.SyncEnabledFlags;

        _loading = false;
        IsDirty = false;
        SaveStateText = "";
        RefreshDerived();
    }

    public Task OnActivatedAsync()
    {
        RefreshDerived();
        return Task.CompletedTask;
    }

    public void RefreshDerived()
    {
        GameValid = GameLocator.IsValid(GamePath);
        GameHint = GameValid
            ? "目录有效：" + Path.Combine(GamePath, "GAMEDATA")
            : "不是有效的无人深空目录（应包含 GAMEDATA\\PCBANKS 或 Binaries\\NMS.exe）";

        ModsPath = GameValid ? GameLocator.ModsDir(GamePath) : "";
        ParkedPath = GameValid ? GameLocator.ParkedDir(GamePath) : "";
        ParkedCount = Directory.Exists(ParkedPath) ? Directory.GetDirectories(ParkedPath).Length : 0;
        ParkedInfo = GameValid
            ? (ParkedCount > 0
                ? $"停用区里有 {ParkedCount} 个目录，开关模组时会在这里来回移动。"
                : "停用区为空。关闭的模组会移动到 GAMEDATA\\MODS_disabled。")
            : "";
        ModsWritable = GameValid && AppPaths.CanWrite(ModsPath);

        RefreshAmumss();
    }

    private void RefreshAmumss()
    {
        var info = _workspace.Amumss.Inspect(AmumssPath);
        AmumssOk = info.IsValid;
        AmumssLine = info.IsValid
            ? info.Path + (string.IsNullOrEmpty(info.VersionLine) ? "" : "　·　" + info.VersionLine)
            : "未检测到（目录里应有 BUILDMOD.bat 或 MBINCompiler.exe）";
    }

    private void MarkDirty()
    {
        if (_loading) return;
        IsDirty = true;
        SaveStateText = "有未保存的修改";
    }

    private void Save()
    {
        var settings = _workspace.Settings;
        settings.GamePath = GamePath.Trim();
        settings.GameVersion = GameVersion.Trim();
        settings.NexusApiKey = NexusKey.Trim();
        settings.AmumssPath = AmumssPath.Trim();
        settings.CreateDisableModsFile = CreateDisableModsFile;
        settings.SyncEnabledFlags = SyncEnabledFlags;

        if (!_workspace.SaveSettings())
        {
            SaveStateText = "保存失败：" + (SettingsStore.LastSaveError ?? "未知原因");
            _ui.Toast("设置保存失败", false);
            return;
        }

        IsDirty = false;
        SaveStateText = "已保存 " + DateTime.Now.ToString("HH:mm:ss");
        _ui.Toast("设置已保存", true);
        _ = _ui.RefreshAllAsync();
    }

    private void DetectGame()
    {
        var found = GameLocator.Detect(GamePath);
        if (string.IsNullOrEmpty(found))
        {
            _ui.Alert("没有找到游戏",
                "自动检测失败。请点「浏览」手动选择无人深空安装目录（包含 GAMEDATA 的那一层）。");
            return;
        }

        GamePath = found;
        GameVersion = GameLocator.DetectGameVersion(found, "");
        Save();
    }

    private void BrowseGame()
    {
        var folder = _ui.PickFolder("选择无人深空安装目录（含 GAMEDATA 的那一层）", GamePath);
        if (string.IsNullOrEmpty(folder)) return;

        GamePath = folder;
        if (string.IsNullOrWhiteSpace(GameVersion))
            GameVersion = GameLocator.DetectGameVersion(folder, "");

        if (!GameLocator.IsValid(folder))
            _ui.Alert("目录可能不对", "这个目录里没有 GAMEDATA\\PCBANKS 或 Binaries\\NMS.exe。\n仍然可以保存，但模组功能可能无法使用。");

        Save();
    }

    private void AutoVersion()
    {
        var version = GameLocator.DetectGameVersion(GamePath, "");
        if (string.IsNullOrEmpty(version))
        {
            _ui.Alert("读不到版本", "没能从存档备份推断版本。请在输入框里手动填写，例如 7.10。");
            return;
        }

        GameVersion = version;
        _ui.Toast("已从存档备份推断版本：" + version, true);
    }

    private void BrowseAmumss()
    {
        var folder = _ui.PickFolder("选择 AMUMSS 目录（里面有 BUILDMOD.bat）", AmumssPath);
        if (string.IsNullOrEmpty(folder)) return;

        if (!_workspace.Amumss.IsValid(folder))
        {
            _ui.Alert("目录不对", "这个目录里没有 BUILDMOD.bat 或 MBINCompiler.exe。");
            return;
        }

        AmumssPath = folder;
        Save();
    }

    private void ReadSaves()
    {
        var saveDir = GameLocator.FindSaveDir();
        if (string.IsNullOrEmpty(saveDir))
        {
            SaveInfoText = "没找到存档目录（%APPDATA%\\HelloGames\\NMS\\st_*）";
            return;
        }

        var (saves, error) = SaveGameReader.Scan(saveDir);
        if (saves.Count == 0)
        {
            SaveInfoText = "存档读取失败：" + (error ?? "没有 .hg 文件");
            return;
        }

        var newest = saves[0];
        var version = newest.SaveVersion > 0 ? newest.SaveVersion.ToString() : "未知";
        var extra = "";
        if (!string.IsNullOrEmpty(newest.SaveName)) extra += $" · 存档名「{newest.SaveName}」";
        if (!string.IsNullOrEmpty(newest.PlayText)) extra += " · 游玩 " + newest.PlayText;
        if (!string.IsNullOrEmpty(newest.Platform)) extra += " · " + newest.Platform;

        SaveInfoText = $"存档 Version {version}{extra} · {saves.Count} 个存档 · " +
                       $"最近保存 {newest.Modified:MM-dd HH:mm}（{newest.FileName}）";
        _ui.Toast("已读取存档信息", true);
    }
}

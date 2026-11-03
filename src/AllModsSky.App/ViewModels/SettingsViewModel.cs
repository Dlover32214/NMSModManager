using System.Windows.Input;
using AllModsSky.App.Mvvm;
using AllModsSky.App.Services;

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
    private string _language = L.SystemLanguage;
    private bool _isDirty;
    private string _saveState = "";
    private string _saveInfoText = L.T("还没有读取存档");
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
        ClearSaveInfoCommand = new RelayCommand(() => SaveInfoText = L.T("还没有读取存档"));

        Languages = new[]
        {
            new LanguageOption(L.SystemLanguage, L.T("跟随系统")),
            new LanguageOption(L.ChineseLanguage, "简体中文"),
            new LanguageOption(L.EnglishLanguage, "English")
        };

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
    public IReadOnlyList<LanguageOption> Languages { get; }

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

    public string Language
    {
        get => _language;
        set
        {
            if (!SetProperty(ref _language, value)) return;
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

    public string ModsStatusText => ModsWritable ? L.T("MODS 可写") : L.T("MODS 不可写");

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
    public string AppVersion => AppInfo.DisplayVersion;

    public void LoadFromSettings()
    {
        _loading = true;
        var settings = _workspace.Settings;

        GamePath = settings.GamePath;
        GameVersion = settings.GameVersion;
        NexusKey = settings.NexusApiKey;
        AmumssPath = settings.AmumssPath;
        CreateDisableModsFile = settings.CreateDisableModsFile;
        Language = settings.Language;

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
            ? L.F("目录有效：{0}", Path.Combine(GamePath, "GAMEDATA"))
            : L.T("不是有效的无人深空目录（应包含 GAMEDATA\\PCBANKS 或 Binaries\\NMS.exe）");

        ModsPath = GameValid ? GameLocator.ModsDir(GamePath) : "";
        ParkedPath = GameValid ? GameLocator.ParkedDir(GamePath) : "";
        ParkedCount = Directory.Exists(ParkedPath) ? Directory.GetDirectories(ParkedPath).Length : 0;
        ParkedInfo = GameValid
            ? (ParkedCount > 0
                ? L.F("停用区里有 {0} 个目录，开关模组时会在这里来回移动。", ParkedCount)
                : L.T("停用区为空。关闭的模组会移动到 GAMEDATA\\MODS_disabled。"))
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
            : L.T("未检测到（目录里应有 BUILDMOD.bat 或 MBINCompiler.exe）");
    }

    private void MarkDirty()
    {
        if (_loading) return;
        IsDirty = true;
        SaveStateText = L.T("有未保存的修改");
    }

    private void Save()
    {
        var settings = _workspace.Settings;
        var previousLanguage = settings.Language;
        settings.GamePath = GamePath.Trim();

        // 版本号会拼进 cmd 命令行，这里先归一化，让界面显示的就是实际生效的值。
        var normalizedVersion = AmumssService.NormalizeGameVersion(GameVersion);
        if (!string.Equals(normalizedVersion, GameVersion, StringComparison.Ordinal))
        {
            GameVersion = normalizedVersion;
            _ui.Toast(L.F("游戏版本号格式不对，已改为 {0}", normalizedVersion), false);
        }
        settings.GameVersion = normalizedVersion;

        settings.NexusApiKey = NexusKey.Trim();
        settings.AmumssPath = AmumssPath.Trim();
        settings.CreateDisableModsFile = CreateDisableModsFile;
        settings.Language = Language;

        if (!_workspace.SaveSettings())
        {
            SaveStateText = L.F("保存失败：{0}", SettingsStore.LastSaveError ?? L.T("未知原因"));
            _ui.Toast(L.T("设置保存失败"), false);
            return;
        }

        IsDirty = false;
        SaveStateText = L.F("已保存 {0}", DateTime.Now.ToString("HH:mm:ss"));

<<<<<<< HEAD
        if (!string.Equals(previousLanguage, Language, StringComparison.OrdinalIgnoreCase))
        {
            // 语言不用重启进程：App 会用新语言重建主窗口，并恢复到当前设置页。
            _ui.RebuildShell();
            return;
        }

        _ui.Toast("设置已保存", true);
=======
        var languageChanged = !string.Equals(previousLanguage, Language, StringComparison.OrdinalIgnoreCase);
        _ui.Toast(languageChanged ? "语言设置已保存，重启程序后生效" : "设置已保存", true);
>>>>>>> 6576be8bf42bc977aa5dec75e25c09d16bffd31c
        _ = _ui.RefreshAllAsync();
    }

    private void DetectGame()
    {
        var found = GameLocator.Detect(GamePath);
        if (string.IsNullOrEmpty(found))
        {
            _ui.Alert(L.T("没有找到游戏"),
                L.T("自动检测失败。请点「浏览」手动选择无人深空安装目录（包含 GAMEDATA 的那一层）。"));
            return;
        }

        GamePath = found;
        GameVersion = GameLocator.DetectGameVersion(found, "");
        Save();
    }

    private void BrowseGame()
    {
        var folder = _ui.PickFolder(L.T("选择无人深空安装目录（含 GAMEDATA 的那一层）"), GamePath);
        if (string.IsNullOrEmpty(folder)) return;

        GamePath = folder;
        if (string.IsNullOrWhiteSpace(GameVersion))
            GameVersion = GameLocator.DetectGameVersion(folder, "");

        if (!GameLocator.IsValid(folder))
            _ui.Alert(L.T("目录可能不对"), L.T("这个目录里没有 GAMEDATA\\PCBANKS 或 Binaries\\NMS.exe。\n仍然可以保存，但模组功能可能无法使用。"));

        Save();
    }

    private void AutoVersion()
    {
        var version = GameLocator.DetectGameVersion(GamePath, "");
        if (string.IsNullOrEmpty(version))
        {
            _ui.Alert(L.T("读不到版本"), L.T("没能从存档备份推断版本。请在输入框里手动填写，例如 7.10。"));
            return;
        }

        GameVersion = version;
        _ui.Toast(L.F("已从存档备份推断版本：{0}", version), true);
    }

    private void BrowseAmumss()
    {
        var folder = _ui.PickFolder(L.T("选择 AMUMSS 目录（里面有 BUILDMOD.bat）"), AmumssPath);
        if (string.IsNullOrEmpty(folder)) return;

        if (!_workspace.Amumss.IsValid(folder))
        {
            _ui.Alert(L.T("目录不对"), L.T("这个目录里没有 BUILDMOD.bat 或 MBINCompiler.exe。"));
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
            SaveInfoText = L.T("没找到存档目录（%APPDATA%\\HelloGames\\NMS\\st_*）");
            return;
        }

        var (saves, error) = SaveGameReader.Scan(saveDir);
        if (saves.Count == 0)
        {
            SaveInfoText = L.F("存档读取失败：{0}", error ?? L.T("没有 .hg 文件"));
            return;
        }

        var newest = saves[0];
        var version = newest.SaveVersion > 0 ? newest.SaveVersion.ToString() : L.T("未知");
        var extra = "";
        if (!string.IsNullOrEmpty(newest.SaveName)) extra += L.F(" · 存档名「{0}」", newest.SaveName);
        if (!string.IsNullOrEmpty(newest.PlayText)) extra += L.F(" · 游玩 {0}", newest.PlayText);
        if (!string.IsNullOrEmpty(newest.Platform)) extra += " · " + newest.Platform;

        SaveInfoText = L.F("存档 Version {0}{1} · {2} 个存档 · ", version, extra, saves.Count) +
                       L.F("最近保存 {0}（{1}）", newest.Modified.ToString("MM-dd HH:mm"), newest.FileName);
        _ui.Toast(L.T("已读取存档信息"), true);
    }
}

/// <summary>设置页里的语言选项。Code 用于持久化，Name 是当前界面语言下的显示名。</summary>
public sealed record LanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
}

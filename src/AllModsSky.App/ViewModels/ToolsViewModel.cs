using System.Collections.ObjectModel;
using System.Windows.Input;
using AllModsSky.App.Mvvm;
using AllModsSky.App.Services;
using AllModsSky.Core;

namespace AllModsSky.App.ViewModels;

public sealed class ConflictRowViewModel
{
    public string Path { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Mods { get; init; } = "";
    public string Winner { get; init; } = "";
    public string Properties { get; init; } = "";
    public bool HasProperties => !string.IsNullOrEmpty(Properties);
    public string WinnerText => string.IsNullOrEmpty(Winner) ? "" : "按当前顺序：" + Winner + " 会覆盖其他";
}

public sealed class LogRowViewModel
{
    public string File { get; init; } = "";
    public string Text { get; init; } = "";
    public string Display => File + "  ›  " + Text;
}

public sealed class SuspectRowViewModel
{
    public string Name { get; init; } = "";
    public string CountText { get; init; } = "";
    public string Preview { get; init; } = "";
}

public sealed class LoadOrderRowViewModel
{
    public string Name { get; init; } = "";
    public int Priority { get; init; }
    public bool Enabled { get; init; }
    public string PriorityText => Priority.ToString("00");
    public string StateText => Enabled ? "启用" : "停用";
}

public sealed class ModChoiceViewModel
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public ModRecord Record { get; init; } = null!;
    public string SubText => Record.IsLegacyPak ? "旧式 pak" : Record.SizeText;
}

public sealed class ToolsViewModel : ObservableObject, IActivatable
{
    private readonly Workspace _workspace;
    private readonly IShellUi _ui;

    private string _conflictSummary = "还没有扫描。";
    private bool _isScanningConflicts;

    private string _diagnosticsSummary = "还没有扫描。";
    private bool _isScanningLogs;
    private bool _hasLogs = true;

    private string _loadOrderSummary = "";
    private string _loadOrderError = "";
    private bool _isReadingOrder;

    private string _amumssPath = "";
    private string _amumssVersionLine = "未检测到 AMUMSS";
    private bool _amumssValid;
    private string _planSummary = "";
    private string _rebuildStatus = "";
    private bool _canCollect;
    private ModChoiceViewModel? _selectedMod;
    private RepairPlan? _plan;

    public ToolsViewModel(Workspace workspace, IShellUi ui)
    {
        _workspace = workspace;
        _ui = ui;

        ScanConflictsCommand = new AsyncRelayCommand(ScanConflictsAsync);
        ScanLogsCommand = new AsyncRelayCommand(ScanLogsAsync);
        ReadLoadOrderCommand = new AsyncRelayCommand(ReadLoadOrderAsync);
        ApplyLoadOrderCommand = new AsyncRelayCommand(ApplyLoadOrderAsync, () => LoadOrderRows.Count > 0);
        DetectAmumssCommand = new RelayCommand(DetectAmumss);
        BrowseAmumssCommand = new RelayCommand(BrowseAmumss);
        OpenAmumssCommand = new RelayCommand(() => Launcher.OpenFolder(AmumssPath));
        OpenAmumssScriptsCommand = new RelayCommand(() =>
            Launcher.OpenFolder(Path.Combine(AmumssPath, "ModScript")));
        OpenCreatedModsCommand = new RelayCommand(() =>
            Launcher.OpenFolder(Path.Combine(AmumssPath, "CreatedMODS")));
        OpenLogsFolderCommand = new RelayCommand(OpenLogsFolder);
        AnalyzeCommand = new RelayCommand(AnalyzeSelected);
        RebuildCommand = new AsyncRelayCommand(RebuildAsync);
        CollectCommand = new AsyncRelayCommand(CollectAsync);

        _workspace.LibraryChanged += RefreshModChoices;
        _workspace.GameChanged += () => Raise(nameof(HasGame));
        _workspace.SettingsChanged += UpdateAmumss;

        RefreshModChoices();
        UpdateAmumss();
    }

    public ObservableCollection<ConflictRowViewModel> Conflicts { get; } = new();
    public ObservableCollection<LogRowViewModel> ErrorRows { get; } = new();
    public ObservableCollection<SuspectRowViewModel> Suspects { get; } = new();
    public ObservableCollection<LoadOrderRowViewModel> LoadOrderRows { get; } = new();
    public ObservableCollection<ModChoiceViewModel> ModChoices { get; } = new();

    public ICommand ScanConflictsCommand { get; }
    public ICommand ScanLogsCommand { get; }
    public ICommand ReadLoadOrderCommand { get; }
    public ICommand ApplyLoadOrderCommand { get; }
    public ICommand DetectAmumssCommand { get; }
    public ICommand BrowseAmumssCommand { get; }
    public ICommand OpenAmumssCommand { get; }
    public ICommand OpenAmumssScriptsCommand { get; }
    public ICommand OpenCreatedModsCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }
    public ICommand AnalyzeCommand { get; }
    public ICommand RebuildCommand { get; }
    public ICommand CollectCommand { get; }

    public bool HasGame => _workspace.Game.IsValid;

    public string ConflictSummary
    {
        get => _conflictSummary;
        private set => SetProperty(ref _conflictSummary, value);
    }

    public bool IsScanningConflicts
    {
        get => _isScanningConflicts;
        private set => SetProperty(ref _isScanningConflicts, value);
    }

    public bool HasConflicts => Conflicts.Count > 0;

    public string DiagnosticsSummary
    {
        get => _diagnosticsSummary;
        private set => SetProperty(ref _diagnosticsSummary, value);
    }

    public bool IsScanningLogs
    {
        get => _isScanningLogs;
        private set => SetProperty(ref _isScanningLogs, value);
    }

    public bool HasLogs
    {
        get => _hasLogs;
        private set => SetProperty(ref _hasLogs, value);
    }

    public bool HasErrors => ErrorRows.Count > 0;
    public bool HasSuspects => Suspects.Count > 0;

    public string LoadOrderSummary
    {
        get => _loadOrderSummary;
        private set => SetProperty(ref _loadOrderSummary, value);
    }

    public string LoadOrderError
    {
        get => _loadOrderError;
        private set => SetProperty(ref _loadOrderError, value);
    }

    public bool IsReadingOrder
    {
        get => _isReadingOrder;
        private set => SetProperty(ref _isReadingOrder, value);
    }

    public bool HasLoadOrder => LoadOrderRows.Count > 0;

    public string AmumssPath
    {
        get => _amumssPath;
        private set => SetProperty(ref _amumssPath, value);
    }

    public string AmumssVersionLine
    {
        get => _amumssVersionLine;
        private set => SetProperty(ref _amumssVersionLine, value);
    }

    public bool AmumssValid
    {
        get => _amumssValid;
        private set => SetProperty(ref _amumssValid, value);
    }

    public string PlanSummary
    {
        get => _planSummary;
        private set => SetProperty(ref _planSummary, value);
    }

    public string RebuildStatus
    {
        get => _rebuildStatus;
        private set => SetProperty(ref _rebuildStatus, value);
    }

    public bool CanCollect
    {
        get => _canCollect;
        private set => SetProperty(ref _canCollect, value);
    }

    public ModChoiceViewModel? SelectedMod
    {
        get => _selectedMod;
        set
        {
            if (!SetProperty(ref _selectedMod, value)) return;
            _plan = null;
            PlanSummary = "";
            CanCollect = false;
        }
    }

    public bool HasMods => ModChoices.Count > 0;

    public Task OnActivatedAsync()
    {
        RefreshModChoices();
        UpdateAmumss();
        return Task.CompletedTask;
    }

    private void RefreshModChoices()
    {
        var previous = SelectedMod?.Id;
        ModChoices.Clear();

        foreach (var record in _workspace.Library.Mods)
            ModChoices.Add(new ModChoiceViewModel { Id = record.Id, Name = record.Name, Record = record });

        SelectedMod = ModChoices.FirstOrDefault(m => m.Id == previous) ?? ModChoices.FirstOrDefault();
        Raise(nameof(HasMods));
    }

    private async Task ScanConflictsAsync()
    {
        if (!TryGetGame(out var game)) return;

        IsScanningConflicts = true;
        Conflicts.Clear();
        ConflictSummary = "正在扫描…";

        try
        {
            var targets = _workspace.Library.Mods
                .Select((record, index) => new ConflictTarget
                {
                    Name = record.Name,
                    Folder = _workspace.FindModFolder(record) ?? "",
                    Enabled = record.Enabled,
                    Order = index
                })
                .Where(t => !string.IsNullOrEmpty(t.Folder))
                .ToList();

            if (targets.Count == 0)
            {
                ConflictSummary = "没有可扫描的模组目录。";
                return;
            }

            var result = await Task.Run(() => ConflictAnalyzer.Scan(targets));

            foreach (var conflict in result.Conflicts.Take(120))
            {
                Conflicts.Add(new ConflictRowViewModel
                {
                    Path = conflict.VanillaPath,
                    Kind = conflict.Kind,
                    Mods = conflict.ModNames,
                    Winner = conflict.WinnerName,
                    Properties = conflict.SharedProperties.Count > 0
                        ? "共同修改：" + string.Join(", ", conflict.SharedProperties)
                        : ""
                });
            }

            ConflictSummary = result.Summary;
            if (result.Conflicts.Count > 120) ConflictSummary += $"（只列出前 120 处）";
            Raise(nameof(HasConflicts));

            _ui.Toast(result.Conflicts.Count == 0 ? "没有发现路径重叠" : $"发现 {result.Conflicts.Count} 处重叠",
                result.Conflicts.Count == 0);
        }
        catch (Exception ex)
        {
            ConflictSummary = "扫描失败：" + ex.Message;
        }
        finally
        {
            IsScanningConflicts = false;
        }
    }

    private async Task ScanLogsAsync()
    {
        if (!TryGetGame(out var game)) return;

        IsScanningLogs = true;
        ErrorRows.Clear();
        Suspects.Clear();
        DiagnosticsSummary = "正在读取日志…";

        try
        {
            var mods = _workspace.Library.Mods.ToList();
            var result = await Task.Run(() => LogDiagnostics.Scan(game, mods));

            foreach (var error in result.Errors.Take(250))
                ErrorRows.Add(new LogRowViewModel { File = error.File, Text = error.Text });

            foreach (var pair in result.Suspects.OrderByDescending(p => p.Value.Count))
            {
                Suspects.Add(new SuspectRowViewModel
                {
                    Name = pair.Key,
                    CountText = $"命中 {pair.Value.Count} 次",
                    Preview = string.Join(Environment.NewLine, pair.Value.Take(3).Select(l => "· " + Trim(l.Text, 180)))
                });
            }

            HasLogs = result.LogFiles.Count > 0;
            DiagnosticsSummary = result.Note
                ?? $"{result.LogFiles.Count} 个日志 · {result.ScannedLines} 行 · " +
                   $"{result.Errors.Count} 条报错 · 命中 {result.Suspects.Count} 个模组";

            Raise(nameof(HasErrors), nameof(HasSuspects));
            _ui.Toast(result.Suspects.Count == 0 ? "日志里没有模组相关的报错" : $"命中 {result.Suspects.Count} 个模组", true);
        }
        catch (Exception ex)
        {
            DiagnosticsSummary = "读取日志失败：" + ex.Message;
        }
        finally
        {
            IsScanningLogs = false;
        }
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    private async Task ReadLoadOrderAsync()
    {
        if (!TryGetGame(out var game)) return;

        IsReadingOrder = true;
        try
        {
            var read = await Task.Run(() => _workspace.LoadOrder.Read(game));

            LoadOrderRows.Clear();
            LoadOrderError = read.Error ?? "";
            if (read.Ok)
            {
                foreach (var entry in read.Entries)
                {
                    LoadOrderRows.Add(new LoadOrderRowViewModel
                    {
                        Name = entry.Name,
                        Priority = entry.Priority,
                        Enabled = entry.Enabled
                    });
                }

                LoadOrderSummary = read.Entries.Count == 0
                    ? "文件存在，但还没有模组条目（进一次游戏让它扫描）。"
                    : $"{read.Entries.Count} 个条目 · " + read.FilePath;
            }
            else
            {
                LoadOrderSummary = "";
            }

            Raise(nameof(HasLoadOrder));
        }
        finally
        {
            IsReadingOrder = false;
        }
    }

    private async Task ApplyLoadOrderAsync()
    {
        if (!TryGetGame(out var game)) return;

        var confirm = _ui.Confirm("写入加载顺序",
            "会把当前模组列表的顺序写入 GCMODSETTINGS.MXML（原文件备份为 .bak）。\n\n继续吗？",
            "写入");
        if (confirm != ConfirmResult.Primary) return;

        var names = _workspace.Library.Mods
            .Select(m => LoadOrderService.ToXmlName(m.EffectiveFolder))
            .ToList();

        var result = await Task.Run(() => _workspace.LoadOrder.Apply(game, names));

        if (!result.Ok)
        {
            _ui.Alert("写入失败", result.Error ?? "没有匹配的模组条目");
            return;
        }

        _ui.Toast($"已写入 {result.Updated} 个模组" +
                  (result.Missing > 0 ? $"，{result.Missing} 个在 XML 里找不到" : ""), true);
        await ReadLoadOrderAsync();
    }

    private bool TryGetGame(out string game)
    {
        game = _workspace.Game.GamePath;
        if (_workspace.Game.IsValid) return true;
        _ui.Alert("需要游戏目录", "先在「设置」里指定无人深空的安装目录。");
        return false;
    }

    private void OpenLogsFolder()
    {
        var gamedata = Path.Combine(_workspace.Game.GamePath, "GAMEDATA");
        Launcher.OpenFolder(Directory.Exists(gamedata) ? gamedata : GameLocator.SaveRoot());
    }

    private void UpdateAmumss()
    {
        var configured = _workspace.Settings.AmumssPath;
        var path = _workspace.Amumss.IsValid(configured)
            ? configured
            : _workspace.Amumss.DetectPath(configured) ?? "";

        AmumssPath = path;

        var info = _workspace.Amumss.Inspect(path);
        AmumssValid = info.IsValid;
        AmumssVersionLine = info.IsValid
            ? (string.IsNullOrEmpty(info.VersionLine) ? "已检测到 AMUMSS（版本未知）" : info.VersionLine)
            : "未检测到 AMUMSS。把它解压到任意目录，然后点「指定目录…」。";
    }

    private void DetectAmumss()
    {
        var found = _workspace.Amumss.DetectPath(_workspace.Settings.AmumssPath);
        if (string.IsNullOrEmpty(found))
        {
            _ui.Alert("没找到 AMUMSS",
                "常见位置（各盘根目录的 AMUMSS）都没有找到。\n请点「指定目录…」选择包含 BUILDMOD.bat 的文件夹。");
            return;
        }

        _workspace.Settings.AmumssPath = found;
        _workspace.SaveSettings();
        UpdateAmumss();
        _ui.Toast("已检测到 AMUMSS", true);
    }

    private void BrowseAmumss()
    {
        var folder = _ui.PickFolder("选择 AMUMSS 目录（里面有 BUILDMOD.bat）", AmumssPath);
        if (string.IsNullOrEmpty(folder)) return;

        if (!_workspace.Amumss.IsValid(folder))
        {
            _ui.Alert("目录不对", "这个目录里没有 BUILDMOD.bat 或 MBINCompiler.exe，不像是 AMUMSS 根目录。");
            return;
        }

        _workspace.Settings.AmumssPath = folder;
        _workspace.SaveSettings();
        UpdateAmumss();
        _ui.Toast("已指定 AMUMSS 目录", true);
    }

    private void AnalyzeSelected()
    {
        if (SelectedMod == null)
        {
            _ui.Toast("先选择一个模组", false);
            return;
        }

        if (!_workspace.Game.IsValid)
        {
            _ui.Alert("需要游戏目录", "先在「设置」里指定游戏目录。");
            return;
        }

        var folder = _workspace.FindModFolder(SelectedMod.Record);
        if (string.IsNullOrEmpty(folder))
        {
            _ui.Alert("找不到模组目录", "这个模组的文件夹既不在 MODS 也不在 MODS_disabled 里。");
            return;
        }

        _plan = _workspace.Amumss.Analyze(folder, SelectedMod.Name, AmumssPath);
        PlanSummary = _plan.Summary;
        CanCollect = false;

        if (_plan.LuaFiles.Count == 0)
        {
            _ui.Alert("无法用 AMUMSS 重建",
                "这个模组里没有 .lua 源脚本，AMUMSS 无法重新生成。\n\n" +
                "只能去原发布页下载适用于当前版本的新版本，或手工修改 EXML。");
        }
    }

    private Task RebuildAsync()
    {
        if (_plan == null)
        {
            AnalyzeSelected();
            if (_plan == null) return Task.CompletedTask;
        }

        if (!_workspace.Amumss.IsValid(AmumssPath))
        {
            _ui.Alert("需要 AMUMSS", "先检测或指定 AMUMSS 目录。");
            return Task.CompletedTask;
        }

        if (_plan.LuaFiles.Count == 0)
        {
            _ui.Alert("没有源脚本", "这个模组没有 lua 源脚本，无法重建。");
            return Task.CompletedTask;
        }

        var confirm = _ui.Confirm("用 AMUMSS 重建",
            $"模组：{SelectedMod?.Name}\n{PlanSummary}\n\n" +
            "步骤：\n1. 把 lua 源脚本复制到 AMUMSS\\ModScript\n" +
            "2. 打开 BUILDMOD 控制台，等它跑完（可能需要几分钟，AMUMSS 若提问请自行回答）\n" +
            "3. 回到这里点「收录结果」（原目录会自动备份）\n\n现在开始吗？",
            "开始构建");

        if (confirm != ConfirmResult.Primary) return Task.CompletedTask;

        var copied = _workspace.Amumss.PrepareScripts(AmumssPath, _plan);
        if (copied == 0)
        {
            _ui.Alert("复制脚本失败", "检查 AMUMSS\\ModScript 是否可写。");
            return Task.CompletedTask;
        }

        var hasCompiler = _workspace.Amumss.FindCompiler(AmumssPath) != null;
        if (!hasCompiler)
        {
            var update = _ui.Confirm("缺少 MBINCompiler",
                "AMUMSS 里没有找到 MBINCompiler.exe。\n\n" +
                "这次会让 AMUMSS 自动下载（-AutoUpdateMBinCompiler Y，需要联网）。继续吗？",
                "继续");
            if (update != ConfirmResult.Primary) return Task.CompletedTask;
        }

        try
        {
            _workspace.Amumss.StartBuild(AmumssPath, _workspace.Settings.GameVersion, !hasCompiler);
            CanCollect = true;
            RebuildStatus = $"已复制 {copied} 个脚本，AMUMSS 已在新窗口运行。跑完后点「收录结果」。";
            _ui.Toast("AMUMSS 已启动", true);
        }
        catch (Exception ex)
        {
            _ui.Alert("启动 AMUMSS 失败", ex.Message);
        }

        return Task.CompletedTask;
    }

    private async Task CollectAsync()
    {
        if (_plan == null)
        {
            _ui.Alert("还没有构建计划", "先选择模组并点「分析」。");
            return;
        }

        var confirm = _ui.Confirm("收录构建结果",
            "确认 AMUMSS 已经跑完？\n\n会把 CreatedMODS / CreatedModPAKs 里的输出复制回模组目录，" +
            "原目录自动备份为 .bak_时间戳。",
            "收录结果");

        if (confirm != ConfirmResult.Primary) return;

        var result = _workspace.Amumss.Collect(AmumssPath, _plan);

        if (!result.Ok)
        {
            _ui.Alert("收录失败", result.Error ?? "没有找到匹配的输出");
            return;
        }

        if (SelectedMod != null)
        {
            SelectedMod.Record.Size = FileOps.DirSize(_plan.Folder);
            _workspace.Library.Save();
        }

        RebuildStatus = $"已收录 {result.Copied} 项输出" +
                        (string.IsNullOrEmpty(result.BackupPath) ? "" : $"，原目录备份在 {result.BackupPath}");
        CanCollect = false;

        _ui.Toast("已收录 AMUMSS 输出", true);
        await _ui.RefreshAllAsync();
    }
}

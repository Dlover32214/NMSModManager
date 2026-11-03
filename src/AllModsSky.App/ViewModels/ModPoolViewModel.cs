using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using AllModsSky.App.Behaviors;
using AllModsSky.App.Mvvm;
using AllModsSky.App.Services;

namespace AllModsSky.App.ViewModels;

/// <summary>模组库里的一行：池子里的模组 + 它是否已经在当前配置里。</summary>
public sealed class PoolItemViewModel : ObservableObject, ISelectableItem
{
    private bool _inProfile;
    private bool _isSelected;
    private string _profileName = "";

    public PoolItemViewModel(ModRecord record) => Record = record;

    public ModRecord Record { get; }
    public string Id => Record.Id;
    public string Name => Record.Name;
    public string MetaText => $"{Record.VersionText} · {Record.SizeText} · {L.T(Record.Source)}";
    public string FolderText => Record.EffectiveFolder;
    public bool IsLegacyPak => Record.IsLegacyPak;
    public string SizeText => Record.SizeText;

    /// <summary>批量选取时用。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool HasLink => !string.IsNullOrWhiteSpace(Record.Url);

    public string LinkTooltip => HasLink
        ? L.F("打开模组页面：{0}\n点右侧铅笔可以改", Record.Url)
        : L.T("给这个模组加一个页面链接");

    /// <summary>链接改过之后刷新按钮的提示文字。</summary>
    public void RefreshLink() => Raise(nameof(HasLink), nameof(LinkTooltip));

    /// <summary>当前配置的名字。状态文字里要带上它，不然「已在当前配置」等于没说。</summary>
    public string ProfileName
    {
        get => _profileName;
        set
        {
            if (SetProperty(ref _profileName, value)) Raise(nameof(StateText));
        }
    }

    public bool InProfile
    {
        get => _inProfile;
        set
        {
            if (!SetProperty(ref _inProfile, value)) return;
            Raise(nameof(StateText), nameof(AddText));
        }
    }

    public string StateText => _inProfile ? L.F("已在「{0}」", _profileName) : L.T("未加入");
    public string AddText => _inProfile ? L.T("已加入") : L.T("加入配置");
}

/// <summary>
/// 模组库页：整个池子的模组都在这儿（不管在不在当前配置里），
/// 导入、收编外部模组、往配置里加、从库里删，都在这一页做。
/// </summary>
public sealed class ModPoolViewModel : ObservableObject, IActivatable, IBulkSelectionSink
{
    private readonly Workspace _workspace;
    private readonly IShellUi _ui;

    private string _searchText = "";
    private bool _onlyUnused;
    private bool _showExternal;
    private bool _batchMode;

    public ModPoolViewModel(Workspace workspace, IShellUi ui)
    {
        _workspace = workspace;
        _ui = ui;

        Items = new ObservableCollection<PoolItemViewModel>();
        External = new ObservableCollection<ExternalModItemViewModel>();
        View = CollectionViewSource.GetDefaultView(Items);
        View.Filter = FilterItem;

        ImportFilesCommand = new RelayCommand(ImportFiles);
        ImportFolderCommand = new RelayCommand(ImportFolder);
        RefreshCommand = new AsyncRelayCommand(() => _ui.RefreshAllAsync());
        AddCommand = new AsyncRelayCommand(p => AddAsync(p as PoolItemViewModel));
        OpenFolderCommand = new RelayCommand(p => OpenFolder(p as PoolItemViewModel));
        DeleteCommand = new RelayCommand(p => Delete(p as PoolItemViewModel));
        ToggleUnusedCommand = new RelayCommand(() => OnlyUnused = !OnlyUnused);
        ToggleExternalPanelCommand = new AsyncRelayCommand(ToggleExternalPanelAsync);
        RefreshExternalCommand = new AsyncRelayCommand(RefreshExternalAsync);
        AdoptExternalCommand = new AsyncRelayCommand(p => AdoptAsync(p as ExternalModItemViewModel));
        AdoptAllExternalCommand = new AsyncRelayCommand(AdoptAllAsync);
        ToggleExternalItemCommand = new AsyncRelayCommand(p => ToggleExternalAsync(p as ExternalModItemViewModel));

        ToggleBatchCommand = new RelayCommand(() => BatchMode = !BatchMode);
        SelectAllCommand = new RelayCommand(ToggleSelectAll);
        ClearSelectionCommand = new RelayCommand(ExitBatch);
        BatchAddCommand = new AsyncRelayCommand(BatchAddAsync);
        BatchRemoveCommand = new AsyncRelayCommand(BatchRemoveAsync);
        LinkCommand = new RelayCommand(p => OpenLink(p as PoolItemViewModel));
        EditLinkCommand = new RelayCommand(p => EditLink(p as PoolItemViewModel));

        _workspace.LibraryChanged += Reload;
    }

    public ObservableCollection<PoolItemViewModel> Items { get; }
    public ICollectionView View { get; }
    public ObservableCollection<ExternalModItemViewModel> External { get; }

    public ICommand ImportFilesCommand { get; }
    public ICommand ImportFolderCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand AddCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ToggleUnusedCommand { get; }
    public ICommand ToggleExternalPanelCommand { get; }
    public ICommand RefreshExternalCommand { get; }
    public ICommand AdoptExternalCommand { get; }
    public ICommand AdoptAllExternalCommand { get; }
    public ICommand ToggleExternalItemCommand { get; }
    public ICommand ToggleBatchCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand BatchAddCommand { get; }
    public ICommand BatchRemoveCommand { get; }
    public ICommand LinkCommand { get; }
    public ICommand EditLinkCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            View.Refresh();
            Raise(nameof(HasVisibleMods), nameof(IsEmpty),
                nameof(AllSelected), nameof(SelectAllText), nameof(SelectAllTooltip));
        }
    }

    /// <summary>只看还没进当前配置的（挑模组时最常用）。</summary>
    public bool OnlyUnused
    {
        get => _onlyUnused;
        set
        {
            if (!SetProperty(ref _onlyUnused, value)) return;

            // 换筛选就退出批量，免得选中的被筛没了还挂着
            if (BatchMode) ExitBatch();

            View.Refresh();
            Raise(nameof(HasVisibleMods), nameof(IsEmpty), nameof(UnusedButtonText),
                nameof(AllSelected), nameof(SelectAllText), nameof(SelectAllTooltip));
        }
    }

    public bool ShowExternal
    {
        get => _showExternal;
        private set => SetProperty(ref _showExternal, value);
    }

    public string UnusedButtonText => _onlyUnused ? L.T("只看没加入 ✓") : L.T("只看没加入");

    public int TotalCount => Items.Count;
    public int UsedCount => Items.Count(i => i.InProfile);
    public int UnusedCount => Items.Count(i => !i.InProfile);

    public string CountText =>
        L.F("共 {0} 个 · 当前配置「{1}」用了 {2} 个", TotalCount, _workspace.Library.ActiveProfile.Name, UsedCount);

    public bool IsEmpty => Items.Count == 0;
    public bool HasVisibleMods => !View.IsEmpty;
    public string ExternalButtonText => L.F("外部模组 ({0})", External.Count);

    // ---- 批量选取 ----

    /// <summary>批量模式：单击整行选中/取消，工具栏换成批量条。</summary>
    public bool BatchMode
    {
        get => _batchMode;
        set
        {
            if (!SetProperty(ref _batchMode, value)) return;
            if (!value) ClearSelectionCore();
            Raise(nameof(ShowBatchBar), nameof(BatchButtonText));
        }
    }

    public bool ShowBatchBar => _batchMode;
    public string BatchButtonText => _batchMode ? L.T("退出批量") : L.T("批量选取");

    public int SelectedCount => Items.Count(i => i.IsSelected);
    public bool HasSelection => SelectedCount > 0;
    public string SelectedText => L.F("已选 {0} 个", SelectedCount);

    /// <summary>当前筛选结果是不是已经全选中了（空列表不算）。</summary>
    public bool AllSelected =>
        !View.IsEmpty && View.OfType<PoolItemViewModel>().All(i => i.IsSelected);

    /// <summary>全选按钮点完就变成「取消全选」。</summary>
    public string SelectAllText => AllSelected ? L.T("取消全选") : L.T("全选");

    public string SelectAllTooltip => AllSelected
        ? L.T("取消当前搜索/筛选结果里的全部选中")
        : L.T("选中当前搜索/筛选结果里的全部模组");

    /// <summary>批量刷选期间不用逐行刷新；刷完一次性通知。</summary>
    public void BeginBulkSelection() { }

    public void EndBulkSelection(bool changed)
    {
        if (changed) NotifySelectionChanged();
    }

    /// <summary>行点击之后由视图调用，避免每行都挂一个 PropertyChanged。</summary>
    public void NotifySelectionChanged() =>
        Raise(nameof(SelectedCount), nameof(SelectedText), nameof(HasSelection),
            nameof(AllSelected), nameof(SelectAllText), nameof(SelectAllTooltip));

    private void ClearSelectionCore()
    {
        foreach (var item in Items) item.IsSelected = false;
        NotifySelectionChanged();
    }

    private void ExitBatch()
    {
        if (_batchMode)
        {
            BatchMode = false;   // setter 会清空选择
            return;
        }

        ClearSelectionCore();
    }

    /// <summary>
    /// 全选 / 取消全选当前可见的（搜索和「只看没加入」之后的）模组。
    /// 已经全选了就反向取消，按钮文字跟着变。
    /// </summary>
    private void ToggleSelectAll()
    {
        var targets = View.OfType<PoolItemViewModel>().ToList();
        if (targets.Count == 0) return;

        var select = !AllSelected;

        foreach (var item in targets)
        {
            if (item.IsSelected == select) continue;
            item.IsSelected = select;
        }

        NotifySelectionChanged();
    }

    public Task OnActivatedAsync()
    {
        Reload();
        return _workspace.Game.IsValid ? RefreshExternalAsync() : Task.CompletedTask;
    }

    public void Reload()
    {
        var active = _workspace.Library.ActiveProfile;
        var inProfile = new HashSet<string>(
            active.Entries.Select(e => e.ModId), StringComparer.OrdinalIgnoreCase);

        var existing = Items.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<PoolItemViewModel>();

        foreach (var record in _workspace.Library.Mods)
        {
            if (!existing.TryGetValue(record.Id, out var item))
                item = new PoolItemViewModel(record);

            item.ProfileName = active.Name;
            item.InProfile = inProfile.Contains(record.Id);
            ordered.Add(item);
        }

        foreach (var stale in Items.Except(ordered).ToList()) Items.Remove(stale);

        for (var i = 0; i < ordered.Count; i++)
        {
            var item = ordered[i];
            var current = Items.IndexOf(item);
            if (current < 0) Items.Insert(i, item);
            else if (current != i) Items.Move(current, i);
        }

        Raise(nameof(TotalCount), nameof(UsedCount), nameof(UnusedCount), nameof(CountText),
            nameof(IsEmpty), nameof(HasVisibleMods));
    }

    private bool FilterItem(object obj)
    {
        if (obj is not PoolItemViewModel item) return false;
        if (_onlyUnused && item.InProfile) return false;

        var query = (_searchText ?? "").Trim();
        if (query.Length == 0) return true;

        return item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
               || item.Record.Source.Contains(query, StringComparison.OrdinalIgnoreCase)
               || item.Record.VersionText.Contains(query, StringComparison.OrdinalIgnoreCase)
               || item.FolderText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void ImportFiles()
    {
        var paths = _ui.PickArchives();
        if (paths is { Count: > 0 }) _ = _ui.ImportAsync(paths);
    }

    private void ImportFolder()
    {
        var folder = _ui.PickFolder("选择模组文件夹（里面可以是任意层级的 EXML/MBIN/pak）");
        if (!string.IsNullOrEmpty(folder)) _ = _ui.ImportAsync(new[] { folder });
    }

    private void OpenFolder(PoolItemViewModel? item)
    {
        if (item == null) return;

        var folder = _workspace.FindModFolder(item.Record);
        if (string.IsNullOrEmpty(folder))
        {
            _ui.Toast(item.IsLegacyPak ? "pak 模组没有独立文件夹" : "找不到模组目录", false);
            return;
        }

        Launcher.OpenFolder(folder);
    }

    private async Task AddAsync(PoolItemViewModel? item)
    {
        if (item == null || item.InProfile) return;
        await AddRecordsAsync(new[] { item.Record });
    }

    private async Task BatchAddAsync()
    {
        var records = Items.Where(i => i.IsSelected && !i.InProfile).Select(i => i.Record).ToList();
        if (records.Count == 0)
        {
            _ui.Toast(SelectedCount == 0 ? "先选中模组" : "选中的模组都已经在当前配置里了", false);
            return;
        }

        await AddRecordsAsync(records);
        ExitBatch();
    }

    private async Task BatchRemoveAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            _ui.Toast("先选中模组", false);
            return;
        }

        var names = string.Join("\n", selected.Take(6).Select(i => "  · " + i.Name));
        if (selected.Count > 6) names += L.F("\n  … 另有 {0} 个", selected.Count - 6);

        var confirm = _ui.Confirm(L.F("把 {0} 个模组移出模组库", selected.Count),
            L.F("文件会被移到 MODS_disabled，随时可以重新收编，不会删除。\n\n{0}", names),
            L.T("移出模组库"));

        if (confirm != ConfirmResult.Primary) return;

        try
        {
            // 顺序跟单个移出保持一致：先摘出配置并落盘，部署时文件才会挪进 MODS_disabled，
            // 之后再把记录删掉。反过来的话文件会被当成「外部模组」留在 MODS 里。
            foreach (var item in selected)
                _workspace.Library.RemoveFromActiveProfile(item.Record);

            _workspace.Library.Save();
            if (_workspace.Game.IsValid) await _ui.DeployAsync(silent: true);

            foreach (var item in selected)
                _workspace.Library.Remove(item.Record);

            _workspace.Library.Save();
            _workspace.NotifyLibraryChanged();
            await RefreshExternalAsync();

            ExitBatch();
            _ui.Toast(L.F("已移出 {0} 个模组，文件在 MODS_disabled 里", selected.Count), true);
        }
        catch (Exception ex)
        {
            _ui.Toast(L.F("移出失败：{0}", ex.Message), false);
        }
    }

    private void OpenLink(PoolItemViewModel? item)
    {
        if (item == null) return;

        if (!item.HasLink)
        {
            EditLink(item);
            return;
        }

        Launcher.OpenUrl(item.Record.Url);
    }

    private void EditLink(PoolItemViewModel? item)
    {
        if (item == null) return;

        var input = DialogService.Prompt(L.F("模组链接 · {0}", item.Name),
            L.T("粘贴这个模组的页面地址（Nexus、oogc、论坛都行），留空表示清除。"), item.Record.Url);
        if (input == null) return;   // 取消

        var url = ModRecord.NormalizeUrl(input);
        if (url == null)
        {
            _ui.Toast("链接要写成 https:// 开头的网址", false);
            return;
        }

        if (url == item.Record.Url) return;

        item.Record.Url = url;
        _workspace.Library.Save();
        item.RefreshLink();
        _ui.Toast(url.Length == 0 ? "已清除链接" : "已保存链接", true);
    }

    private async Task AddRecordsAsync(IReadOnlyList<ModRecord> records)
    {
        if (_workspace.Library.ActiveProfile.BuiltIn)
        {
            _ui.Toast("「无模组」是内置配置，不放模组——先新建一套配置再加", false);
            return;
        }

        var (added, profile) = _workspace.Library.AddToActiveProfile(records);
        if (added == 0)
        {
            _ui.Toast("这些模组已经在当前配置里了", false);
            return;
        }

        _workspace.Library.Save();
        _workspace.NotifyLibraryChanged();

        if (_workspace.Game.IsValid) await _ui.DeployAsync(silent: true);

        _ui.Toast(L.F("已加入 {0} 个模组到「{1}」", added, profile.Name), true);
    }

    private void Delete(PoolItemViewModel? item)
    {
        if (item == null) return;

        var message = L.F("把「{0}」从模组库移除？\n\n", item.Name) +
                      L.F("文件（{0}）会被移到 MODS_disabled，随时可以重新收编，不会删除。\n", item.SizeText) +
                      L.T("如果确定不再需要，可以点「永久删除…」一并删除磁盘文件。");

        var result = _ui.Confirm(L.T("移除模组"), message, L.T("移出模组库"), L.T("永久删除…"));
        if (result == ConfirmResult.Primary) _ = SoftRemoveAsync(item);
        else if (result == ConfirmResult.Danger) _ = PermanentDeleteAsync(item);
    }

    private async Task SoftRemoveAsync(PoolItemViewModel item)
    {
        try
        {
            // 先从配置里摘掉并停用，部署时才会把文件挪进 MODS_disabled
            _workspace.Library.RemoveFromActiveProfile(item.Record);
            _workspace.Library.Save();

            if (_workspace.Game.IsValid) await _ui.DeployAsync(silent: true);

            _workspace.Library.Remove(item.Record);
            _workspace.Library.Save();
            _workspace.NotifyLibraryChanged();
            await RefreshExternalAsync();
            _ui.Toast("已移出模组库，文件在 MODS_disabled 里", true);
        }
        catch (Exception ex)
        {
            _ui.Toast(L.F("移出失败：{0}", ex.Message), false);
        }
    }

    private async Task PermanentDeleteAsync(PoolItemViewModel item)
    {
        var confirm = _ui.Confirm(L.T("永久删除"),
            L.F("「{0}」的磁盘文件会被永久删除，无法通过本程序恢复。\n\n", item.Name) +
            L.T("会同时删除 MODS 与 MODS_disabled 里的副本。确定继续吗？"),
            L.T("永久删除"));

        if (confirm != ConfirmResult.Primary) return;

        try
        {
            _workspace.Deployment.DeleteFiles(_workspace.Game.GamePath, item.Record);
            _workspace.Library.Remove(item.Record);
            _workspace.Library.Save();
            _workspace.NotifyLibraryChanged();
            await RefreshExternalAsync();
            _ui.Toast(L.F("已永久删除：{0}", item.Name), true);
        }
        catch (Exception ex)
        {
            _ui.Toast(L.F("删除失败：{0}", ex.Message), false);
        }
    }

    // ---- 外部模组 ----

    private async Task ToggleExternalPanelAsync()
    {
        ShowExternal = !ShowExternal;
        Raise(nameof(ShowExternal));
        if (ShowExternal) await RefreshExternalAsync();
    }

    public async Task RefreshExternalAsync()
    {
        var game = _workspace.Game.GamePath;
        var mods = _workspace.Library.Mods.ToList();
        var list = await Task.Run(() => _workspace.Deployment.ListExternal(game, mods));

        External.Clear();
        foreach (var item in list) External.Add(new ExternalModItemViewModel(item));

        Raise(nameof(ExternalButtonText), nameof(External));
        if (External.Count == 0) ShowExternal = false;
    }

    private async Task ToggleExternalAsync(ExternalModItemViewModel? item)
    {
        if (item == null || !_workspace.Game.IsValid) return;

        try
        {
            await Task.Run(() => _workspace.Deployment.SetExternal(
                _workspace.Game.GamePath, item.Model, !item.Model.Enabled));
            await RefreshExternalAsync();
            _ui.Toast(L.F("{0}：{1}", item.Name, L.T(item.Model.Enabled ? "已停用" : "已启用")), true);
        }
        catch (Exception ex)
        {
            _ui.Toast(L.F("操作失败：{0}", ex.Message), false);
        }
    }

    private async Task AdoptAsync(ExternalModItemViewModel? item)
    {
        if (item == null || !_workspace.Game.IsValid) return;

        try
        {
            var record = _workspace.Deployment.Adopt(_workspace.Game.GamePath, item.Model, _workspace.Library.NameExists);
            _workspace.Library.Add(record);
            var (added, profile) = _workspace.Library.AddToActiveProfile(new[] { record });
            _workspace.Library.Save();
            _workspace.NotifyLibraryChanged();
            await RefreshExternalAsync();
            _ui.Toast(added > 0
                ? L.F("已收编并加入「{0}」：{1}", profile.Name, record.Name)
                : L.F("已收编到模组库：{0}（当前是「无模组」，新建配置后再加）", record.Name), true);
        }
        catch (Exception ex)
        {
            _ui.Toast(L.F("收编失败：{0}", ex.Message), false);
        }
    }

    /// <summary>一次性把外部模组全收编。逐个来，坏的跳过并汇报，不因为一个失败就整批中断。</summary>
    private async Task AdoptAllAsync()
    {
        if (!_workspace.Game.IsValid)
        {
            _ui.Toast("先设置游戏目录", false);
            return;
        }

        var pending = External.ToList();
        if (pending.Count == 0)
        {
            _ui.Toast("没有可收编的外部模组", false);
            return;
        }

        var confirm = _ui.Confirm(L.F("全部收编（{0} 个）", pending.Count),
            L.T("把它们都登记进模组库，并加入当前配置，之后就能统一启停。\n\n") +
            L.T("文件不会被删除；MODS 根目录里散放的 pak 会收进同名文件夹，方便启停。"),
            L.T("全部收编"));
        if (confirm != ConfirmResult.Primary) return;

        var adopted = new List<ModRecord>();
        var failed = new List<string>();

        foreach (var item in pending)
        {
            try
            {
                var record = _workspace.Deployment.Adopt(
                    _workspace.Game.GamePath, item.Model, _workspace.Library.NameExists);
                _workspace.Library.Add(record);
                adopted.Add(record);
            }
            catch (Exception ex)
            {
                failed.Add(item.Name);
                AppLog.Warn($"收编失败 {item.Name}：{ex.Message}");
            }
        }

        var joined = 0;
        if (adopted.Count > 0)
        {
            (joined, _) = _workspace.Library.AddToActiveProfile(adopted);
            _workspace.Library.Save();
            _workspace.NotifyLibraryChanged();
        }

        await RefreshExternalAsync();

        // 当前是「无模组」时收编的模组只进库、不进配置，得说清楚，否则用户以为加进去了
        var left = adopted.Count - joined;

        if (failed.Count == 0)
        {
            _ui.Toast(left > 0
                ? L.F("已收编 {0} 个模组，{1} 个留在模组库（当前是「无模组」）", adopted.Count, left)
                : L.F("已收编 {0} 个模组", adopted.Count), true);
            return;
        }

        var sample = string.Join("、", failed.Take(3));
        if (failed.Count > 3) sample += "…";
        _ui.Toast(adopted.Count > 0
            ? L.F("已收编 {0} 个，{1} 个失败（{2}）", adopted.Count, failed.Count, sample)
            : L.F("收编失败：{0}", sample), false);
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using AllModsSky.App.Mvvm;
using AllModsSky.App.Services;

namespace AllModsSky.App.ViewModels;

/// <summary>
/// 模组页。列表只显示当前配置里的模组（顺序即加载顺序）；
/// 不在配置里的模组待在「模组库」页，点「添加模组」挑进来。
/// </summary>
public sealed class LibraryViewModel : ObservableObject
{
    private readonly Workspace _workspace;
    private readonly IShellUi _ui;
    private readonly Dictionary<string, ModItemViewModel> _cache = new(StringComparer.OrdinalIgnoreCase);

    private string _searchText = "";
    private string _filter = "All";
    private bool _batchMode;
    private bool _profileMenuOpen;
    private bool _bulkSelecting;

    public LibraryViewModel(Workspace workspace, IShellUi ui)
    {
        _workspace = workspace;
        _ui = ui;

        Items = new ObservableCollection<ModItemViewModel>();
        View = CollectionViewSource.GetDefaultView(Items);
        View.Filter = FilterItem;

        ImportFilesCommand = new RelayCommand(ImportFiles);
        ImportFolderCommand = new RelayCommand(ImportFolder);
        AddModsCommand = new AsyncRelayCommand(AddModsAsync);
        RefreshCommand = new AsyncRelayCommand(() => _ui.RefreshAllAsync());
        DeployCommand = new AsyncRelayCommand(() => _ui.DeployAsync(false));
        OpenModsFolderCommand = new RelayCommand(() => Launcher.OpenFolder(_workspace.ModsDir));
        OpenLibraryCommand = new RelayCommand(() => _ui.Navigate(PageKey.Pool));
        OpenFolderCommand = new RelayCommand(p => OpenFolder(p as ModItemViewModel));
        DeleteCommand = new RelayCommand(p => Delete(p as ModItemViewModel));
        RemoveFromProfileCommand = new AsyncRelayCommand(p => RemoveFromProfileAsync(p as ModItemViewModel));
        MoveTopCommand = new AsyncRelayCommand(() => MoveSelectedAsync(true));
        MoveBottomCommand = new AsyncRelayCommand(() => MoveSelectedAsync(false));
        BatchEnableCommand = new AsyncRelayCommand(() => SetSelectedEnabledAsync(true));
        BatchDisableCommand = new AsyncRelayCommand(() => SetSelectedEnabledAsync(false));
        BatchRemoveCommand = new AsyncRelayCommand(BatchRemoveAsync);
        BatchDeleteCommand = new AsyncRelayCommand(BatchDeleteAsync);
        SelectAllCommand = new RelayCommand(ToggleSelectAll);
        ClearSelectionCommand = new RelayCommand(ExitBatch);
        ToggleBatchCommand = new RelayCommand(() => BatchMode = !BatchMode);
        ApplyOrderCommand = new RelayCommand(ApplyOrder);

        ToggleProfileMenuCommand = new RelayCommand(() =>
        {
            if (!IsProfileMenuOpen) RefreshProfiles();
            IsProfileMenuOpen = !IsProfileMenuOpen;
        });
        NewProfileCommand = new RelayCommand(NewProfile);
        SwitchProfileCommand = new AsyncRelayCommand(p => SwitchProfileAsync(p as ModProfile));
        RenameProfileCommand = new RelayCommand(p => RenameProfile(p as ModProfile));
        DeleteProfileCommand = new RelayCommand(p => DeleteProfile(p as ModProfile));

        _workspace.LibraryChanged += SyncFromLibrary;
        _workspace.GameChanged += OnGameChanged;
    }

    public ObservableCollection<ModItemViewModel> Items { get; }
    public ICollectionView View { get; }

    public ICommand ImportFilesCommand { get; }
    public ICommand ImportFolderCommand { get; }
    public ICommand AddModsCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand DeployCommand { get; }
    public ICommand OpenModsFolderCommand { get; }
    public ICommand OpenLibraryCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand RemoveFromProfileCommand { get; }
    public ICommand MoveTopCommand { get; }
    public ICommand MoveBottomCommand { get; }
    public ICommand BatchEnableCommand { get; }
    public ICommand BatchDisableCommand { get; }
    public ICommand BatchRemoveCommand { get; }
    public ICommand BatchDeleteCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand ToggleBatchCommand { get; }
    public ICommand ApplyOrderCommand { get; }
    public ICommand ToggleProfileMenuCommand { get; }
    public ICommand NewProfileCommand { get; }
    public ICommand SwitchProfileCommand { get; }
    public ICommand RenameProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }

    /// <summary>配置菜单里的条目（内置「无模组」永远第一）。</summary>
    public ObservableCollection<ModProfile> Profiles { get; } = new();

    public bool IsProfileMenuOpen
    {
        get => _profileMenuOpen;
        set => SetProperty(ref _profileMenuOpen, value);
    }

    public string ActiveProfileName => _workspace.Library.ActiveProfile.Name;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            View.Refresh();
            Raise(nameof(HasVisibleMods), nameof(AllSelected), nameof(SelectAllText),
                nameof(SelectAllTooltip));
        }
    }

    public string Filter
    {
        get => _filter;
        set
        {
            if (!SetProperty(ref _filter, value)) return;

            // 换分类就退出批量：不然选中的那批被筛没了，批量条还挂着「已选 N 个」，
            // 点「停用」会改到看不见的模组。
            if (BatchMode) ExitBatch();

            View.Refresh();
            Raise(nameof(HasVisibleMods), nameof(AllSelected), nameof(SelectAllText),
                nameof(SelectAllTooltip));
        }
    }

    /// <summary>批量选取模式：单击整行（不含按钮/开关）即可选中或取消。</summary>
    public bool BatchMode
    {
        get => _batchMode;
        set
        {
            if (!SetProperty(ref _batchMode, value)) return;
            ClearSelectionCore();
            Raise(nameof(ShowBatchBar), nameof(BatchButtonText));
        }
    }

    public bool ShowBatchBar => BatchMode;
    public string BatchButtonText => BatchMode ? L.T("退出批量") : L.T("批量选取");

    public bool HasVisibleMods => !View.IsEmpty;
    public bool IsLibraryEmpty => Items.Count == 0;

    public int TotalCount => Items.Count;
    public int EnabledCount => Items.Count(i => i.Enabled);
    public int SelectedCount => Items.Count(i => i.IsSelected);
    public bool HasSelection => SelectedCount > 0;

    /// <summary>当前筛选结果是不是已经全选中了（空列表不算）。</summary>
    public bool AllSelected =>
        !View.IsEmpty && View.OfType<ModItemViewModel>().All(i => i.IsSelected);

    /// <summary>全选按钮点完就变成「取消全选」，不用再单独放一个按钮。</summary>
    public string SelectAllText => AllSelected ? L.T("取消全选") : L.T("全选");

    public string SelectAllTooltip => AllSelected
        ? L.T("取消当前搜索/筛选结果里的全部选中")
        : L.T("选中当前搜索/筛选结果里的全部模组");

    public string CountText =>
        L.F("配置「{0}」· {1} 个模组 · 已启用 {2}", ActiveProfileName, TotalCount, EnabledCount);

    public string SelectedText => L.F("已选 {0} 个", SelectedCount);

    public string EmptyTitle => _workspace.Library.ActiveProfile.BuiltIn
        ? L.T("「无模组」不放模组")
        : L.T("这套配置里还没有模组");

    public string EmptyHint => _workspace.Library.ActiveProfile.BuiltIn
        ? L.T("这套内置配置就是全部停用、直接启动原版。想玩模组，点上面的「配置」换一套。")
        : L.T("点「添加模组」从模组库里挑，或者直接导入新的。");

    /// <summary>
    /// 空状态里的三个按钮。内置「无模组」下它们都没意义（工具栏上本来也有同样的入口），
    /// 这时只留一句「去换个配置」。
    /// </summary>
    public bool ShowEmptyActions => !_workspace.Library.ActiveProfile.BuiltIn;

    public bool GameValid => _workspace.Game.IsValid;
    public string GamePathText => _workspace.Game.IsValid ? _workspace.Game.GamePath : L.T("未设置游戏目录 — 去「设置」里指定");
    public string GameVersionText => _workspace.Game.IsValid ? _workspace.Game.VersionLine : "";
    public string ModsPath => _workspace.Game.ModsPath;
    public bool ModsWritable => _workspace.Game.ModsWritable;
    public string DisableFlagText =>
        _workspace.Game.IsValid && _workspace.Game.DisableModsFileExists ? "DISABLEMODS.TXT " + L.T("已存在") : "";

    public List<ModItemViewModel> SelectedItems => Items.Where(i => i.IsSelected).ToList();

    // ---- 同步 ----

    public void ReloadFromLibrary()
    {
        foreach (var item in _cache.Values) item.PropertyChanged -= Item_PropertyChanged;
        _cache.Clear();
        Items.Clear();
        SyncFromLibrary();
    }

    public void SyncFromLibrary()
    {
        var active = _workspace.Library.ActiveMods;

        var alive = new HashSet<string>(active.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var id in _cache.Keys.Where(k => !alive.Contains(k)).ToList())
        {
            _cache[id].PropertyChanged -= Item_PropertyChanged;
            _cache.Remove(id);
        }

        var wanted = new List<ModItemViewModel>(active.Count);
        foreach (var record in active) wanted.Add(Materialize(record));

        foreach (var stale in Items.Where(i => !wanted.Contains(i)).ToList()) Items.Remove(stale);

        for (var i = 0; i < wanted.Count; i++)
        {
            var item = wanted[i];
            var current = Items.IndexOf(item);
            if (current < 0) Items.Insert(i, item);
            else if (current != i) Items.Move(current, i);
        }

        RefreshProfiles();
        RaiseCounts();

        // 列表被整份换掉（切配置、新建/删除配置、导入、刷新）时，批选里那批对象已经不在了：
        // 批量条还挂着的话，点「停用」会改到列表里看不见的模组，所以直接退出。
        if (BatchMode) ExitBatch();
    }

    private ModItemViewModel Materialize(ModRecord record)
    {
        if (_cache.TryGetValue(record.Id, out var item))
        {
            item.SetEnabledSilently(record.Enabled);
            return item;
        }

        item = new ModItemViewModel(record, OnItemToggled);
        item.PropertyChanged += Item_PropertyChanged;
        _cache[record.Id] = item;
        return item;
    }

    private void Reindex()
    {
        for (var i = 0; i < Items.Count; i++) Items[i].Index = i + 1;
    }

    private bool FilterItem(object obj)
    {
        if (obj is not ModItemViewModel item) return false;

        var query = (_searchText ?? "").Trim();
        if (query.Length > 0)
        {
            var hit = item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                      || item.Source.Contains(query, StringComparison.OrdinalIgnoreCase)
                      || item.VersionText.Contains(query, StringComparison.OrdinalIgnoreCase)
                      || item.Record.EffectiveFolder.Contains(query, StringComparison.OrdinalIgnoreCase);
            if (!hit) return false;
        }

        return _filter switch
        {
            "Enabled" => item.Enabled,
            "Disabled" => !item.Enabled,
            "Issues" => item.HasWarning,
            _ => true
        };
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ModItemViewModel.IsSelected) or nameof(ModItemViewModel.Enabled))) return;
        if (_bulkSelecting) return;   // 批量改选中状态时先别刷新，循环结束统一来一次

        RaiseCounts();
    }

    /// <summary>
    /// 刷选（按住左键往下滑）时用：先把逐行刷新压住，划过的行全部改完再统一刷一次。
    /// </summary>
    public void BeginBulkSelection() => _bulkSelecting = true;

    public void EndBulkSelection(bool changed)
    {
        _bulkSelecting = false;
        if (changed) RaiseCounts();
    }

    private void RaiseCounts()
    {
        Reindex();
        Raise(nameof(TotalCount), nameof(EnabledCount), nameof(SelectedCount), nameof(HasSelection),
            nameof(AllSelected), nameof(SelectAllText), nameof(SelectAllTooltip),
            nameof(CountText), nameof(SelectedText), nameof(IsLibraryEmpty), nameof(HasVisibleMods),
            nameof(ShowBatchBar), nameof(BatchButtonText), nameof(ActiveProfileName),
            nameof(EmptyTitle), nameof(EmptyHint), nameof(ShowEmptyActions),
            nameof(GameValid), nameof(GamePathText), nameof(GameVersionText), nameof(ModsPath),
            nameof(ModsWritable), nameof(DisableFlagText));
    }

    private void OnGameChanged()
    {
        Raise(nameof(GameValid), nameof(GamePathText), nameof(GameVersionText), nameof(ModsPath),
            nameof(ModsWritable), nameof(DisableFlagText));
    }

    // ---- 导入 / 添加 / 打开 / 删除 ----

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

    /// <summary>从模组库里挑几个模组加进当前配置。</summary>
    private async Task AddModsAsync()
    {
        if (_workspace.Library.ActiveProfile.BuiltIn)
        {
            _ui.Toast("「无模组」是内置配置，不放模组——先新建一套配置再加", false);
            return;
        }

        var candidates = _workspace.Library.PoolMods;
        if (candidates.Count == 0)
        {
            _ui.Toast("模组库里没有可加的模组，先去「模组库」导入", false);
            return;
        }

        var profile = _workspace.Library.ActiveProfile;
        var picked = DialogService.PickMods(candidates, profile.Name);
        if (picked is null || picked.Count == 0) return;

        var (added, target) = _workspace.Library.AddToActiveProfile(picked);
        if (added == 0)
        {
            _ui.Toast("这些模组已经在当前配置里了", false);
            return;
        }

        _workspace.Library.Save();
        _workspace.NotifyLibraryChanged();   // 模组库页的「已加入」标记要一起变

        // 新加进来的条目还没有格式/适配信息，只补查缺的
        await RefreshHealthAsync(onlyMissing: true);

        if (_workspace.Game.IsValid) await _ui.DeployAsync(silent: true);

        _ui.Toast(L.F("已加入 {0} 个模组到「{1}」", added, target.Name), true);
    }

    private void OpenFolder(ModItemViewModel? item)
    {
        if (item == null) return;
        var folder = _workspace.FindModFolder(item.Record);
        if (string.IsNullOrEmpty(folder))
        {
            _ui.Toast(item.Record.IsLegacyPak ? L.T("pak 模组没有独立文件夹") : L.T("找不到模组目录"), false);
            return;
        }
        Launcher.OpenFolder(folder);
    }

    private async void OnItemToggled(ModItemViewModel item)
    {
        try
        {
            AppLog.Info($"切换模组：{item.Name} → {(item.Enabled ? "启用" : "停用")}");
            _workspace.Library.SetEntriesEnabled(new[] { item.Record }, item.Enabled);
            _workspace.Library.Save();
            RaiseCounts();

            if (!_workspace.Game.IsValid)
            {
                _ui.Toast(L.F("{0}：{1}（未设置游戏目录）", item.Name, L.T(item.Enabled ? "已启用" : "已停用")), true);
                return;
            }

            var report = await _ui.DeployAsync(silent: true);
            var ok = report is { Failed: 0 };
            _ui.Toast(L.F("{0}：{1}", item.Name, L.T(item.Enabled ? "已启用" : "已停用")) +
                      (ok ? "" : L.T("，但同步时出现问题")), ok);
        }
        catch (Exception ex)
        {
            _ui.Toast(L.F("保存失败：{0}", ex.Message), false);
        }
    }

    private void Delete(ModItemViewModel? item)
    {
        if (item == null) return;

        var message = L.F("把「{0}」从模组库移除？\n\n", item.Name) +
                      L.F("文件（{0}）会被移到 MODS_disabled，随时可以重新收编，不会删除。\n", item.SizeText) +
                      L.T("只是不想让它出现在这套配置里的话，点行尾的「移出配置」就够了。\n\n") +
                      L.T("如果确定不再需要，可以点「永久删除…」一并删除磁盘文件。");

        var result = _ui.Confirm(L.T("移除模组"), message, L.T("移出模组库"), L.T("永久删除…"));
        if (result == ConfirmResult.Primary) _ = SoftRemoveAsync(item);
        else if (result == ConfirmResult.Danger) _ = PermanentDeleteAsync(item);
    }

    private async Task SoftRemoveAsync(ModItemViewModel item)
    {
        try
        {
            // 先从配置里摘掉并停用，部署时才会把文件挪进 MODS_disabled
            _workspace.Library.RemoveFromActiveProfile(item.Record);
            _workspace.Library.Save();

            if (_workspace.Game.IsValid)
                await _ui.DeployAsync(silent: true);

            _workspace.Library.Remove(item.Record);
            _workspace.Library.Save();
            _workspace.NotifyLibraryChanged();
            _ui.Toast(L.T("已移出模组库，文件在 MODS_disabled 里"), true);
        }
        catch (Exception ex)
        {
            _ui.Toast(L.F("移出失败：{0}", ex.Message), false);
        }
    }

    private Task PermanentDeleteAsync(ModItemViewModel item)
    {
        var confirm = _ui.Confirm(L.T("永久删除"),
            L.F("「{0}」的磁盘文件会被永久删除，无法通过本程序恢复。\n\n", item.Name) +
            L.T("会同时删除 MODS 与 MODS_disabled 里的副本。确定继续吗？"),
            L.T("永久删除"));

        if (confirm != ConfirmResult.Primary) return Task.CompletedTask;

        try
        {
            _workspace.Deployment.DeleteFiles(_workspace.Game.GamePath, item.Record);
            _workspace.Library.Remove(item.Record);
            _workspace.Library.Save();
            _workspace.NotifyLibraryChanged();
            _ui.Toast(L.F("已永久删除：{0}", item.Name), true);
        }
        catch (Exception ex)
        {
            _ui.Toast(L.F("删除失败：{0}", ex.Message), false);
        }

        return Task.CompletedTask;
    }

    private async Task SetSelectedEnabledAsync(bool enabled)
    {
        var selected = SelectedItems;
        if (selected.Count == 0)
        {
            _ui.Toast("先选中模组", false);
            return;
        }

        var records = selected.Select(i => i.Record).ToList();
        _workspace.Library.SetEntriesEnabled(records, enabled);
        foreach (var item in selected) item.SetEnabledSilently(enabled);
        _workspace.Library.Save();
        RaiseCounts();

        if (_workspace.Game.IsValid) await _ui.DeployAsync(silent: true);

        _ui.Toast(L.F("已{0} {1} 个模组", L.T(enabled ? "启用" : "停用"), selected.Count), true);
        ExitBatch();
    }

    /// <summary>批量把选中项从当前配置里摘掉，文件仍留在模组库。</summary>
    private async Task BatchRemoveAsync()
    {
        var selected = SelectedItems;
        if (selected.Count == 0)
        {
            _ui.Toast("先选中模组", false);
            return;
        }

        var confirm = _ui.Confirm(L.F("从当前配置移出 {0} 个模组", selected.Count),
            L.F("这 {0} 个模组会从「{1}」里摘掉，文件仍留在模组库，随时可以再加回来。", selected.Count, ActiveProfileName),
            L.T("移出配置"));

        if (confirm != ConfirmResult.Primary) return;

        var removed = 0;
        foreach (var item in selected)
        {
            if (_workspace.Library.RemoveFromActiveProfile(item.Record)) removed++;
        }

        if (removed == 0)
        {
            _ui.Toast("这些模组不在当前配置里", false);
            return;
        }

        _workspace.Library.Save();
        _workspace.NotifyLibraryChanged();

        if (_workspace.Game.IsValid) await _ui.DeployAsync(silent: true);

        _ui.Toast(L.F("已移出 {0} 个模组，回到模组库", removed), true);
        ExitBatch();
    }

    private Task BatchDeleteAsync()
    {
        var selected = SelectedItems;
        if (selected.Count == 0)
        {
            _ui.Toast("先选中模组", false);
            return Task.CompletedTask;
        }

        var total = selected.Sum(i => i.Record.Size);
        var names = string.Join("\n", selected.Take(6).Select(i => "  · " + i.Name));
        if (selected.Count > 6) names += L.F("\n  … 另有 {0} 个", selected.Count - 6);

        var confirm = _ui.Confirm(L.F("永久删除 {0} 个模组", selected.Count),
            L.F("将永久删除约 {0} 的文件，无法恢复。\n\n{1}\n\n", Humanize.Size(total), names) +
            L.T("会同时删除 MODS 与 MODS_disabled 里的目录。"),
            L.T("永久删除"));

        if (confirm != ConfirmResult.Primary) return Task.CompletedTask;

        foreach (var item in selected)
        {
            _workspace.Deployment.DeleteFiles(_workspace.Game.GamePath, item.Record);
            _workspace.Library.Remove(item.Record);
        }

        _workspace.Library.Save();
        _workspace.NotifyLibraryChanged();
        _ui.Toast(L.F("已删除 {0} 个模组", selected.Count), true);
        return Task.CompletedTask;
    }

    private Task MoveSelectedAsync(bool toTop)
    {
        var selected = SelectedItems;
        if (selected.Count == 0)
        {
            _ui.Toast("先选中模组", false);
            return Task.CompletedTask;
        }

        var records = selected.Select(i => i.Record).ToList();
        if (!_workspace.Library.MoveEntriesToEdge(records, toTop)) return Task.CompletedTask;

        _workspace.Library.Save();
        SyncFromLibrary();
        _ui.Toast(L.F(toTop ? "已移到顶部：{0} 个模组" : "已移到底部：{0} 个模组", selected.Count), true);
        return Task.CompletedTask;
    }

    private void ClearSelectionCore()
    {
        foreach (var item in Items) item.IsSelected = false;
        RaiseCounts();
    }

    /// <summary>
    /// 全选 / 取消全选当前可见的模组（搜索和筛选之后的那一批），不是整个配置。
    /// 已经全选了就反向取消，按钮文字跟着变，不额外占一个按钮的位置。
    /// 逐个设置会触发 N 次 RaiseCounts，这里先关掉通知再统一刷新。
    /// </summary>
    private void ToggleSelectAll()
    {
        var targets = View.OfType<ModItemViewModel>().ToList();
        if (targets.Count == 0) return;

        var select = !AllSelected;

        _bulkSelecting = true;
        try
        {
            foreach (var item in targets)
            {
                if (item.IsSelected == select) continue;
                item.IsSelected = select;
            }
        }
        finally
        {
            _bulkSelecting = false;
        }

        RaiseCounts();
    }

    /// <summary>退出批量模式并清空选择（Esc / 批量按钮 / 取消选择共用）。</summary>
    public void ExitBatch()
    {
        if (BatchMode)
        {
            BatchMode = false; // setter 会清空选择
            return;
        }

        ClearSelectionCore();
    }

    /// <summary>拖拽落位：把 source 放到 target 之前/之后。</summary>
    public void MoveItem(ModItemViewModel source, ModItemViewModel? target, bool after)
    {
        var from = Items.IndexOf(source);
        if (from < 0) return;

        int to;
        if (target == null)
        {
            to = Items.Count - 1;
        }
        else
        {
            to = Items.IndexOf(target);
            if (to < 0) return;
            if (after) to++;
            if (from < to) to--;
        }

        to = Math.Clamp(to, 0, Items.Count - 1);
        if (from == to) return;

        if (!_workspace.Library.MoveEntry(from, to)) return;
        _workspace.Library.Save();

        // 只挪这一条，不重建整个视图。走 SyncFromLibrary 会 View.Refresh()，
        // 容器被全部重建，拖拽时就没东西可动了（表现就是「没有动画」）。
        Items.Move(from, to);
        Reindex();
    }

    // ---- 配置 ----

    private void RefreshProfiles()
    {
        // 指向已删模组的条目会让菜单显示成「1 / 1 个启用」，跟实际列表对不上，进菜单前先清掉
        if (_workspace.Library.PruneMissingEntries() > 0) _workspace.Library.Save();

        var menu = _workspace.Library.MenuProfiles.ToList();

        // SummaryText 是算出来的，对象没换就绑不到新值：清理过条目之后必须重建一次，
        // 否则菜单还写着旧的「1 / 1 个启用」。
        var same = Profiles.Count == menu.Count
                   && Profiles.Zip(menu).All(p => ReferenceEquals(p.First, p.Second)
                                                  && p.First.SummaryText == p.Second.SummaryText);
        if (same) return;

        Profiles.Clear();
        foreach (var profile in menu) Profiles.Add(profile);
    }

    private void NewProfile()
    {
        // 弹层是独立窗口，会浮在模态对话框上面，所以先收起菜单再问名字
        IsProfileMenuOpen = false;

        var input = DialogService.Prompt(L.T("新建配置"),
            L.F("把当前这套模组（含顺序和启停状态）复制成一套新配置，最多 {0} 个字：", ModProfile.MaxNameLength),
            _workspace.Library.NextProfileName());
        if (string.IsNullOrWhiteSpace(input)) return;

        // 先截断再查重：不然「超长名」和「截断后同名」会各判一次，提示对不上
        var name = TextLimit.Clamp(input, ModProfile.MaxNameLength);
        var truncated = name != input.Trim();

        if (_workspace.Library.ProfileNameExists(name))
        {
            _ui.Toast("已经有同名配置了", false);
            return;
        }

        var profile = _workspace.Library.CreateProfile(name, copyActive: true);
        _workspace.Library.SetActiveProfile(profile.Id);
        _workspace.Library.Save();

        _workspace.NotifyLibraryChanged();
        _ui.Toast(L.F("已新建「{0}」，复制了 {1} 个模组{2}", profile.Name, profile.Entries.Count,
            TruncatedHint(truncated)), true);
    }

    private static string TruncatedHint(bool truncated) =>
        truncated ? L.F("（名字超过 {0} 个字，已截断）", ModProfile.MaxNameLength) : "";

    private async Task SwitchProfileAsync(ModProfile? profile)
    {
        if (profile == null) return;

        var missing = _workspace.Library.MissingProfileEntries(profile);
        var changed = _workspace.Library.SetActiveProfile(profile.Id);

        _workspace.NotifyLibraryChanged();

        // 切到「无模组」会把缓存里的条目全丢掉，切回来是新对象，格式/适配都是空的。
        // 这里补查一遍，已经查过的直接跳过。
        await RefreshHealthAsync(onlyMissing: true);

        _workspace.Library.Save();
        IsProfileMenuOpen = false;

        if (_workspace.Game.IsValid) await _ui.DeployAsync(silent: true);

        var text = L.F("已切换到「{0}」", profile.Name);
        text += changed > 0 ? L.F("，{0} 个模组状态改变", changed) : L.T("，状态没有变化");
        if (missing > 0) text += L.F("；{0} 个模组已不在库中", missing);

        _ui.Toast(text, true);
    }

    private void RenameProfile(ModProfile? profile)
    {
        if (profile == null) return;
        if (profile.BuiltIn)
        {
            _ui.Toast("「无模组」是内置配置，不能改名", false);
            return;
        }

        IsProfileMenuOpen = false;

        var input = DialogService.Prompt(L.F("重命名配置「{0}」", profile.Name),
            L.F("新名字（最多 {0} 个字）：", ModProfile.MaxNameLength), profile.Name);
        if (string.IsNullOrWhiteSpace(input)) return;

        var name = TextLimit.Clamp(input, ModProfile.MaxNameLength);
        var truncated = name != input.Trim();

        if (name == profile.Name) return;

        if (_workspace.Library.ProfileNameExists(name, profile))
        {
            _ui.Toast("已经有同名配置了", false);
            return;
        }

        _workspace.Library.RenameProfile(profile, name);
        _workspace.Library.Save();
        RefreshProfiles();
        Raise(nameof(ActiveProfileName), nameof(CountText));
        _ui.Toast(L.F("已重命名为「{0}」{1}", name, TruncatedHint(truncated)), true);
    }

    private void DeleteProfile(ModProfile? profile)
    {
        if (profile == null) return;
        if (profile.BuiltIn)
        {
            _ui.Toast("「无模组」是内置配置，删不掉", false);
            return;
        }

        if (!profile.Deletable)
        {
            _ui.Toast("至少要保留一套配置，先新建一套再删它", false);
            return;
        }

        IsProfileMenuOpen = false;

        var confirm = _ui.Confirm(L.F("删除配置「{0}」", profile.Name),
            L.T("只删除这套配置，模组本身和文件都不动——它们会回到模组库里。"), L.T("删除"));
        if (confirm != ConfirmResult.Primary) return;

        _workspace.Library.RemoveProfile(profile);
        _workspace.Library.Save();
        _workspace.NotifyLibraryChanged();
        _ui.Toast(L.F("已删除「{0}」，当前配置：{1}", profile.Name, ActiveProfileName), true);
    }

    private void ApplyOrder()
    {
        if (!_workspace.Game.IsValid)
        {
            _ui.Alert(L.T("应用加载顺序"), L.T("还没有设置游戏目录。"));
            return;
        }

        var confirm = _ui.Confirm(L.T("应用加载顺序"),
            L.T("将把当前配置里的模组顺序写入游戏设置 GCMODSETTINGS.MXML 的 ModPriority。\n") +
            L.T("列表越靠上，优先级越高（冲突时覆盖靠下的模组）。原文件会自动备份为 .bak。\n\n继续吗？"),
            L.T("写入顺序"));

        if (confirm != ConfirmResult.Primary) return;

        var names = _workspace.Library.ActiveMods
            .Select(m => LoadOrderService.ToXmlName(m.EffectiveFolder))
            .ToList();

        var result = _workspace.LoadOrder.Apply(_workspace.Game.GamePath, names);

        if (!result.Ok)
        {
            _ui.Alert(L.T("应用加载顺序"),
                result.Error ?? L.T("GCMODSETTINGS.MXML 里没有匹配的模组条目。先进一次游戏让它生成。"));
            return;
        }

        var text = L.F("已写入 {0} 个模组的 ModPriority", result.Updated);
        if (result.Missing > 0) text += L.F("，{0} 个在 XML 里找不到（可能还没被游戏扫描到）", result.Missing);
        _ui.Toast(text, true);
    }

    private async Task RemoveFromProfileAsync(ModItemViewModel? item)
    {
        if (item == null) return;

        if (!_workspace.Library.RemoveFromActiveProfile(item.Record))
        {
            _ui.Toast("这个模组不在当前配置里", false);
            return;
        }

        _workspace.Library.Save();
        _workspace.NotifyLibraryChanged();

        if (_workspace.Game.IsValid) await _ui.DeployAsync(silent: true);

        _ui.Toast(L.F("{0}：已移出配置，回到模组库", item.Name), true);
    }

    /// <summary>
    /// 检查模组的格式/适配情况。
    /// <paramref name="onlyMissing"/> 为真时只补查还没查过的——切换配置时用，
    /// 切到「无模组」会把缓存清空，切回来新建的条目是空的，但已经查过的不用重扫磁盘。
    /// </summary>
    public async Task RefreshHealthAsync(bool onlyMissing = false)
    {
        var gameVersion = _workspace.Settings.GameVersion;
        var items = _cache.Values.Where(i => !onlyMissing || !i.IsHealthReady).ToList();
        if (items.Count == 0) return;

        var results = await Task.Run(() => items
            .Select(item => (Item: item, Health: ModInspector.Evaluate(
                item.Record, _workspace.FindModFolder(item.Record), gameVersion)))
            .ToList());

        foreach (var (item, health) in results) item.ApplyHealth(health);

        RaiseCounts();
        View.Refresh();
    }
}

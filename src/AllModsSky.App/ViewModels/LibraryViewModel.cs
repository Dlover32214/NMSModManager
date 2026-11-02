using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using AllModsSky.App.Mvvm;
using AllModsSky.App.Services;
using AllModsSky.Core;

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
        BatchDeleteCommand = new AsyncRelayCommand(BatchDeleteAsync);
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
    public ICommand BatchDeleteCommand { get; }
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
            Raise(nameof(HasVisibleMods));
        }
    }

    public string Filter
    {
        get => _filter;
        set
        {
            if (!SetProperty(ref _filter, value)) return;
            View.Refresh();
            Raise(nameof(HasVisibleMods));
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
    public string BatchButtonText => BatchMode ? "退出批量" : "批量选取";

    public bool HasVisibleMods => !View.IsEmpty;
    public bool IsLibraryEmpty => Items.Count == 0;

    public int TotalCount => Items.Count;
    public int EnabledCount => Items.Count(i => i.Enabled);
    public int SelectedCount => Items.Count(i => i.IsSelected);
    public bool HasSelection => SelectedCount > 0;

    public string CountText =>
        $"配置「{ActiveProfileName}」· {TotalCount} 个模组 · 已启用 {EnabledCount}";

    public string SelectedText => $"已选 {SelectedCount} 个";

    public string EmptyTitle => _workspace.Library.ActiveProfile.BuiltIn
        ? "「无模组」不放模组"
        : "这套配置里还没有模组";

    public string EmptyHint => _workspace.Library.ActiveProfile.BuiltIn
        ? "这套内置配置就是全部停用、直接启动原版。要玩模组，先新建一套配置。"
        : "点「添加模组」从模组库里挑，或者直接导入新的。";

    public bool GameValid => _workspace.Game.IsValid;
    public string GamePathText => _workspace.Game.IsValid ? _workspace.Game.GamePath : "未设置游戏目录 — 去「设置」里指定";
    public string GameVersionText => _workspace.Game.IsValid ? _workspace.Game.VersionLine : "";
    public string ModsPath => _workspace.Game.ModsPath;
    public bool ModsWritable => _workspace.Game.ModsWritable;
    public string DisableFlagText =>
        _workspace.Game.IsValid && _workspace.Game.DisableModsFileExists ? "DISABLEMODS.TXT 已存在" : "";

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
        if (e.PropertyName is nameof(ModItemViewModel.IsSelected) or nameof(ModItemViewModel.Enabled))
            RaiseCounts();
    }

    private void RaiseCounts()
    {
        Reindex();
        Raise(nameof(TotalCount), nameof(EnabledCount), nameof(SelectedCount), nameof(HasSelection),
            nameof(CountText), nameof(SelectedText), nameof(IsLibraryEmpty), nameof(HasVisibleMods),
            nameof(ShowBatchBar), nameof(BatchButtonText), nameof(ActiveProfileName),
            nameof(EmptyTitle), nameof(EmptyHint),
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
        SyncFromLibrary();

        if (_workspace.Game.IsValid) await _ui.DeployAsync(silent: true);

        _ui.Toast($"已加入 {added} 个模组到「{target.Name}」", true);
    }

    private void OpenFolder(ModItemViewModel? item)
    {
        if (item == null) return;
        var folder = _workspace.FindModFolder(item.Record);
        if (string.IsNullOrEmpty(folder))
        {
            _ui.Toast(item.Record.IsLegacyPak ? "pak 模组没有独立文件夹" : "找不到模组目录", false);
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
                _ui.Toast($"{item.Name}：已{(item.Enabled ? "启用" : "停用")}（未设置游戏目录）", true);
                return;
            }

            var report = await _ui.DeployAsync(silent: true);
            var ok = report is { Failed: 0 };
            _ui.Toast($"{item.Name}：{(item.Enabled ? "已启用" : "已停用")}" +
                      (ok ? "" : "，但同步时出现问题"), ok);
        }
        catch (Exception ex)
        {
            _ui.Toast("保存失败：" + ex.Message, false);
        }
    }

    private void Delete(ModItemViewModel? item)
    {
        if (item == null) return;

        var message = $"把「{item.Name}」从模组库移除？\n\n" +
                      $"文件（{item.SizeText}）会被移到 MODS_disabled，随时可以重新收编，不会删除。\n" +
                      $"只是不想让它出现在这套配置里的话，点行尾的「移出配置」就够了。\n\n" +
                      "如果确定不再需要，可以点「永久删除…」一并删除磁盘文件。";

        var result = _ui.Confirm("移除模组", message, "移出模组库", "永久删除…");
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
            _ui.Toast("已移出模组库，文件在 MODS_disabled 里", true);
        }
        catch (Exception ex)
        {
            _ui.Toast("移出失败：" + ex.Message, false);
        }
    }

    private async Task PermanentDeleteAsync(ModItemViewModel item)
    {
        var confirm = _ui.Confirm("永久删除",
            $"「{item.Name}」的磁盘文件会被永久删除，无法通过本程序恢复。\n\n" +
            "会同时删除 MODS 与 MODS_disabled 里的副本。确定继续吗？",
            "永久删除");

        if (confirm != ConfirmResult.Primary) return;

        try
        {
            _workspace.Deployment.DeleteFiles(_workspace.Game.GamePath, item.Record);
            _workspace.Library.Remove(item.Record);
            _workspace.Library.Save();
            _workspace.NotifyLibraryChanged();
            _ui.Toast("已永久删除：" + item.Name, true);
        }
        catch (Exception ex)
        {
            _ui.Toast("删除失败：" + ex.Message, false);
        }
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

        _ui.Toast($"已{(enabled ? "启用" : "停用")} {selected.Count} 个模组", true);
        ExitBatch();
    }

    private async Task BatchDeleteAsync()
    {
        var selected = SelectedItems;
        if (selected.Count == 0)
        {
            _ui.Toast("先选中模组", false);
            return;
        }

        var total = selected.Sum(i => i.Record.Size);
        var names = string.Join("\n", selected.Take(6).Select(i => "  · " + i.Name));
        if (selected.Count > 6) names += $"\n  … 另有 {selected.Count - 6} 个";

        var confirm = _ui.Confirm($"永久删除 {selected.Count} 个模组",
            $"将永久删除约 {Humanize.Size(total)} 的文件，无法恢复。\n\n{names}\n\n" +
            "会同时删除 MODS 与 MODS_disabled 里的目录。",
            "永久删除");

        if (confirm != ConfirmResult.Primary) return;

        foreach (var item in selected)
        {
            _workspace.Deployment.DeleteFiles(_workspace.Game.GamePath, item.Record);
            _workspace.Library.Remove(item.Record);
        }

        _workspace.Library.Save();
        _workspace.NotifyLibraryChanged();
        _ui.Toast($"已删除 {selected.Count} 个模组", true);
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
        _ui.Toast((toTop ? "已移到顶部：" : "已移到底部：") + selected.Count + " 个模组", true);
        return Task.CompletedTask;
    }

    private void ClearSelectionCore()
    {
        foreach (var item in Items) item.IsSelected = false;
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

        var name = DialogService.Prompt("新建配置",
            "把当前这套模组（含顺序和启停状态）复制成一套新配置：",
            _workspace.Library.NextProfileName());
        if (string.IsNullOrWhiteSpace(name)) return;

        if (_workspace.Library.ProfileNameExists(name))
        {
            _ui.Toast("已经有同名配置了", false);
            return;
        }

        var profile = _workspace.Library.CreateProfile(name, copyActive: true);
        _workspace.Library.SetActiveProfile(profile.Id);
        _workspace.Library.Save();

        SyncFromLibrary();
        _ui.Toast($"已新建「{profile.Name}」，复制了 {profile.Entries.Count} 个模组", true);
    }

    private async Task SwitchProfileAsync(ModProfile? profile)
    {
        if (profile == null) return;

        var missing = _workspace.Library.MissingProfileEntries(profile);
        var changed = _workspace.Library.SetActiveProfile(profile.Id);

        SyncFromLibrary();
        _workspace.Library.Save();
        IsProfileMenuOpen = false;

        if (_workspace.Game.IsValid) await _ui.DeployAsync(silent: true);

        var text = $"已切换到「{profile.Name}」";
        text += changed > 0 ? $"，{changed} 个模组状态改变" : "，状态没有变化";
        if (missing > 0) text += $"；{missing} 个模组已不在库中";

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

        var name = DialogService.Prompt($"重命名配置「{profile.Name}」", "新名字：", profile.Name);
        if (string.IsNullOrWhiteSpace(name) || name == profile.Name) return;

        if (_workspace.Library.ProfileNameExists(name, profile))
        {
            _ui.Toast("已经有同名配置了", false);
            return;
        }

        _workspace.Library.RenameProfile(profile, name);
        _workspace.Library.Save();
        RefreshProfiles();
        Raise(nameof(ActiveProfileName), nameof(CountText));
        _ui.Toast("已重命名为「" + name + "」", true);
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

        var confirm = _ui.Confirm($"删除配置「{profile.Name}」",
            "只删除这套配置，模组本身和文件都不动——它们会回到模组库里。", "删除");
        if (confirm != ConfirmResult.Primary) return;

        _workspace.Library.RemoveProfile(profile);
        _workspace.Library.Save();
        SyncFromLibrary();
        _ui.Toast($"已删除「{profile.Name}」，当前配置：{ActiveProfileName}", true);
    }

    private void ApplyOrder()
    {
        if (!_workspace.Game.IsValid)
        {
            _ui.Alert("应用加载顺序", "还没有设置游戏目录。");
            return;
        }

        var confirm = _ui.Confirm("应用加载顺序",
            "将把当前配置里的模组顺序写入游戏设置 GCMODSETTINGS.MXML 的 ModPriority。\n" +
            "列表越靠上，优先级越高（冲突时覆盖靠下的模组）。原文件会自动备份为 .bak。\n\n继续吗？",
            "写入顺序");

        if (confirm != ConfirmResult.Primary) return;

        var names = _workspace.Library.ActiveMods
            .Select(m => LoadOrderService.ToXmlName(m.EffectiveFolder))
            .ToList();

        var result = _workspace.LoadOrder.Apply(_workspace.Game.GamePath, names);

        if (!result.Ok)
        {
            _ui.Alert("应用加载顺序",
                result.Error ?? "GCMODSETTINGS.MXML 里没有匹配的模组条目。先进一次游戏让它生成。");
            return;
        }

        var text = $"已写入 {result.Updated} 个模组的 ModPriority";
        if (result.Missing > 0) text += $"，{result.Missing} 个在 XML 里找不到（可能还没被游戏扫描到）";
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
        SyncFromLibrary();

        if (_workspace.Game.IsValid) await _ui.DeployAsync(silent: true);

        _ui.Toast($"{item.Name}：已移出配置，回到模组库", true);
    }

    public async Task RefreshHealthAsync()
    {
        var gameVersion = _workspace.Settings.GameVersion;
        var items = _cache.Values.ToList();

        var results = await Task.Run(() => items
            .Select(item => (Item: item, Health: ModInspector.Evaluate(
                item.Record, _workspace.FindModFolder(item.Record), gameVersion)))
            .ToList());

        foreach (var (item, health) in results) item.ApplyHealth(health);

        RaiseCounts();
        View.Refresh();
    }
}

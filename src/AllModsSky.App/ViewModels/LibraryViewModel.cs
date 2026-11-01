using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using AllModsSky.App.Mvvm;
using AllModsSky.App.Services;
using AllModsSky.Core;

namespace AllModsSky.App.ViewModels;

public sealed class LibraryViewModel : ObservableObject
{
    private readonly Workspace _workspace;
    private readonly IShellUi _ui;

    private string _searchText = "";
    private string _filter = "All";
    private bool _showExternal;
    private bool _batchMode;

    public LibraryViewModel(Workspace workspace, IShellUi ui)
    {
        _workspace = workspace;
        _ui = ui;

        Items = new ObservableCollection<ModItemViewModel>();
        External = new ObservableCollection<ExternalModItemViewModel>();
        View = CollectionViewSource.GetDefaultView(Items);
        View.Filter = FilterItem;

        ImportFilesCommand = new RelayCommand(ImportFiles);
        ImportFolderCommand = new RelayCommand(ImportFolder);
        RefreshCommand = new AsyncRelayCommand(() => _ui.RefreshAllAsync());
        DeployCommand = new AsyncRelayCommand(() => _ui.DeployAsync(false));
        OpenModsFolderCommand = new RelayCommand(() => Launcher.OpenFolder(_workspace.ModsDir));
        ToggleExternalPanelCommand = new AsyncRelayCommand(ToggleExternalPanelAsync);
        RefreshExternalCommand = new AsyncRelayCommand(RefreshExternalAsync);
        AdoptExternalCommand = new AsyncRelayCommand(p => AdoptAsync(p as ExternalModItemViewModel));
        ToggleExternalItemCommand = new AsyncRelayCommand(p => ToggleExternalAsync(p as ExternalModItemViewModel));
        OpenFolderCommand = new RelayCommand(p => OpenFolder(p as ModItemViewModel));
        DeleteCommand = new RelayCommand(p => Delete(p as ModItemViewModel));
        MoveTopCommand = new AsyncRelayCommand(() => MoveSelectedAsync(true));
        MoveBottomCommand = new AsyncRelayCommand(() => MoveSelectedAsync(false));
        BatchEnableCommand = new AsyncRelayCommand(() => SetSelectedEnabledAsync(true));
        BatchDisableCommand = new AsyncRelayCommand(() => SetSelectedEnabledAsync(false));
        BatchDeleteCommand = new AsyncRelayCommand(BatchDeleteAsync);
        ClearSelectionCommand = new RelayCommand(ExitBatch);
        ToggleBatchCommand = new RelayCommand(() => BatchMode = !BatchMode);
        ApplyOrderCommand = new RelayCommand(ApplyOrder);

        _workspace.LibraryChanged += SyncFromLibrary;
        _workspace.GameChanged += OnGameChanged;
    }

    public ObservableCollection<ModItemViewModel> Items { get; }
    public ICollectionView View { get; }
    public ObservableCollection<ExternalModItemViewModel> External { get; }

    public ICommand ImportFilesCommand { get; }
    public ICommand ImportFolderCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand DeployCommand { get; }
    public ICommand OpenModsFolderCommand { get; }
    public ICommand ToggleExternalPanelCommand { get; }
    public ICommand RefreshExternalCommand { get; }
    public ICommand AdoptExternalCommand { get; }
    public ICommand ToggleExternalItemCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand MoveTopCommand { get; }
    public ICommand MoveBottomCommand { get; }
    public ICommand BatchEnableCommand { get; }
    public ICommand BatchDisableCommand { get; }
    public ICommand BatchDeleteCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand ToggleBatchCommand { get; }
    public ICommand ApplyOrderCommand { get; }

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

    public bool ShowExternal
    {
        get => _showExternal;
        private set => SetProperty(ref _showExternal, value);
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
    public string CountText => $"{TotalCount} 个模组 · 已启用 {EnabledCount}";
    public string SelectedText => $"已选 {SelectedCount} 个";
    public string ExternalButtonText => $"外部模组 ({External.Count})";

    public bool ShowExternalHint => External.Count > 0 && Items.Count == 0;

    public string ExternalHint => ShowExternalHint
        ? $"MODS 里还有 {External.Count} 个未管理的模组：可在下方「外部模组」里直接停用/启用，或点「收编」加入列表。"
        : "";

    public bool GameValid => _workspace.Game.IsValid;
    public string GamePathText => _workspace.Game.IsValid ? _workspace.Game.GamePath : "未设置游戏目录 — 去「设置」里指定";
    public string GameVersionText => _workspace.Game.IsValid ? _workspace.Game.VersionLine : "";
    public string ModsPath => _workspace.Game.ModsPath;
    public bool ModsWritable => _workspace.Game.ModsWritable;
    public string DisableFlagText =>
        _workspace.Game.IsValid && _workspace.Game.DisableModsFileExists ? "DISABLEMODS.TXT 已存在" : "";

    public List<ModItemViewModel> SelectedItems => Items.Where(i => i.IsSelected).ToList();

    public void ReloadFromLibrary()
    {
        foreach (var item in Items) item.PropertyChanged -= Item_PropertyChanged;
        Items.Clear();

        foreach (var record in _workspace.Library.Mods)
        {
            var item = new ModItemViewModel(record, OnItemToggled);
            item.PropertyChanged += Item_PropertyChanged;
            Items.Add(item);
        }

        Reindex();
        View.Refresh();
        RaiseCounts();
    }

    public void SyncFromLibrary()
    {
        var existing = Items.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<ModItemViewModel>();

        foreach (var record in _workspace.Library.Mods)
        {
            if (existing.TryGetValue(record.Id, out var item))
            {
                item.SetEnabledSilently(record.Enabled);
            }
            else
            {
                item = new ModItemViewModel(record, OnItemToggled);
                item.PropertyChanged += Item_PropertyChanged;
            }
            ordered.Add(item);
        }

        foreach (var stale in Items.Except(ordered).ToList())
        {
            stale.PropertyChanged -= Item_PropertyChanged;
            Items.Remove(stale);
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var item = ordered[i];
            var current = Items.IndexOf(item);
            if (current < 0) Items.Insert(i, item);
            else if (current != i) Items.Move(current, i);
        }

        Reindex();
        View.Refresh();
        RaiseCounts();
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
        Raise(nameof(TotalCount), nameof(EnabledCount), nameof(SelectedCount), nameof(HasSelection),
            nameof(CountText), nameof(SelectedText), nameof(IsLibraryEmpty), nameof(ExternalButtonText),
            nameof(GameValid), nameof(GamePathText), nameof(GameVersionText), nameof(ModsPath),
            nameof(ModsWritable), nameof(DisableFlagText), nameof(HasVisibleMods),
            nameof(ShowBatchBar), nameof(BatchButtonText));
    }

    private void OnGameChanged()
    {
        Raise(nameof(GameValid), nameof(GamePathText), nameof(GameVersionText), nameof(ModsPath),
            nameof(ModsWritable), nameof(DisableFlagText));
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

        var message = $"把「{item.Name}」从列表移除？\n\n" +
                      $"文件（{item.SizeText}）会被移到 MODS_disabled，随时可以重新收编，不会删除。\n" +
                      $"如果确定不再需要，可以点「永久删除…」一并删除磁盘文件。";

        var result = _ui.Confirm("移除模组", message, "移出列表", "永久删除…");
        if (result == ConfirmResult.Primary) _ = SoftRemoveAsync(item);
        else if (result == ConfirmResult.Danger) _ = PermanentDeleteAsync(item);
    }

    private async Task SoftRemoveAsync(ModItemViewModel item)
    {
        try
        {
            item.SetEnabledSilently(false);
            _workspace.Library.Save();

            if (_workspace.Game.IsValid)
                await _ui.DeployAsync(silent: true);

            _workspace.Library.Remove(item.Record);
            _workspace.Library.Save();
            _workspace.NotifyLibraryChanged();
            await RefreshExternalAsync();
            _ui.Toast("已移出列表，文件在 MODS_disabled 里", true);
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
            await RefreshExternalAsync();
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
        await RefreshExternalAsync();
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

        _workspace.Library.MoveMany(selected.Select(i => i.Record).ToList(), toTop);
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
        var list = _workspace.Library.Mods.ToList();
        var from = list.IndexOf(source.Record);
        if (from < 0) return;

        int to;
        if (target == null)
        {
            to = list.Count - 1;
        }
        else
        {
            to = list.IndexOf(target.Record);
            if (to < 0) return;
            if (after) to++;
            if (from < to) to--;
        }

        to = Math.Clamp(to, 0, list.Count - 1);
        if (from == to) return;

        list.RemoveAt(from);
        list.Insert(to, source.Record);

        _workspace.Library.ReplaceAll(list);
        _workspace.Library.Save();
        SyncFromLibrary();
        _ui.Toast("顺序已更新", true);
    }

    private void ApplyOrder()
    {
        if (!_workspace.Game.IsValid)
        {
            _ui.Alert("应用加载顺序", "还没有设置游戏目录。");
            return;
        }

        var confirm = _ui.Confirm("应用加载顺序",
            "将把当前列表顺序写入游戏设置 GCMODSETTINGS.MXML 的 ModPriority。\n" +
            "列表越靠上，优先级越高（冲突时覆盖靠下的模组）。原文件会自动备份为 .bak。\n\n继续吗？",
            "写入顺序");

        if (confirm != ConfirmResult.Primary) return;

        var names = _workspace.Library.Mods
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
        else if (Items.Count == 0) ShowExternal = true; // 库为空时自动展开，让用户看到现有模组

        Raise(nameof(ShowExternal), nameof(ExternalHint), nameof(ShowExternalHint));
    }

    private async Task ToggleExternalAsync(ExternalModItemViewModel? item)
    {
        if (item == null || !_workspace.Game.IsValid) return;

        try
        {
            await Task.Run(() => _workspace.Deployment.SetExternal(
                _workspace.Game.GamePath, item.Model, !item.Model.Enabled));
            await RefreshExternalAsync();
            _ui.Toast($"{item.Name}：{(item.Model.Enabled ? "已停用" : "已启用")}", true);
        }
        catch (Exception ex)
        {
            _ui.Toast("操作失败：" + ex.Message, false);
        }
    }

    private async Task AdoptAsync(ExternalModItemViewModel? item)
    {
        if (item == null || !_workspace.Game.IsValid) return;

        try
        {
            var record = _workspace.Deployment.Adopt(_workspace.Game.GamePath, item.Model, _workspace.Library.NameExists);
            _workspace.Library.Add(record);
            _workspace.Library.Save();
            _workspace.NotifyLibraryChanged();
            await RefreshExternalAsync();
            await RefreshHealthAsync();
            _ui.Toast("已收编：" + record.Name, true);
        }
        catch (Exception ex)
        {
            _ui.Toast("收编失败：" + ex.Message, false);
        }
    }

    public async Task RefreshHealthAsync()
    {
        var gameVersion = _workspace.Settings.GameVersion;
        var items = Items.ToList();

        var results = await Task.Run(() => items
            .Select(item => (Item: item, Health: ModInspector.Evaluate(
                item.Record, _workspace.FindModFolder(item.Record), gameVersion)))
            .ToList());

        foreach (var (item, health) in results) item.ApplyHealth(health);

        RaiseCounts();
        View.Refresh();
    }
}

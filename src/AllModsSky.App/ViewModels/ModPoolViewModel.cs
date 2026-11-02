using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using AllModsSky.App.Mvvm;
using AllModsSky.App.Services;
using AllModsSky.Core;

namespace AllModsSky.App.ViewModels;

/// <summary>模组库里的一行：池子里的模组 + 它是否已经在当前配置里。</summary>
public sealed class PoolItemViewModel : ObservableObject
{
    private bool _inProfile;
    private string _profileName = "";

    public PoolItemViewModel(ModRecord record) => Record = record;

    public ModRecord Record { get; }
    public string Id => Record.Id;
    public string Name => Record.Name;
    public string MetaText => $"{Record.VersionText} · {Record.SizeText} · {Record.Source}";
    public string FolderText => Record.EffectiveFolder;
    public bool IsLegacyPak => Record.IsLegacyPak;
    public string SizeText => Record.SizeText;

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

    public string StateText => _inProfile ? $"已在「{_profileName}」" : "未加入";
    public string AddText => _inProfile ? "已加入" : "加入配置";
}

/// <summary>
/// 模组库页：整个池子的模组都在这儿（不管在不在当前配置里），
/// 导入、收编外部模组、往配置里加、从库里删，都在这一页做。
/// </summary>
public sealed class ModPoolViewModel : ObservableObject, IActivatable
{
    private readonly Workspace _workspace;
    private readonly IShellUi _ui;

    private string _searchText = "";
    private bool _onlyUnused;
    private bool _showExternal;

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
        AddAllCommand = new AsyncRelayCommand(AddAllAsync);
        OpenFolderCommand = new RelayCommand(p => OpenFolder(p as PoolItemViewModel));
        DeleteCommand = new RelayCommand(p => Delete(p as PoolItemViewModel));
        ToggleUnusedCommand = new RelayCommand(() => OnlyUnused = !OnlyUnused);
        ToggleExternalPanelCommand = new AsyncRelayCommand(ToggleExternalPanelAsync);
        RefreshExternalCommand = new AsyncRelayCommand(RefreshExternalAsync);
        AdoptExternalCommand = new AsyncRelayCommand(p => AdoptAsync(p as ExternalModItemViewModel));
        AdoptAllExternalCommand = new AsyncRelayCommand(AdoptAllAsync);
        ToggleExternalItemCommand = new AsyncRelayCommand(p => ToggleExternalAsync(p as ExternalModItemViewModel));

        _workspace.LibraryChanged += Reload;
    }

    public ObservableCollection<PoolItemViewModel> Items { get; }
    public ICollectionView View { get; }
    public ObservableCollection<ExternalModItemViewModel> External { get; }

    public ICommand ImportFilesCommand { get; }
    public ICommand ImportFolderCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand AddCommand { get; }
    public ICommand AddAllCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ToggleUnusedCommand { get; }
    public ICommand ToggleExternalPanelCommand { get; }
    public ICommand RefreshExternalCommand { get; }
    public ICommand AdoptExternalCommand { get; }
    public ICommand AdoptAllExternalCommand { get; }
    public ICommand ToggleExternalItemCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            View.Refresh();
            Raise(nameof(HasVisibleMods), nameof(IsEmpty));
        }
    }

    /// <summary>只看还没进当前配置的（挑模组时最常用）。</summary>
    public bool OnlyUnused
    {
        get => _onlyUnused;
        set
        {
            if (!SetProperty(ref _onlyUnused, value)) return;
            View.Refresh();
            Raise(nameof(HasVisibleMods), nameof(IsEmpty), nameof(UnusedButtonText));
        }
    }

    public bool ShowExternal
    {
        get => _showExternal;
        private set => SetProperty(ref _showExternal, value);
    }

    public string UnusedButtonText => _onlyUnused ? "只看没加入 ✓" : "只看没加入";

    public int TotalCount => Items.Count;
    public int UsedCount => Items.Count(i => i.InProfile);
    public int UnusedCount => Items.Count(i => !i.InProfile);

    public string CountText =>
        $"共 {TotalCount} 个 · 当前配置「{_workspace.Library.ActiveProfile.Name}」用了 {UsedCount} 个";

    public bool IsEmpty => Items.Count == 0;
    public bool HasVisibleMods => !View.IsEmpty;
    public string ExternalButtonText => $"外部模组 ({External.Count})";

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

    private async Task AddAllAsync()
    {
        var pending = Items.Where(i => !i.InProfile).Select(i => i.Record).ToList();
        if (pending.Count == 0)
        {
            _ui.Toast("都在当前配置里了", false);
            return;
        }

        await AddRecordsAsync(pending);
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

        _ui.Toast($"已加入 {added} 个模组到「{profile.Name}」", true);
    }

    private void Delete(PoolItemViewModel? item)
    {
        if (item == null) return;

        var message = $"把「{item.Name}」从模组库移除？\n\n" +
                      $"文件（{item.SizeText}）会被移到 MODS_disabled，随时可以重新收编，不会删除。\n" +
                      "如果确定不再需要，可以点「永久删除…」一并删除磁盘文件。";

        var result = _ui.Confirm("移除模组", message, "移出模组库", "永久删除…");
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
            _ui.Toast("移出失败：" + ex.Message, false);
        }
    }

    private async Task PermanentDeleteAsync(PoolItemViewModel item)
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
            var (added, profile) = _workspace.Library.AddToActiveProfile(new[] { record });
            _workspace.Library.Save();
            _workspace.NotifyLibraryChanged();
            await RefreshExternalAsync();
            _ui.Toast(added > 0
                ? $"已收编并加入「{profile.Name}」：{record.Name}"
                : $"已收编到模组库：{record.Name}（当前是「无模组」，新建配置后再加）", true);
        }
        catch (Exception ex)
        {
            _ui.Toast("收编失败：" + ex.Message, false);
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

        var confirm = _ui.Confirm($"全部收编（{pending.Count} 个）",
            "把它们都登记进模组库，并加入当前配置，之后就能统一启停。\n\n" +
            "文件不会被删除；MODS 根目录里散放的 pak 会收进同名文件夹，方便启停。",
            "全部收编");
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
                ? $"已收编 {adopted.Count} 个模组，{left} 个留在模组库（当前是「无模组」）"
                : $"已收编 {adopted.Count} 个模组", true);
            return;
        }

        var sample = string.Join("、", failed.Take(3));
        if (failed.Count > 3) sample += "…";
        _ui.Toast(adopted.Count > 0
            ? $"已收编 {adopted.Count} 个，{failed.Count} 个失败（{sample}）"
            : $"收编失败：{sample}", false);
    }
}

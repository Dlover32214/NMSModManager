using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using AllModsSky.App.Mvvm;
using AllModsSky.App.Services;
using AllModsSky.Core;

namespace AllModsSky.App.ViewModels;

public sealed class SourceCardViewModel : ObservableObject
{
    private ImageSource? _cover;

    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string CoverUrl { get; init; } = "";
    public object Payload { get; init; } = null!;

    public ImageSource? Cover
    {
        get => _cover;
        set => SetProperty(ref _cover, value);
    }
}

public sealed class NexusCategoryItem
{
    public string Id { get; init; } = "0";
    public string Name { get; init; } = "全部";
}

public sealed class DiscoverViewModel : ObservableObject, IActivatable
{
    private const int PageSize = 12;

    private readonly Workspace _workspace;
    private readonly IShellUi _ui;

    private string _sourceKey = "nexus";
    private string _nexusSort = "trending";
    private string _nexusQuery = "";
    private string _oogcCategory = "3";
    private string _oogcSort = "latest";
    private string _oogcCost = "all";
    private string _oogcQuery = "";
    private NexusCategoryItem? _selectedNexusCategory;

    private bool _isLoading;
    private string _status = "";
    private string _error = "";
    private bool _hasError;
    private int _page = 1;
    private int _maxPage = 1;

    private bool _detailOpen;
    private string _detailTitle = "";
    private string _detailMeta = "";
    private string _detailBody = "";
    private ImageSource? _detailCover;
    private string _selectedUrl = "";

    private bool _loadedNexus;
    private bool _loadedOogc;
    private bool _suppressCategoryReload;
    private int _loadToken;
    private List<NexusModInfo> _nexusMods = new();

    public DiscoverViewModel(Workspace workspace, IShellUi ui)
    {
        _workspace = workspace;
        _ui = ui;

        NexusCategories.Add(new NexusCategoryItem { Id = "0", Name = "全部" });
        _selectedNexusCategory = NexusCategories[0];

        SearchCommand = new AsyncRelayCommand(SearchAsync);
        RetryCommand = new AsyncRelayCommand(() => ForceReloadAsync());
        RandomNexusCommand = new AsyncRelayCommand(RandomNexusAsync);
        BackCommand = new RelayCommand(CloseDetail);
        OpenPageCommand = new RelayCommand(() => Launcher.OpenUrl(SelectedUrl));
        CopyLinkCommand = new RelayCommand(CopyLink);
        OpenNexusSiteCommand = new RelayCommand(() =>
            Launcher.OpenUrl(NexusClient.SiteBase + "/mods?sort=trending"));
        PrevPageCommand = new RelayCommand(() => _ = GoPageAsync(-1));
        NextPageCommand = new RelayCommand(() => _ = GoPageAsync(1));
        OpenCardCommand = new AsyncRelayCommand(p => OpenDetailAsync(p as SourceCardViewModel));

        _workspace.SettingsChanged += OnSettingsChanged;
    }

    public ObservableCollection<SourceCardViewModel> Cards { get; } = new();
    public ObservableCollection<NexusCategoryItem> NexusCategories { get; } = new();

    /// <summary>骨架卡占位数量；用于首次加载时撑住与真实卡片相同的布局。</summary>
    public IReadOnlyList<int> SkeletonItems { get; } = Enumerable.Range(0, 8).ToArray();

    public ICommand SearchCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand RandomNexusCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand OpenPageCommand { get; }
    public ICommand CopyLinkCommand { get; }
    public ICommand OpenNexusSiteCommand { get; }
    public ICommand PrevPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand OpenCardCommand { get; }

    public string SourceKey
    {
        get => _sourceKey;
        set
        {
            if (!SetProperty(ref _sourceKey, value)) return;
            Raise(nameof(IsNexus), nameof(IsOogc));
            _ = EnsureLoadedAsync();
        }
    }

    public bool IsNexus => SourceKey == "nexus";
    public bool IsOogc => SourceKey == "oogc";

    public string NexusSort
    {
        get => _nexusSort;
        set
        {
            if (!SetProperty(ref _nexusSort, value)) return;
            Page = 1;
            _ = LoadNexusAsync();
        }
    }

    public string NexusQuery
    {
        get => _nexusQuery;
        set => SetProperty(ref _nexusQuery, value);
    }

    public string OogcCategory
    {
        get => _oogcCategory;
        set
        {
            if (!SetProperty(ref _oogcCategory, value)) return;
            Page = 1;
            _ = LoadOogcAsync();
        }
    }

    public string OogcSort
    {
        get => _oogcSort;
        set
        {
            if (!SetProperty(ref _oogcSort, value)) return;
            Page = 1;
            _ = LoadOogcAsync();
        }
    }

    public string OogcCost
    {
        get => _oogcCost;
        set
        {
            if (!SetProperty(ref _oogcCost, value)) return;
            Page = 1;
            _ = LoadOogcAsync();
        }
    }

    public string OogcQuery
    {
        get => _oogcQuery;
        set => SetProperty(ref _oogcQuery, value);
    }

    public NexusCategoryItem? SelectedNexusCategory
    {
        get => _selectedNexusCategory;
        set
        {
            if (!SetProperty(ref _selectedNexusCategory, value)) return;
            if (_suppressCategoryReload) return;
            Page = 1;
            _ = LoadNexusAsync();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
                Raise(nameof(ShowEmptyState), nameof(ShowSkeleton), nameof(ShowBusyOverlay), nameof(ShowPager));
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (SetProperty(ref _hasError, value))
                Raise(nameof(ShowEmptyState), nameof(ShowSkeleton), nameof(ShowBusyOverlay));
        }
    }

    public int Page
    {
        get => _page;
        private set
        {
            if (!SetProperty(ref _page, value)) return;
            Raise(nameof(PageText), nameof(CanGoPrev), nameof(CanGoNext));
        }
    }

    public int MaxPage
    {
        get => _maxPage;
        private set
        {
            if (!SetProperty(ref _maxPage, value)) return;
            Raise(nameof(ShowPager), nameof(PageText), nameof(CanGoNext));
        }
    }

    public string PageText => $"{Page} / {MaxPage}";
    public bool CanGoPrev => Page > 1;
    public bool CanGoNext => Page < MaxPage;
    public bool ShowPager => MaxPage > 1;
    public bool ShowSkeleton => IsLoading && Cards.Count == 0;
    public bool ShowBusyOverlay => IsLoading && Cards.Count > 0;
    public bool ShowEmptyState => !IsLoading && !HasError && Cards.Count == 0;

    public bool DetailOpen
    {
        get => _detailOpen;
        private set => SetProperty(ref _detailOpen, value);
    }

    public string DetailTitle
    {
        get => _detailTitle;
        private set => SetProperty(ref _detailTitle, value);
    }

    public string DetailMeta
    {
        get => _detailMeta;
        private set => SetProperty(ref _detailMeta, value);
    }

    public string DetailBody
    {
        get => _detailBody;
        private set => SetProperty(ref _detailBody, value);
    }

    public ImageSource? DetailCover
    {
        get => _detailCover;
        private set => SetProperty(ref _detailCover, value);
    }

    public string SelectedUrl
    {
        get => _selectedUrl;
        private set => SetProperty(ref _selectedUrl, value);
    }

    public async Task EnsureLoadedAsync()
    {
        if (IsNexus && !_loadedNexus) await LoadNexusAsync();
        else if (IsOogc && !_loadedOogc) await LoadOogcAsync();
    }

    public Task OnActivatedAsync() => EnsureLoadedAsync();

    private async Task ForceReloadAsync()
    {
        if (IsNexus) await LoadNexusAsync(force: true);
        else await LoadOogcAsync();
    }

    /// <summary>从 Nexus 当前可获取的模组池中随机抽 10 个；没有官方随机接口。</summary>
    private async Task RandomNexusAsync()
    {
        if (!IsNexus) return;

        var key = _workspace.Settings.NexusApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            _ui.Toast("随机抽签需要先填写 Nexus API Key", false);
            return;
        }

        var token = ++_loadToken;
        IsLoading = true;
        HasError = false;
        Error = "";
        Status = "正在从 Nexus 抽签…";

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var (mods, error) = await _workspace.Nexus.GetAllAsync(key, timeout.Token);
            if (token != _loadToken) return;

            if (mods.Count == 0)
            {
                SetError(error ?? "Nexus 没有返回可抽取的模组");
                return;
            }

            var pool = mods.ToList();
            for (var i = pool.Count - 1; i > 0; i--)
            {
                var j = Random.Shared.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            var picked = pool.Take(Math.Min(10, pool.Count)).ToList();
            var categoryNames = NexusCategories.ToDictionary(
                c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);

            Cards.Clear();
            foreach (var mod in picked)
            {
                var category = mod.CategoryId != null && categoryNames.TryGetValue(mod.CategoryId, out var name)
                    ? name
                    : "";

                Cards.Add(new SourceCardViewModel
                {
                    Title = mod.Name,
                    Subtitle = (string.IsNullOrEmpty(category) ? "" : category + " · ") +
                               "v" + mod.Version + " · " + mod.Author,
                    CoverUrl = mod.CoverUrl,
                    Payload = mod
                });
            }

            Page = 1;
            MaxPage = 1;
            Status = $"随机抽到 {picked.Count} 个模组（再抽一次可换一批）";
            HasError = false;
            _loadedNexus = true;
            Raise(nameof(ShowEmptyState), nameof(ShowPager), nameof(CanGoPrev), nameof(CanGoNext));
            _ = LoadCoversAsync(Cards.ToList(), null);
        }
        catch (OperationCanceledException)
        {
            if (token == _loadToken) SetError("抽签超时（30 秒），请检查网络后重试。");
        }
        catch (Exception ex)
        {
            if (token == _loadToken) SetError(ex.Message);
        }
        finally
        {
            if (token == _loadToken) IsLoading = false;
        }
    }

    private async Task SearchAsync()
    {
        Page = 1;
        if (IsNexus) await LoadNexusAsync(force: true);
        else await LoadOogcAsync();
    }

    private async Task GoPageAsync(int delta)
    {
        var target = Math.Clamp(Page + delta, 1, Math.Max(1, MaxPage));
        if (target == Page) return;

        Page = target;
        if (IsNexus) await LoadNexusAsync(keepPage: true);
        else await LoadOogcAsync();
    }

    private async Task LoadNexusAsync(bool force = false, bool keepPage = false)
    {
        var token = ++_loadToken;
        var key = _workspace.Settings.NexusApiKey;

        if (string.IsNullOrWhiteSpace(key))
        {
            SetError("还没有填写 Nexus API Key。到「设置 → Nexus Mods」填入并保存即可浏览列表。");
            _loadedNexus = false;
            return;
        }

        IsLoading = true;
        HasError = false;
        Error = "";
        Status = "正在加载 Nexus…";
        if (!keepPage) Page = 1;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var cancellation = timeout.Token;

        try
        {
            if (NexusCategories.Count <= 1)
            {
                var (categories, categoryError) = await _workspace.Nexus.GetCategoriesAsync(key, cancellation);
                if (token != _loadToken) return;

                if (categoryError == null && categories.Count > 0)
                {
                    _suppressCategoryReload = true;
                    try
                    {
                        foreach (var category in categories)
                            NexusCategories.Add(new NexusCategoryItem { Id = category.Id, Name = category.Name });

                        var current = SelectedNexusCategory?.Id ?? "0";
                        SelectedNexusCategory =
                            NexusCategories.FirstOrDefault(c => c.Id == current) ?? NexusCategories[0];
                    }
                    finally
                    {
                        _suppressCategoryReload = false;
                    }
                }
            }

            var filteredByCategory = SelectedNexusCategory != null && SelectedNexusCategory.Id != "0";
            var searching = !string.IsNullOrWhiteSpace(NexusQuery);
            var needAll = filteredByCategory || searching || NexusSort == "all";

            var (mods, error) = needAll
                ? await _workspace.Nexus.GetAllAsync(key, cancellation)
                : await _workspace.Nexus.GetModsAsync(NexusSort, key, cancellation);

            if (token != _loadToken) return;

            if (error != null && mods.Count == 0)
            {
                SetError(error);
                _loadedNexus = true;
                return;
            }

            IEnumerable<NexusModInfo> query = mods;
            if (filteredByCategory)
                query = query.Where(m => m.CategoryId == SelectedNexusCategory!.Id);
            if (searching)
            {
                var q = NexusQuery.Trim();
                query = query.Where(m =>
                    m.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    m.Summary.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    m.Author.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            _nexusMods = query.ToList();

            var maxPage = Math.Max(1, (int)Math.Ceiling(_nexusMods.Count / (double)PageSize));
            MaxPage = maxPage;
            if (Page > MaxPage) Page = MaxPage;
            if (!keepPage && Page > 1) Page = 1;

            var categoryNames = NexusCategories.ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
            var slice = _nexusMods.Skip((Page - 1) * PageSize).Take(PageSize).ToList();

            Cards.Clear();
            foreach (var mod in slice)
            {
                var category = mod.CategoryId != null && categoryNames.TryGetValue(mod.CategoryId, out var name)
                    ? name
                    : "";
                Cards.Add(new SourceCardViewModel
                {
                    Title = mod.Name,
                    Subtitle = (string.IsNullOrEmpty(category) ? "" : category + " · ") + "v" + mod.Version + " · " + mod.Author,
                    CoverUrl = mod.CoverUrl,
                    Payload = mod
                });
            }

            Status = _nexusMods.Count == 0
                ? "这个分类/关键词下没有结果"
                : $"{_nexusMods.Count} 个模组" +
                  (_workspace.Nexus.TotalMods > 0 ? $" · 全站 {_workspace.Nexus.TotalMods} 个" : "");

            HasError = false;
            _loadedNexus = true;
            Raise(nameof(ShowEmptyState), nameof(ShowPager), nameof(CanGoPrev));

            // 卡片先出现，封面在后台继续加载，避免骨架一直挂到图片下载完。
            _ = LoadCoversAsync(Cards.ToList(), null);
        }
        catch (OperationCanceledException)
        {
            if (token == _loadToken) SetError("加载超时（25 秒），请检查网络后重试。");
        }
        catch (Exception ex)
        {
            if (token == _loadToken) SetError(ex.Message);
        }
        finally
        {
            if (token == _loadToken) IsLoading = false;
        }
    }

    private async Task LoadOogcAsync()
    {
        var token = ++_loadToken;
        IsLoading = true;
        HasError = false;
        Error = "";
        Status = "正在加载 oogc.cc…";

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var cancellation = timeout.Token;

        try
        {
            var cost = OogcCost == "all" ? "" : OogcCost;
            var (page, error) = await _workspace.Oogc.ListAsync(
                Page, OogcCategory, OogcSort, cost, OogcQuery, cancellation);

            if (token != _loadToken) return;

            if (page == null)
            {
                SetError(error ?? "读取失败");
                _loadedOogc = true;
                return;
            }

            MaxPage = Math.Max(page.MaxPage, Page);
            Cards.Clear();
            foreach (var item in page.Items)
            {
                Cards.Add(new SourceCardViewModel
                {
                    Title = item.Title,
                    Subtitle = item.Subtitle,
                    CoverUrl = item.CoverUrl,
                    Payload = item
                });
            }

            Status = $"{Cards.Count} 个资源";
            HasError = false;
            _loadedOogc = true;
            Raise(nameof(ShowEmptyState), nameof(ShowPager), nameof(CanGoPrev));

            _ = LoadCoversAsync(Cards.ToList(), OogcClient.BaseUrl);
        }
        catch (OperationCanceledException)
        {
            if (token == _loadToken) SetError("加载超时（25 秒），请检查网络后重试。");
        }
        catch (Exception ex)
        {
            if (token == _loadToken) SetError(ex.Message);
        }
        finally
        {
            if (token == _loadToken) IsLoading = false;
        }
    }

    private async Task LoadCoversAsync(IReadOnlyList<SourceCardViewModel> cards, string? referer)
    {
        try
        {
            var tasks = cards
                .Where(c => !string.IsNullOrEmpty(c.CoverUrl))
                .Select(async card => (Card: card,
                    Image: await _workspace.Images.LoadAsync(card.CoverUrl, 320, referer)))
                .ToList();

            var results = await Task.WhenAll(tasks);
            foreach (var (card, image) in results)
                if (image != null) card.Cover = image;
        }
        catch
        {
            // 封面失败不影响卡片本身
        }
    }

    private void SetError(string message)
    {
        Error = message;
        HasError = true;
        Status = "";
        Cards.Clear();
        IsLoading = false;
        Raise(nameof(ShowEmptyState), nameof(ShowSkeleton), nameof(ShowBusyOverlay));
    }

    private async Task OpenDetailAsync(SourceCardViewModel? card)
    {
        if (card == null) return;

        DetailOpen = true;
        DetailTitle = card.Title;
        DetailMeta = "";
        DetailBody = "正在读取详情…";
        DetailCover = card.Cover;
        SelectedUrl = "";

        if (card.Payload is OogcResource resource)
        {
            SelectedUrl = resource.Url;
            DetailMeta = "oogc.cc 资源中心 · 作品 ID " + resource.Fid;

            var (detail, error) = await _workspace.Oogc.GetDetailAsync(resource.Fid);
            if (detail == null)
            {
                DetailBody = "读取详情失败：" + error;
            }
            else
            {
                DetailMeta = "oogc.cc · " + (string.IsNullOrEmpty(detail.Author) ? "未知作者" : detail.Author)
                             + (string.IsNullOrEmpty(detail.DateText) ? "" : " · " + detail.DateText);
                DetailBody = BuildOogcDetail(detail);

                if (DetailCover == null && !string.IsNullOrEmpty(detail.CoverUrl))
                    DetailCover = await _workspace.Images.LoadAsync(detail.CoverUrl, 1200, OogcClient.BaseUrl);
            }

            return;
        }

        if (card.Payload is NexusModInfo mod)
        {
            SelectedUrl = mod.PageUrl;
            DetailMeta = $"Nexus · 作者 {mod.Author} · 版本 {mod.Version}";

            var (files, error) = await _workspace.Nexus.GetFilesAsync(
                mod.ModId, _workspace.Settings.NexusApiKey);

            DetailBody = BuildNexusDetail(mod, files, error);
        }
    }

    private static string BuildNexusDetail(NexusModInfo mod, IReadOnlyList<NexusFileInfo> files, string? error)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(mod.Summary)) lines.Add(mod.Summary.Trim());

        lines.Add("");
        if (files.Count > 0)
        {
            lines.Add("文件：");
            foreach (var file in files.Take(20))
                lines.Add($"· {file.Name}   v{file.Version}   {file.SizeKb} KB");
        }
        else
        {
            lines.Add("文件列表读取失败：" + (error ?? "接口没有返回文件"));
        }

        lines.Add("");
        lines.Add("Nexus 不提供第三方直链，点「在浏览器中打开」查看页面，登录后手动下载，再把文件拖回本窗口即可导入。");
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildOogcDetail(OogcDetail detail)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(detail.Description))
        {
            lines.Add(detail.Description.Trim());
            lines.Add("");
        }

        if (!string.IsNullOrEmpty(detail.Size)) lines.Add("大小：" + detail.Size);
        if (!string.IsNullOrEmpty(detail.Format)) lines.Add("格式：" + detail.Format);
        if (!string.IsNullOrEmpty(detail.Downloads)) lines.Add("下载量：" + detail.Downloads);
        lines.Add("");
        lines.Add("附件：");
        lines.Add(detail.Attachments);
        return string.Join(Environment.NewLine, lines);
    }

    private void CloseDetail()
    {
        DetailOpen = false;
        DetailCover = null;
        SelectedUrl = "";
    }

    private void CopyLink()
    {
        if (string.IsNullOrEmpty(SelectedUrl))
        {
            _ui.Toast("没有可复制的链接", false);
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(SelectedUrl);
            _ui.Toast("已复制链接", true);
        }
        catch (Exception ex)
        {
            _ui.Toast("复制失败：" + ex.Message, false);
        }
    }

    private void OnSettingsChanged()
    {
        _loadedNexus = false;
    }
}

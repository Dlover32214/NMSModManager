# 架构

两个项目、四个目录、一套 MVVM 契约、一条异常/日志通路。这篇描述它们之间的实际边界。

---

## 1. 分层总览

```
AllModsSky.App (WPF)  ──引用──▶  AllModsSky.Core (无 UI)
```

| 约束 | 现状 |
|---|---|
| App → Core 单向引用 | `AllModsSky.App.csproj` 的 `ProjectReference` 指向 Core，Core 不引用 App |
| Core 不含 WPF 类型 | 全库搜索 `System.Windows` / `PresentationCore` / `Dispatcher` **零命中** |
| Core 仍是 `net10.0-windows` | 因为 `GameLocator` 读注册表（`Microsoft.Win32`），不是因为 UI |

**注意措辞**：Core 是「不依赖 UI」，不是「跨平台」。它有 Windows 平台依赖。

---

## 2. Core 的四层

```
Models  ←  Infrastructure  ←  Integrations  ←  Services
```

箭头是「被依赖」。四条边全部单向，构成严格 DAG。

| 目录 | 文件 | 职责 | 依赖 |
|---|---|---|---|
| `Models/` | `Models.cs` | 纯数据形状，无行为 | 无 |
| `Infrastructure/` | `Infrastructure` / `PathGuard` / `TextLimit` / `VersionParser` / `LegacyDataMigrator` | 与业务无关的底座 | `Models` |
| `Integrations/` | `GameLocator` / `SaveGameReader` / `LogDiagnostics` / `NexusClient` / `OogcClient` / `UpdateService` | 与外部世界打交道（游戏本体、存档、日志、第三方 API、网络） | `Models`、`Infrastructure` |
| `Services/` | `ModLibrary` / `ModInspector` / `Importer` / `DeploymentService` / `ConflictAnalyzer` / `LoadOrderService` / `AmumssService` | 自己的业务动作 | `Models`、`Infrastructure`、`Integrations` |

### 划分标准

> **Services = 只操作自己的库和 MODS 目录；Integrations = 和外部世界打交道。**

`AmumssService` 是这条线最容易判错的一个：它调外部 CLI，看起来像集成。但它依赖 `ModInspector`（Services），且「检测 → 分析 → 重建 → 收集」是一条完整业务链，不是薄客户端。放进 `Integrations/` 会出现 `Integrations → Services` 反向边，和 `DeploymentService → GameLocator` 一起构成目录级循环。

### 依赖方向由编译器保证

`AllModsSky.Core.csproj` **不注入** `AllModsSky.Core.Services` 的全局 using。这意味着 `Models/`、`Infrastructure/`、`Integrations/` 里的文件无法直接引用 Services 类型——想反向依赖就编译不过。

判断某文件依赖哪些层，看 csproj 的 `<Using>` 清单加这张表，不要看文件头的 `using`。

---

## 3. Core 的两种服务形态

### 静态工具类（16 个）

无状态，全静态方法。名字是名词，不带 `Service` 后缀：

`AppPaths` / `JsonStore` / `FileOps` / `Humanize` / `AppLog` / `PathGuard` / `TextLimit` / `VersionParser` / `ConflictAnalyzer` / `Importer` / `ModInspector` / `LogDiagnostics` / `GameLocator` / `SaveGameReader` / `LegacyDataMigrator` / `UpdateService`

### 有状态实例类（6 个）

需要持有状态或需要释放：

| 类型 | 特点 |
|---|---|
| `ModLibrary` | 持有 `_mods` / `_profiles` 两个列表 |
| `DeploymentService` | 部署过程持有进度与报告 |
| `LoadOrderService` | — |
| `AmumssService` | 持有检测到的工具路径 |
| `NexusClient` | `IDisposable`，持有 `HttpClient` |
| `OogcClient` | `IDisposable`，持有 `HttpClient` |

### 没有接口

全项目只有 2 个接口（`IActivatable`、`IShellUi`，都在 `App/Services/ShellContracts.cs`），**不存在 `IXxxService` + `XxxService` 的配对**，也没有任何 mock 点。这是零依赖的代价：没有 DI 容器就不需要接口解耦，验证靠 `.tmp` 探针直接调真实实现。

---

## 4. 组合根与共享状态

没有 DI 容器，没有 Service Locator。组装分两层：

**第一层：`Workspace` 用字段初始化器持有 Core 服务实例**

```csharp
// App/Services/Workspace.cs
public sealed class Workspace : IDisposable
{
    public AppSettings Settings { get; private set; }
    public ModLibrary Library { get; } = new();
    public DeploymentService Deployment { get; } = new();
    public LoadOrderService LoadOrder { get; } = new();
    public AmumssService Amumss { get; } = new();
    public NexusClient Nexus { get; } = new();
    public OogcClient Oogc { get; } = new();
    public ImageService Images { get; } = new();
    public GameInfo Game { get; private set; } = new();
```

`Workspace` 还广播三个事件：`LibraryChanged` / `GameChanged` / `SettingsChanged`。

**第二层：`MainViewModel` 构造函数把 `_workspace` 注入 6 个页面 VM**

```csharp
// App/ViewModels/MainViewModel.cs
public MainViewModel()
{
    Library = new LibraryViewModel(_workspace, this);
    Pool = new ModPoolViewModel(_workspace, this);
    Discover = new DiscoverViewModel(_workspace, this);
    Tools = new ToolsViewModel(_workspace, this);
    SettingsPage = new SettingsViewModel(_workspace, this);
    AboutPage = new AboutViewModel(this);
```

子 VM 构造签名统一为 `(Workspace workspace, IShellUi ui)`，`AboutViewModel` 例外（只要 `IShellUi`）。

**唯一入口**是 `App.xaml.cs`：

```csharp
_shell = new MainViewModel();
var window = new MainWindow { DataContext = _shell };
```

---

## 5. MVVM 契约

### `ObservableObject`

`App/Mvvm/ObservableObject.cs` 全文 25 行，手写实现：

```csharp
protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
protected void Raise([CallerMemberName] string? propertyName = null)
protected void Raise(params string[] propertyNames)
```

标准 setter：

```csharp
public bool IsBusy
{
    get => _isBusy;
    private set => SetProperty(ref _isBusy, value);
}
```

派生属性批量通知用 `Raise(nameof(A), nameof(B))`。

### `ICommand`

`App/Mvvm/RelayCommand.cs` 里两个 `sealed` 类：

| 类型 | 用途 |
|---|---|
| `RelayCommand` | 同步命令 |
| `AsyncRelayCommand` | 异步命令，自带 `_running` 重入保护和异常兜底 |

用法固定：构造函数里 new，属性只读暴露。

```csharp
ImportFilesCommand = new RelayCommand(ImportFiles);
AddModsCommand = new AsyncRelayCommand(AddModsAsync);

public ICommand ImportFilesCommand { get; }
```

`LibraryViewModel` 一个 VM 有 24 个 `ICommand` 属性。

### `IShellUi` —— VM 与 View 的解耦点

`App/Services/ShellContracts.cs` 定义 UI 能力，`MainViewModel` 实现它，子 VM 只持有接口：

```csharp
void Toast(string message, bool success = true);
ConfirmResult Confirm(string title, string message, string primary, string? danger = null);
void Alert(string title, string message);
string? PickFolder(string title, string? initial = null);
IReadOnlyList<string>? PickArchives();
void BeginBusy(string text = "处理中…");
void EndBusy();
void Navigate(PageKey page);
Task<DeployReport?> DeployAsync(bool silent = false);
Task ImportAsync(IReadOnlyList<string> paths);
Task RefreshAllAsync();
```

`Confirm` 返回 `ConfirmResult`（`None` / `Primary` / `Danger` / `Cancel`），破坏性操作走 `Danger` 档。

### 导航

`CurrentPageViewModel` + `DataTemplate`，`MainWindow.xaml` 里 6 个页面级模板按 `DataType` 映射到 View。页面切换时：

```csharp
if (CurrentPageViewModel is IActivatable activatable)
    _ = activatable.OnActivatedAsync();
```

### code-behind 的边界

| 文件 | 行数 | 内容 |
|---|---|---|
| `Views/LibraryView.xaml.cs` | 500 | 整套拖拽排序（`_pressTimer`、`DragGhostAdorner`、`AnimatePositions`）与批量刷选 |
| `MainWindow.xaml.cs` | 266 | 标题栏主题、窗口拖放、快捷键 |
| `Services/ModPickerWindow.xaml.cs` | 126 | 内含 `PickerItem : ObservableObject` |
| 其余 5 个 View | 11–47 | 纯事件转发 |

**只有拖拽和批量刷选突破了 MVVM。** 其余 code-behind 只做事件转发和焦点处理。

---

## 6. 失败如何表达

**Core 不往 UI 抛异常，用 `XxxReport` / `XxxOutcome` 承载失败并返回。**

| 类型 | 位置 |
|---|---|
| `DeployReport` | `Core/Services/DeploymentService.cs` |
| `ExternalMod` | 同上 |
| `ImportOutcome` / `ImportOptions` | `Core/Services/Importer.cs` |
| `LoadOrderReadResult` / `LoadOrderApplyResult` | `Core/Services/LoadOrderService.cs` |
| `AmumssInfo` / `RepairPlan` / `CollectResult` | `Core/Services/AmumssService.cs` |
| `UpdateCheckResult` / `PreparedUpdate` | `Core/Integrations/UpdateService.cs` |
| `ConflictScanResult` | `Core/Services/ConflictAnalyzer.cs` |
| `ModHealth` / `ModFormatInfo` | `Core/Services/ModInspector.cs` |

```csharp
// Core/Services/Importer.cs
public static ImportOutcome Fail(string source, string displayName, string message) =>
    new() { Ok = false, Source = source, DisplayName = displayName, Message = message };
```

```csharp
// Core/Services/DeploymentService.cs
catch (Exception ex)
{
    report.Failed++;
    report.Message("无法访问 MODS 目录：" + ex.Message);
    return report;
}
```

**UI 层负责把结果翻译成提示**：`_ui.Toast(...)` / `_ui.Alert(...)`。

---

## 7. 异常与日志

### 三道兜底

`App.xaml.cs` 启动时挂三个处理器，全部汇到 `WriteCrash`：

| 处理器 | 捕获 |
|---|---|
| `DispatcherUnhandledException` | UI 线程未处理异常 |
| `AppDomain.CurrentDomain.UnhandledException` | 非 UI 线程 |
| `TaskScheduler.UnobservedTaskException` | 未观察的 Task 异常 |

```csharp
private static void WriteCrash(Exception ex, string source)
{
    try { File.AppendAllText(AppPaths.CrashFile, ...); } catch { }
    AppLog.Error(ex, source);
}
```

### `AppLog`

`Core/Infrastructure/Infrastructure.cs:522`，静态类，API 只有三个：`Info` / `Warn` / `Error`。

| 特性 | 实现 |
|---|---|
| 当前日志 | `logs/latest.log` |
| 归档 | 启动时把上次的压成 `logs/yyyy-MM-dd-HHmmss.log.gz` |
| 轮转 | 超 `MaxBytes`（2 MB）触发 |
| 保留 | `KeepArchives` = 5 份 |
| 并发 | `lock (Gate)` 串行化 |
| **失败绝不抛异常** | 内部 `catch` 空吞，注释写明「日志本身不应抛异常」 |

反递归设计：压缩失败时不能走 `AppLog.Warn`，否则递归。

### 网络客户端完全不写日志

`NexusClient` / `OogcClient` / `UpdateService` 里 `AppLog.` 命中数为 **0**。理由是不让 URL 里的 API Key 落进日志文件。

### 三种 catch 风格并存

| 风格 | 典型位置 | 形态 |
|---|---|---|
| A：记日志 + 弹窗 | `MainViewModel` | `AppLog.Error(ex, "同步模组")` + `Alert(...)` |
| B：只 Toast | `LibraryViewModel` / `DiscoverViewModel` | `_ui.Toast("移出失败：" + ex.Message, false)` |
| C：Core 返回结果对象 | `DeploymentService` / `Importer` | 填 `report` 后 `return` |

空 `catch` 共 26 处，多数带一行注释说明为什么可以吞（比如 `PathGuard.Match` 枚举失败只 `Warn` 不抛）。没有注释的空 catch 见 [deviations.md](deviations.md)。

---

## 8. 一条完整数据流

拖入文件 → 部署，经过的层：

```
MainWindow.xaml.cs  Window_Drop
  └─ MainViewModel.ImportAsync(paths)          IShellUi 实现
       └─ Importer.Import(...)                 Core/Services   返回 ImportOutcome
            └─ ModLibrary.Add(...)             Core/Services   改内存态 + 写 library.json
                 └─ DeploymentService.Sync(...) Core/Services  返回 DeployReport
                      └─ PathGuard.*            Core/Infrastructure  枚举定位，不拼路径
       └─ Workspace.NotifyLibraryChanged()     App/Services    广播事件
            └─ 各页面 VM 刷新列表
```

关键点：**Core 全程不弹窗、不碰 UI**，只返回结果对象；UI 层在 `MainViewModel` 里统一翻译成 Toast/Alert。

---

## 9. 不该出现的东西

| 位置 | 禁止 |
|---|---|
| `Core/**` | `MessageBox`、`Dispatcher`、`Window`、任何 `System.Windows.*` |
| `ViewModels/**` | 直接调 `File.*` / `Directory.*`，应走 Core |
| `Views/*.xaml.cs` | 业务逻辑（拖拽除外） |
| `Core/Services/**` | 被 `Infrastructure/` 或 `Integrations/` 引用 |

`MessageBox.Show` 全项目只有 3 处，都在 `App.xaml.cs`（启动失败 / 崩溃 / 已运行）——那时 `IShellUi` 还没建立起来。

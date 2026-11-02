# 现状偏差

一篇对照表：**代码现在的样子** vs **本规范描述的样子**。只陈述，不要求整改——存在即记录，改不改另议。

每条都附证据位置，可以自己去看。

---

## 1. 项目结构与文件组织

| 现状 | 规范怎么说 | 差在哪 |
|---|---|---|
| `MainWindow.xaml` / `.xaml.cs` 在 `src/AllModsSky.App/` 根目录 | 命名空间 = 目录，所有 View 应在 `Views/` | 根目录还留着两个文件，命名空间是 `AllModsSky.App` 而非 `.Views` |
| `Services/DialogWindow.xaml(.cs)`、`Services/ModPickerWindow.xaml(.cs)` 在 `Services/` | `Services/` 只放非 UI 类型 | 两个 `Window` 住在服务目录里，命名空间是 `AllModsSky.App.Services` |
| `Services/ModPickerWindow.xaml.cs:105` 里定义了 `PickerItem : ObservableObject` | ViewModel 应在 `ViewModels/` | 一个 VM 藏在 Services 下的窗口文件里 |
| `Integrations/GameLocator.cs:9` 定义 `SettingsStore` | 一个文件一个职责族 | 设置持久化与游戏定位是两个不相关的职责，共用一个文件 |
| `Models/Models.cs` 一个文件 7 个类型；`DeploymentService.cs` 4 个；`SaveGameReader.cs` 3 个 | 允许同族小类型跟随主类型 | 属允许范围，但文件已偏大（`Models.cs` 225 行） |

---

## 2. C# 风格

| 现状 | 规范怎么说 | 差在哪 |
|---|---|---|
| `ViewModels/LibraryViewModel.cs:370` 的 `private async void OnItemToggled(...)` | `async void` 只允许 `ICommand.Execute` 和事件处理器 | 它是作为 `Action<ModItemViewModel>` 回调传入的，不是事件处理器，可改成 `async Task` |
| `Core/Services/Importer.cs` 有一处 `.Result` | 不用 `.Result` / `.Wait()` | 在 async 方法里阻塞等进程输出，是唯一例外 |
| 空 `catch` 约 31 处 | 空 catch 必须带一行注释说明为什么可以吞 | 多数有注释（如 `PathGuard.Match`），少数没有 |
| `App/ViewModels/ModItemViewModel.cs` 手写 setter，并在 setter 里 `Raise(nameof(...))` | 默认用 `SetProperty` | setter 有副作用（同步 `Record.Enabled` 并触发回调），属合理例外，但 `Raise()` 未显式写属性名 |

---

## 3. 工程配置

| 现状 | 规范怎么说 | 差在哪 |
|---|---|---|
| 无 `.editorconfig` | 风格靠机器强制 | 缩进、using 排序、花括号全靠模仿 |
| `Directory.Build.props` 只集中版本号 | 公共属性集中声明 | `TargetFramework` / `Nullable` / `LangVersion` / `Deterministic` 仍在两个 csproj 各写一遍 |
| 无 `.gitattributes` | 行尾策略声明 | 靠 `core.autocrlf=true` 维持，源码实际是 CRLF 无 BOM |
| 未开 `TreatWarningsAsErrors` | 警告即错误 | 目前是 0 警告，但没有机制防止退化 |
| `Core/AllModsSky.Core.csproj` 的 `GenerateDocumentationFile=false` | 写了 `<see cref>` 就该校验 | 源码有 552 行 `///`，但不产出 XML，IDE 里也不提示摘要缺失。**注意打开它也只能补出 CS1591（缺注释）**——摘要写错、写重复，编译器都不会有诊断（实测） |
| `Core` 的 TFM 是 `net10.0-windows` | Core 不依赖 UI | 不依赖 WPF 属实，但依赖 `Microsoft.Win32` 注册表，所以用不了跨平台的 `net10.0` |

---

## 4. 行尾

| 现状 | 规范怎么说 | 差在哪 |
|---|---|---|
| 68 个文件 CRLF，3 个 LF | 无 BOM + CRLF | `Services/ModPickerWindow.xaml`、`Views/DragGeometry.cs`、`Views/ModPoolView.xaml.cs` 是 LF |

这 3 个文件在 Core 重构前就已经是 LF，不是重构引入的。

---

## 5. 版本号

**已集中到根目录 `Directory.Build.props` 的 `<AppVersion>`。** `AppInfo` 从程序集元数据读取，侧栏、设置页、关于页和更新器都用它；`app.manifest` 由构建目标生成，不再手写版本。原先 6 个文件、9 行的版本字面量已移除。

清单与维护方式见 [build-and-release.md](build-and-release.md) 第 6 节。

---

## 6. 构建与 CI

| 现状 | 规范怎么说 | 差在哪 |
|---|---|---|
| SDK 钉 `10.0.201`，TFM 是 `net10.0-windows` | 版本口径一致 | 已迁到 .NET 10；构建说明见 [build-and-release.md](build-and-release.md) |
| `build.ps1` 输出 `[1/3]` `[2/3]`，收尾段没有 `[3/3]` | 阶段标记完整 | 源码注释里写的是 `# ---- 3. 收尾 ----`，只是没打印标题 |
| 无测试项目 | — | 验证完全依赖 `.tmp/` 探针 |

---

## 7. 验证

| 现状 | 规范怎么说 | 差在哪 |
|---|---|---|
| 无测试项目 | — | 仓库里没有任何自动化测试 |
| 验证靠 `.tmp/<name>probe/` | 探针约定见 [build-and-release.md](build-and-release.md) 第 7 节 | `.tmp` 不入库、不参与 CI、不保证可编译 |
| `.tmp/` 下多数旧探针已失效 | — | Core 拆子目录后命名空间变了，旧探针里的 `using AllModsSky.Core;` 编译不过。属预期，不修 |

---

## 8. `.gitignore` 与磁盘不一致

| 现状 | 差在哪 |
|---|---|
| `.gitignore` 列了 `/.tmp`、`/dist`、`/finacheck`、`/tests`、`/tools`、`/release` | `/dist`、`/finacheck`、`/tests`、`/tools` 四个目录在磁盘上**都不存在** |
| README 第 8 章结构图曾画着 `tools\` 和 `dist\` | 已在本轮修正中删除 |

忽略规则留着无害，只是和实际目录对不上。

---

## 9. 隐式 using 集合不同

| 现状 | 差在哪 |
|---|---|
| `AllModsSky.App` 的隐式 using 是 **5 条** | 缺 `System.IO` 和 `System.Net.Http` |
| `AllModsSky.Core` 的隐式 using 是 **7 条** | 包含上述两条 |

这是 **WPF SDK 决定的，不是配置错误**。`UseWPF=true` 时 SDK 给的隐式集合比普通 SDK 少两条。所以 App 必须在 csproj 里显式补上这两条。

原来的 `AllModsSky.App/GlobalUsings.cs` 就是在补这个缺口，本轮已把两条搬进 csproj 并删除该文件。

---

## 10. MVVM 与 code-behind

| 现状 | 规范怎么说 | 差在哪 |
|---|---|---|
| `Views/LibraryView.xaml.cs` 有 437 行 | code-behind 只做事件转发 | 整套拖拽排序逻辑（`DragGhostAdorner`、`AnimatePositions`）写在 code-behind |
| `MainWindow.xaml.cs` 有 267 行 | 同上 | 标题栏主题、窗口拖放、快捷键 |
| 其余 5 个 View 的 code-behind 只有 11–47 行 | — | 符合规范 |

只有拖拽突破了 MVVM，其余都是事件转发。

---

## 11. 没有接口

| 现状 | 规范怎么说 | 差在哪 |
|---|---|---|
| 全项目只有 2 个接口（`IActivatable`、`IShellUi`） | — | 没有 `IXxxService` + `XxxService` 的配对，没有任何 mock 点 |
| 服务命名三套并存：`XxxService` / `XxxClient` / 无后缀静态类 | 见 [architecture.md](architecture.md) 第 3 节 | `DialogService` 名字叫 Service 但是 `static class`；`Importer` 是动词性操作也是静态类 |

这是零依赖 + 无 DI 容器的直接结果，属有意识的取舍，不是疏漏。

---

## 12. XAML

| 现状 | 规范怎么说 | 差在哪 |
|---|---|---|
| 无字体资源键 | 样式集中在 `Themes/Styles.xaml` | `FontFamily="Segoe UI, Microsoft YaHei UI"` 在 3 个 XAML 里各硬编码一次 |
| 46 处硬编码颜色，29 处在 `MainWindow.xaml` | 业务 UI 走资源键 | 品牌 logo 渐变属允许范围，但 `LibraryView.xaml` 也有 1 处 |
| 条目级 `DataTemplate` 两种写法 | — | `LibraryView` / `ModPoolView` 带 `DataType`；`DiscoverView` / `ModPickerWindow` 是匿名模板 |
| `LibraryView.xaml` / `ModPoolView.xaml` 把 `d:DataContext` 写在 `xmlns:d` 声明之前 | XAML 属性顺序统一 | 其余 4 个 View 是规范顺序 |

---

## 13. 提交历史

| 现状 | 差在哪 |
|---|---|
| `a40b38a` 之前的提交是自由式中文短句 | `更新`、`清理文件`、`代码掉落`、`双持`、`主窗户`、`2.0.3小小更新` 等，无 type 无 scope |
| `a40b38a` 及之后遵守约定式 | 20 多条全部符合 `type(scope): 中文描述` |

不需要回头改历史。详见 [commit-convention.md](commit-convention.md) 第 8 节。

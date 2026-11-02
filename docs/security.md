# 安全

这个程序会**删文件、跑命令行、下载 exe 并自替换**。这篇写清每条防线防的是什么。

---

## 1. 威胁模型

三个不可信输入：

| 输入 | 从哪来 | 风险 |
|---|---|---|
| `library.json` 里的字符串 | 用户手改、旧版数据迁移、文件损坏 | 目录名里带 `..\` 或 `\` 可以跳到任意路径 |
| 设置页手填的游戏版本号 | 用户输入 | 版本号会拼进 `cmd.exe` 命令行 |
| 网络下载的更新包 | GitHub Release | 被替换的 exe 会覆盖自身 |

对应的三道防线：路径枚举、版本号白名单、SHA256 校验。

---

## 2. 路径安全：不拼路径，只枚举

**核心规则**（`Core/Infrastructure/PathGuard.cs` 类注释原文）：

> 不信任 `library.json` 里的目录名。
> 定位已有条目一律从磁盘枚举，索引里的字符串只用来「查找」，不用来拼路径；
> 只有「新建目标」这一种场景才允许 `Path.Combine`，且名字必须通过 `IsSafeSegment`。

### 四个 API

| 方法 | 作用 |
|---|---|
| `FindExisting(root, name, wantFile)` | 在 `root` 下枚举真实条目并按名字精确匹配（忽略大小写）。找不到返回 `null`，调用方应视为「不存在」，**而不是去拼路径** |
| `IndexEntries(root)` | 把 `root` 下的条目建成字典（名字 → 路径 + 是否目录），批量同步时避免反复枚举 |
| `IsSafeSegment(name)` | 是否是合法单段名字：不含路径分隔符、不是 `.` 或 `..`。**只用于「创建新条目」** |
| `HasPathSeparator(name)` | 名字里含 `\` 或 `/` —— 这类值不可能对应磁盘上的条目，直接判非法 |

### `FindExisting` 的二次匹配

磁盘上的名字一定是净化过的，而旧数据里的 `FolderName` 可能还带着 `: ? *` 这些字符（比如 `Better Ship: Redux`）。所以 `FindExisting` 先按原名匹配，匹配不到再按 `FileOps.SafeFileName` 规范化一次。

```csharp
var hit = Match(root, name, wantFile);
if (hit != null) return hit;

var normalized = FileOps.SafeFileName(name, 80);
return string.Equals(normalized, name, StringComparison.Ordinal)
    ? null
    : Match(root, normalized, wantFile);
```

### 调用面

`PathGuard` 目前被两处调用：

| 位置 | 用途 |
|---|---|
| `Core/Services/DeploymentService.cs` | 部署、启停、删除、冲突检测、外部模组收编 |
| `App/Services/Workspace.cs:56` | UI 侧定位模组当前所在目录 |

`DeploymentService` 里的关键检查点：

| 行 | 检查 |
|---|---|
| 92–93 | 一次性 `IndexEntries` 建两个索引 |
| 122 / 186 | 部署前 `IsSafeSegment(key)`，非法就跳过并记 `Warn` |
| 203 | pak 文件名逐个 `IsSafeSegment` |
| 300 | `HasPathSeparator(key)` → 直接返回 `Missing` |
| 396 | 收编外部模组前 `IsSafeSegment`，非法直接抛 `InvalidOperationException` |
| 520–522 | 删除前 `HasPathSeparator` 拦截，**未删除任何文件** |

`IsSafeSegment` 全项目 5 个调用点，全部在 `DeploymentService`。

---

## 3. 命令注入：版本号白名单

AMUMSS 是外部 CLI，`AmumssService` 用 `cmd.exe /c` 调它。游戏版本号会拼进命令行，而版本号来自设置页的手填输入。

```csharp
// Core/Services/AmumssService.cs:71
private static readonly Regex GameVersionPattern =
    new(@"^\d{1,3}(\.\d{1,3}){0,2}$", RegexOptions.Compiled);
```

```csharp
// Core/Services/AmumssService.cs:322
/// 版本号会拼进 cmd 命令行，必须白名单化：设置页里手填的 "5.60 & calc" 原本可以直接执行任意命令。
```

不过白名单就返回原值，否则拒绝。

### 为什么不用 `ArgumentList`

```csharp
// Core/Services/AmumssService.cs:366
// 这里刻意保留手工拼引号：cmd.exe 用的是自己的引号规则，不认 CRT 的 \" 转义，
// 用 ArgumentList 反而会把复合命令解析坏。注入面已由 BuildArguments 的版本号白名单消除。
var psi = new ProcessStartInfo("cmd.exe", "/c \"" + inner + "\"")
```

这是个有意识的取舍：**`ArgumentList` 会破坏复合命令，所以靠白名单而不是靠参数转义**。改动这段前先读这条注释。

---

## 4. 破坏性操作二次确认

统一走 `IShellUi.Confirm`，返回 `ConfirmResult`：

```csharp
public enum ConfirmResult { None, Primary, Danger, Cancel }
```

`danger` 参数非 `null` 时，对话框会多出一个红色危险按钮。当前 15 个调用点：

| 文件 | 场景 |
|---|---|
| `LibraryViewModel` | 移除模组 / 永久删除 / 批量永久删除 / 删除配置 / 应用加载顺序 |
| `ModPoolViewModel` | 移出模组库 / 永久删除 / 全部收编 |
| `ToolsViewModel` | 写入加载顺序 / AMUMSS 重建 / 缺少 MBINCompiler / 收录构建结果 |
| `AboutViewModel` | 检查更新后安装 |
| `MainViewModel:314` | 转发给 `DialogService.Confirm` |

「移出模组库」和「永久删除」在同一个对话框里并列（`LibraryViewModel.cs:405`、`ModPoolViewModel.cs:470`），前者是 `Primary`，后者是 `Danger`。

---

## 5. 库损坏守卫

`library.json` 解析失败时，**先备份原文件并锁死写入**，避免空列表被 `Save()` 覆盖回去。

```csharp
// Core/Services/ModLibrary.cs
// 解析失败时先备份原文件并锁死写入，避免空列表被 Save() 覆盖回去。
if (!JsonStore.TryLoad<LibraryState>(AppPaths.DataFile, out var state, out _))
{
    var backup = NextBackupPath();
    try
    {
        File.Move(AppPaths.DataFile, backup);
        AppLog.Warn("library.json 无法解析，已备份到 " + backup);
    }
    catch (Exception ex)
    {
        AppLog.Error(ex, "备份损坏的 library.json");
        backup = AppPaths.DataFile;
    }

    _corrupt = true;
    LoadMessage = $"library.json 无法解析，已备份到 {backup}。本次以空库启动，不会覆盖原文件。";
    return;
}
```

备份名格式：`backup_library_20261102125300.json.bak`，与 `library.json` 同目录。重名时追加 `_1`、`_2`。

锁死状态由 `_corrupt` 标志承载，`Save()` 会直接返回并设置：

```csharp
LastSaveError = "模组库处于损坏保护状态，已跳过写入";
```

---

## 6. 单实例

双开会让两个实例同时改写 `library.json` 和 MODS 目录。

```csharp
// App/App.xaml.cs
// 双开会让两个实例同时改写 library.json 和 MODS 目录，先挡住。
// 注意：string.GetHashCode() 在 .NET Core 每进程随机化，必须用稳定哈希。
var dir = AppContext.BaseDirectory.ToLowerInvariant();
var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dir)))[..16];
_instanceMutex = new Mutex(true, @"Local\AllModsSky_" + hash, out var isFirstInstance);
```

**必须用稳定哈希**：`string.GetHashCode()` 在 .NET Core 上每进程随机化，用它做 Mutex 名字等于每次启动都是新名字，单实例保护直接失效。

用 `Local\` 前缀而不是 `Global\`，避免跨会话干扰。

---

## 7. 更新链路的完整性

```csharp
// Core/Integrations/UpdateService.cs:144
var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("更新文件 SHA256 校验失败，已停止安装。");
```

流程：查 Release → 下载到临时文件 → SHA256 比对 → 通过才自替换。校验失败抛异常，不覆盖现有 exe。

---

## 8. 凭据与日志

| 规则 | 现状 |
|---|---|
| Nexus API Key 只出现在请求头 | `NexusClient` 里注入 `apiKey` 请求头 |
| 网络客户端不写日志 | `NexusClient` / `OogcClient` / `UpdateService` 的 `AppLog.` 命中数 **0** |
| 日志不含路径以外的敏感信息 | `AppLog` 只记消息与异常，不记请求头 |

网络客户端完全不调 `AppLog` 是刻意的——URL 和请求头里可能带 Key，落进 `logs/latest.log` 就等于泄露到磁盘上。

---

## 9. 权限

**不要用管理员运行。** 程序清单是 `asInvoker`（普通用户权限）。以管理员运行时：

- 资源管理器向管理员进程拖放会被 Windows UIPI 阻止，拖入导入直接失效
- 程序写出的文件归管理员所有，之后普通权限启动可能读不了

例外情况见 README 第 10 章：仅在该环境确实需要时（比如游戏装在受保护目录）才临时提权。

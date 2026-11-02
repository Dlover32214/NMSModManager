# 构建与发布

从 `git clone` 到一个 `release/publish/AllModsSky.exe`，以及「怎么证明改对了」。

---

## 1. 环境

| 要求 | 说明 |
|---|---|
| PowerShell **7**（`pwsh`） | `build.ps1` 首行 `#Requires -Version 7.0` |
| .NET SDK | TFM 是 `net8.0-windows`；`AllModsSky.slnx` 需要 SDK **9+** 才能读 |
| 7-Zip / Bandizip | 可选，只影响 `.rar` / `.7z` / `.cab` 导入 |

**SDK 版本有三处不同口径**，都不是配置错误，但要知道：

| 位置 | 值 |
|---|---|
| 两个 csproj 的 `TargetFramework` | `net8.0-windows` |
| `build.ps1` 的分支判断 | 主版本 `>= 9` 走 `.slnx`，否则退化为直接编 App 项目 |
| CI（`.github/workflows/dotnet-desktop-CI.yml`） | `dotnet-version: '10.0.x'` |
| README 第 7 章 | 写的是 .NET 8 |

本地当前实测 SDK 是 `10.0.403`，与 CI 一致。

---

## 2. 一键构建（推荐）

```powershell
pwsh -File ./build.ps1
```

产物落在 `release/publish/AllModsSky.exe`。

| 参数 | 作用 |
|---|---|
| `-SelfContained` | 把 .NET 运行时打进去，约 150 MB，双击即用 |
| `-NoPause` | 结束后不停留（给终端 / CI 用） |

### 脚本做了什么

| 阶段 | 行为 |
|---|---|
| 环境检查 | `dotnet` 不在 PATH → 中止；`AllModsSky` 进程在跑 → 中止并报 PID（exe 被锁住，清理和覆盖都会失败） |
| `[1/3] 编译` | `dotnet build AllModsSky.slnx -c Release --nologo -v minimal -tl:off`；SDK 主版本 < 9 时改为直接编 `AllModsSky.App.csproj` |
| `[2/3] 发布` | 先 `Remove-Item release\publish -Recurse -Force`，再 `dotnet publish -c Release -r win-x64` |
| 校验 | 发布后 `Test-Path $exe` 失败 → 中止 |
| `[3/3] 收尾` | 打印耗时、全路径、MB 体积；框架依赖模式追加提示「目标机器需要 .NET 8 Desktop Runtime」 |

发布参数固定为：

```
-p:PublishSingleFile=true
-p:EnableCompressionInSingleFile=false
-p:DebugType=none
-p:PublishReadyToRun=false
```

框架依赖产物约 **1.25 MB**。

> 脚本的输出标记是 `[1/3]` `[2/3]`，但收尾段没有 `[3/3]` 标题（源码注释里写的是 `# ---- 3. 收尾 ----`）。只是标记不齐，不影响构建。

---

## 3. 手动构建

```bash
dotnet build AllModsSky.slnx -c Release
```

手动发布（与 CI 完全一致）：

```bash
dotnet publish src/AllModsSky.App/AllModsSky.App.csproj \
  -c Release -r win-x64 --no-self-contained \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=false \
  -p:DebugType=none \
  -p:PublishReadyToRun=false \
  -o release/publish
```

---

## 4. CI

`.github/workflows/dotnet-desktop-CI.yml`：

| 项 | 值 |
|---|---|
| 触发 | `push` / `pull_request` 到 `main` |
| 运行环境 | `windows-latest` |
| SDK | `10.0.x` |
| 步骤 | checkout → setup-dotnet → `dotnet build` → `dotnet publish` → upload-artifact |
| 产物名 | `AllModsSky-win-x64`，路径 `release/publish/AllModsSky.exe` |

CI 只做构建和上传，**不跑测试**——因为仓库里没有测试项目。

---

## 5. 产物

| 项 | 值 |
|---|---|
| 路径 | `release/publish/AllModsSky.exe` |
| 形态 | 单文件（`PublishSingleFile=true`） |
| 默认模式 | 框架依赖，目标机器需 .NET 8 Desktop Runtime |
| 体积 | 约 1.25 MB（`-SelfContained` 时约 150 MB） |
| 是否入库 | 否，`/release` 在 `.gitignore` 里 |

---

## 6. 版本号维护

**当前版本号在 6 个文件、9 行里维护。** 改版本要全部同步，漏一处就会出现界面上显示旧版本的情况。

| 文件 | 行 | 内容 |
|---|---|---|
| `src/AllModsSky.App/AllModsSky.App.csproj` | 14 | `<Version>2.0.6</Version>` |
| 同上 | 15 | `<AssemblyVersion>2.0.6.0</AssemblyVersion>` |
| 同上 | 16 | `<FileVersion>2.0.6.0</FileVersion>` |
| `src/AllModsSky.App/MainWindow.xaml` | 332 | 侧栏底部 `Text="v2.0.6"` |
| 同上 | 372 | 状态栏 `Text="v2.0.6"` |
| `src/AllModsSky.App/ViewModels/MainViewModel.cs` | 210 | `public string AppVersion => "2.0.6";` |
| `src/AllModsSky.App/ViewModels/SettingsViewModel.cs` | 198 | `public string AppVersion => "2.0.6";` |
| `src/AllModsSky.App/ViewModels/AboutViewModel.cs` | 235 | 反射读版本失败时的兜底串 |
| `src/AllModsSky.Core/Integrations/UpdateService.cs` | 52 | UserAgent `AllModsSky-Updater/2.0.6` |

只有 `AboutViewModel` 会先读程序集版本（`Assembly.GetName().Version`），读不到才用兜底串；其余几处都是字面量。

---

## 7. 验证：探针工程

**仓库里没有测试项目。** 验证靠 `.tmp/` 下的探针工程，它们不入库、不参与 CI、不保证长期可编译。

### 约定

```
.tmp/
└─ <名字>probe/
   └─ probe/
      ├─ <名字>Probe.csproj     Exe / net8.0-windows / ProjectReference 指向 Core
      └─ Program.cs
```

`ProjectReference` 是三级相对路径：

```xml
<ProjectReference Include="..\..\..\src\AllModsSky.Core\AllModsSky.Core.csproj" />
```

### 探针写法

本地函数计数，控制台输出 `PASS` / `FAIL`，结尾汇总，失败时返回非零退出码：

```csharp
var pass = 0;
var fail = 0;

void Check(string name, bool ok, string? detail = null)
{
    if (ok) { pass++; Console.WriteLine($"PASS  {name}"); }
    else { fail++; Console.WriteLine($"FAIL  {name}{(detail is null ? "" : "  → " + detail)}"); }
}

void Eq<T>(string name, T actual, T expected) =>
    Check(name, EqualityComparer<T>.Default.Equals(actual, expected), $"实际={actual} 期望={expected}");
```

### 运行

```bash
dotnet run --project .tmp/nsprobe/probe/NsProbe.csproj -c Release
```

### 提交信息里怎么写

```
验证：.tmp/nameprobe/probe 27 项断言全 PASS（emoji 与组合字符都算 1 个字、
截断不劈代理对、配置新建/重命名都受同一套限制、空名字被拒）
```

没有探针的改动就写手工冒烟项，别留空。

### 现有探针一览

`.tmp/` 下有 30 多个探针目录：`adoptall` / `busyprobe` / `cfgtest` / `drag` / `dragtest` / `healthprobe` / `linkprobe` / `logtest` / `nameprobe` / `nsprobe` / `pathtest` / `profiletest` / `promptfix` / `rowprobe` / `seedtest` / `shot` / `tiprobe` / `toggleprobe` / `uitest` 等。

**已知状态**：Core 拆分子目录后命名空间变了，`.tmp/` 下多数旧探针里的 `using AllModsSky.Core;` 已经失效，编译不过。这是预期结果，不用修——需要验证时新写一个，用新的子命名空间。

---

## 8. `.tmp` 的地位

| 项 | 状态 |
|---|---|
| 是否入库 | 否，`/.tmp` 在 `.gitignore` 里 |
| 是否参与 CI | 否 |
| 是否保证可编译 | 否 |
| 用途 | 一次性验证、截图（`shot`）、编码实验（`enc`） |

新写的探针不进 `src/`，也不进 `.slnx`。

---

## 9. 发布前检查清单

| # | 检查项 |
|---|---|
| 1 | 版本号 6 个文件 9 行是否全部同步 |
| 2 | `pwsh -File ./build.ps1` 是否 0 警告 0 错误 |
| 3 | 产物体积是否在 1.0–1.3 MB（框架依赖模式） |
| 4 | 目标 exe 是否已关闭（否则脚本会拒绝构建） |
| 5 | 打 tag 时用 `vMAJOR.MINOR.PATCH` 形式，与 csproj 的 `Version` 对齐 |
| 6 | 启动一次，确认 `logs/latest.log` 有本次会话头、无 `crash.log` |

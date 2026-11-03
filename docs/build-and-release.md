# 构建与发布

从 `git clone` 到一个 `release/publish/AllModsSky.exe`，以及「怎么证明改对了」。

---

## 1. 环境

| 要求 | 说明 |
|---|---|
| PowerShell **7**（`pwsh`） | `build.ps1` 首行 `#Requires -Version 7.0` |
| .NET SDK | 由根目录 `global.json` 钉在 **10.0.201** |
| 7-Zip / Bandizip | 可选，只影响 `.rar` / `.7z` / `.cab` 导入 |

### SDK 版本只写一处

`global.json` 在仓库根目录：

```json
{
  "sdk": {
    "version": "10.0.201",
    "rollForward": "latestFeature"
  }
}
```

`latestFeature` 的含义：在 `10.0` 这一档里优先使用机器上最新的 feature band（例如机器上只有 `10.0.403` 也能用），但不会悄悄跳到 11.x。要严格钉死单个补丁号就改成 `disable`，代价是机器上没装 `10.0.201` 时 `dotnet` 直接报「找不到 SDK」。

CI 不重复声明版本，`actions/setup-dotnet` 用 `global-json-file: global.json` 从同一个文件读。改版本只改这一处。

**目标框架和 SDK 版本不是同一个概念**：

| 位置 | 值 | 管的是什么 |
|---|---|---|
| 两个 csproj 的 `TargetFramework` | `net10.0-windows` | 产物跑在哪个运行时上 |
| `build.ps1` 的 `$sdkMajor -ge 9` 分支 | 主版本 ≥ 9 走 `.slnx` | SDK 能不能读 `.slnx` 这种解决方案格式 |

README 里出现的「.NET 10」指运行时（`Desktop Runtime`），指的不是 SDK，不算冲突。

> `global.json` 钉到 10 之后，`build.ps1` 里那个「SDK < 9 就退化为直接编项目」的分支已经走不到了。留着无害，只是死代码。

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
| `[2/3] 发布` | 先 `Remove-Item release\publish -Recurse -Force`，再 `dotnet publish -c Release -r win-x64`；.NET 10 SDK 直接支持 `net10.0-windows` 单文件发布 |
| 校验 | 发布后 `Test-Path $exe` 失败 → 中止 |
| `[3/3] 收尾` | 打印耗时、全路径、MB 体积；框架依赖模式追加提示「目标机器需要 .NET 10 Desktop Runtime」 |

发布参数固定为：

```
-p:PublishSingleFile=true
-p:EnableCompressionInSingleFile=false
-p:DebugType=none
-p:PublishReadyToRun=false
```

框架依赖产物约 **0.91 MB**。

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

> **单文件发布由 .NET 10 SDK 直接完成。** 不再需要安装或切换到 8.0.x SDK；手动发布保持与 CI 相同的命令即可。

---

## 4. CI

`.github/workflows/dotnet-desktop-CI.yml`：

| 项 | 值 |
|---|---|
| 触发 | `push` / `pull_request` 到 `main` |
| 并发 | 同分支新提交自动取消上一次运行（`cancel-in-progress`） |
| 权限 | `contents: read`，只读 |
| 运行环境 | `windows-latest`，`timeout-minutes: 20` |
| SDK | 从 `global.json` 读 `10.0.201`；单文件发布也由同一个 SDK 完成 |
| 步骤 | checkout → setup-dotnet（读 `global.json`）→ prepare（`dotnet --info`）→ 缓存 NuGet → `dotnet build` →（仅 push）`build.ps1` 发布 → summarize → upload-artifact |
| 产物名 | `AllModsSky-win-x64`，路径 `release/publish/AllModsSky.exe`，保留 14 天 |

### 缓存

缓存的是 NuGet 全局包目录 `~/.nuget/packages`，key 由 `**/*.csproj` 和 `AllModsSky.slnx` 的哈希决定。

**项目本身零 `PackageReference`，但缓存仍然有用**：`dotnet publish -r win-x64` 要拉 RID 运行时包（`Microsoft.NETCore.App.Host.win-x64` 之类），命中缓存能省掉这部分下载。将来加依赖时同样受益。

`dotnet --info` 放在 `Prepare` 步骤里，出问题时能直接看到 SDK 版本和运行时列表，不用翻构建日志。

### PR 与 push 的差别

| 事件 | 跑到哪一步 |
|---|---|
| `pull_request` | 只到 `dotnet build`——验证能不能编译就够了 |
| `push` 到 `main` | 再加 publish、摘要、上传 artifact |

PR 不产出 exe：publish 加传 artifact 每次白花几十秒和存储。要下载 PR 的产物，临时把对应步骤的 `if` 去掉。

### 运行摘要

`Summarize` 步骤把体积、SHA256、目标框架、SDK 版本、短提交号写进 `GITHUB_STEP_SUMMARY`，在 Actions 运行页面直接可见，不必点开日志找。

SHA256 用 `Get-FileHash -Algorithm SHA256` 算，和 `UpdateService` 校验更新包用的是同一个算法——下载下来可以自己比一遍。

### 没加的东西

| 没加 | 原因 |
|---|---|
| `-warnaserror` | 目前 0 警告，但加上等于改变策略：以后任何新警告都会让 CI 红。要不要加是产品决定，不是构建优化 |
| `ContinuousIntegrationBuild=true` | 需要配合 SourceLink 或 `SourceRoot` 才生效，本项目两者都没有，加了可能报错 |
| `paths-ignore` 跳过纯文档提交 | 如果这个 workflow 是分支保护的必需检查，跳过会让 PR 永远等一个不会出现的检查 |
| `packages.lock.json` + `setup-dotnet` 的 `cache: true` | 需要提交锁文件；零依赖项目没必要为缓存引入锁文件维护成本 |

CI 只做构建和上传，**不跑测试**——因为仓库里没有测试项目。

---

## 5. 产物

| 项 | 值 |
|---|---|
| 路径 | `release/publish/AllModsSky.exe` |
| 形态 | 单文件（`PublishSingleFile=true`） |
| 默认模式 | 框架依赖，目标机器需 .NET 10 Desktop Runtime |
| 体积 | 约 0.91 MB（`-SelfContained` 时约 150 MB） |
| 是否入库 | 否，`/release` 在 `.gitignore` 里 |

---

## 6. 版本号维护

**唯一入口：根目录 `Directory.Build.props` 的 `<AppVersion>`。**

```xml
<AppVersion>2.1.5</AppVersion>
```

改版本只改这一行，然后正常构建。其余位置全部自动派生：

| 位置 | 怎么拿到版本 |
|---|---|
| exe / dll 的 `Version` / `AssemblyVersion` / `FileVersion` | `Directory.Build.props` 派生给 MSBuild |
| 侧栏页脚、状态栏、设置页、关于页 | `AppInfo.DisplayVersion` / `AppInfo.Version` |
| 更新器 User-Agent | `AppInfo.UserAgent` |
| `app.manifest` 的 `assemblyIdentity version` | 构建目标 `GenerateAppManifest` 替换 `__APP_VERSION__` |

`app.manifest` 里保留的是 `__APP_VERSION__` 占位符，不要手动改成数字；构建时会在 `obj\<Configuration>\<TargetFramework>\app.manifest` 生成真实清单。

`AppInfo` 从当前程序集元数据读取版本，不保存版本字符串；因此代码里不应再出现 `2.1.5` 这类字面量。

---

## 7. 验证：探针工程

**仓库里没有测试项目。** 验证靠 `.tmp/` 下的探针工程，它们不入库、不参与 CI、不保证长期可编译。

### 约定

```
.tmp/
└─ <名字>probe/
   └─ probe/
      ├─ <名字>Probe.csproj     Exe / net10.0-windows / ProjectReference 指向 Core
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

---

## 10. 未来迁移 .NET 10 的入口

**现在不迁。** 这一节只是把要动的地方先记下来，免得将来改一半漏一半。

### 要改的位置

| # | 位置 | 现在 | 迁移后 |
|---|---|---|---|
| 1 | `src/AllModsSky.App/AllModsSky.App.csproj` 的 `TargetFramework` | `net10.0-windows` | `net10.0-windows` |
| 2 | `src/AllModsSky.Core/AllModsSky.Core.csproj` 的 `TargetFramework` | `net10.0-windows` | `net10.0-windows` |
| 3 | `build.ps1` 的收尾提示文案 | 「目标机器需要 .NET 10 Desktop Runtime」 | 改成 10 |
| 4 | `build.ps1` 的 `$sdkMajor -ge 9` 判断 | 为兼容旧 SDK 保留的分支（已被 `global.json` 架空） | 可简化成直接编 `.slnx` |
| 5 | `README.md` 第 5 章系统要求、第 10/11 章提到运行时的段落 | 「.NET 10 Desktop Runtime」 | 改成 10 |
| 6 | `global.json` 的 `sdk.version` | `10.0.201` | 不用改，已经是 10 |

CI 工作流和 `global.json` 都不用动——SDK 早就是 10 了，工作流读的就是这个文件。

### 两个坑

**`TargetFramework` 写了两遍。** `Directory.Build.props` 只集中版本号，没有集中 TFM，两个 csproj 各写一份。只改一处会出现 App 编 net10、Core 编 net8 的混合状态——能编过，但运行时会加载错版本的程序集。改的时候两个一起改，改完 `dotnet build` 看两行输出确认。

**SDK 版本和目标框架是两件事。** 现在用 .NET 10 SDK 编 `net10.0-windows`，两者已经对齐；判断有没有迁，仍看 `TargetFramework`，不看 `global.json`——`global.json` 只是钉 SDK。

### 不用碰的东西

版本号已经集中到根目录 `Directory.Build.props`，迁移到 `net10.0-windows` 不需要再改版本文件；两个 csproj 的 `<Using>` 注入清单、目录结构、`.tmp` 探针约定都与 TFM 无关。

### 迁移后的验证

现有探针可以继续用（`ProjectReference` 跟着 TFM 走）。需要额外确认的只有一件事：`PathGuard` 和 `FileOps` 里那些 Windows 路径 API 在 net10 上的行为有没有变。跑一遍 `.tmp/nsprobe` 加一次手工冒烟就够。

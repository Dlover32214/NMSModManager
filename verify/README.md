# 自检（AllModsSky.SelfCheck）

一个零依赖的控制台自检程序：直接调用 `AllModsSky.Core`，在临时目录里构造假游戏目录，验证最容易出错的几块逻辑。

## 运行

在仓库根目录：

```powershell
dotnet run --project verify\AllModsSky.SelfCheck\AllModsSky.SelfCheck.csproj -c Release
```

输出每个检查项的通过/失败，最后一行是汇总：

```text
结果：通过 48，失败 0
```

全部通过时进程退出码为 `0`，有任何失败时为 `1`，可以直接挂到 CI 或提交前钩子上。

## 为什么不引测试框架

项目本身是“无第三方 NuGet 依赖”的，自检也保持这个约定：只有一个 `Program.cs`，自带极简的 `CheckSuite`（`Section` / `Check` / `Finish`）。想加检查项时，复制一行 `suite.Check("说明", 条件, 失败时附带的详情)` 即可。

## 覆盖范围

- `LoadOrderService`
  - 读取 `GCMODSETTINGS.MXML`、按 `ModPriority` 排序；
  - 写优先级、重复写不产生多余文件改动、缺失条目统计；
  - UTF-8 BOM / CRLF 原样保留（无 BOM 的输入不会凭空加 BOM）；
  - 写前 `.bak` 备份、XML 合法性校验、原子替换、无残留 `.tmp`；
  - `ApplyEnabledStates` 只改 `Enabled`，不动 `ModPriority` / `Author` 等字段。
- `ModStateService`
  - 管理器启停 vs 游戏 `Enabled` 的三种不一致（游戏内已关、游戏内仍开、游戏尚未登记）；
  - 只读检测不写文件；修复时不碰旧式 `.pak` 模组；
  - 修复后 BOM 与 `ModPriority` 不变。
- `DeploymentService`
  - `MODS` / `MODS_disabled` 之间的搬迁；
  - `DISABLEMODS.TXT` 的创建与删除；
  - 外部模组识别、游戏目录无效时的失败处理。

## 注意

- 自检不会读写真实游戏目录，也不会启动游戏，可以随时运行；
- 游戏进程检测（`GameProcess.IsRunning`）只做“枚举进程名”，如果本机真的开着无人深空，对账会被跳过——这是预期行为，不是自检失败。

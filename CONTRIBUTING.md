# 参与开发

**本项目的规范以现状为准**——`docs/` 描述的是代码**实际怎么写的**，不是「理想中应该怎么写」。所以照文档做出来的代码，和仓库里现有的代码长一个样。

不追求政治正确，只追求「下一个人读得懂」。

---

## 六篇文档

| 文档 | 一句话 |
|---|---|
| [docs/coding-style.md](docs/coding-style.md) | 一个 C# 文件、一份资源字典长什么样：命名空间、类型声明、命名、注释、XAML 与主题 |
| [docs/architecture.md](docs/architecture.md) | 两个项目怎么分、Core 的四层依赖方向、MVVM 契约、组合根、失败怎么表达、日志通路 |
| [docs/build-and-release.md](docs/build-and-release.md) | 从 clone 到 `release/publish/AllModsSky.exe`，版本号在哪几处维护，验证靠什么 |
| [docs/commit-convention.md](docs/commit-convention.md) | 提交信息格式、type 与 scope 集合、正文模板、两个真实样例 |
| [docs/security.md](docs/security.md) | 路径为什么不拼接、命令注入怎么挡、哪些操作要二次确认、更新包怎么验 |
| [docs/deviations.md](docs/deviations.md) | 代码现状与本规范的差距，只陈述不要求整改 |

---

## 两条阅读路径

**改代码之前**，至少读这两篇：

1. [coding-style.md](docs/coding-style.md) — 否则写出来的东西和周围格格不入
2. [architecture.md](docs/architecture.md) — 否则会把 UI 逻辑塞进 Core，或者反过来

**发版之前**，读这两篇：

1. [build-and-release.md](docs/build-and-release.md) — 版本号在 6 个文件 9 行里维护，漏一处界面就显示旧版本
2. [commit-convention.md](docs/commit-convention.md) — 提交信息里必须有一行 `验证：`

**改涉及删文件、跑命令行、下载的逻辑时**，加读 [security.md](docs/security.md)。

---

## 四条最容易踩的坑

| 坑 | 后果 |
|---|---|
| 在 `Core/` 里写 `using System.Windows;` 或 `MessageBox` | 分层破掉，Core 就不再与 UI 无关 |
| 用 `StaticResource` 引用主题刷子 | 切换深浅色主题时该处不变色，因为 `ThemeManager` 是运行时替换整个字典 |
| 在 `Core/` 的 csproj 里加 `System.*` 的 `<Using>` | 与 `ImplicitUsings` 重复，生成文件里出现重复条目 |
| 在 CI 工作流里写 `dotnet-version` | 与 `global.json` 形成两个版本源，`setup-dotnet` 会把两边都装一遍，迟早对不上。CI 只写 `global-json-file: global.json` |

---

## 构建

```powershell
pwsh -File ./build.ps1
```

需要 PowerShell 7，以及 `global.json` 钉住的 .NET SDK **10.0.403**（`rollForward: latestPatch`，同档补丁号可用）。产物在 `release/publish/AllModsSky.exe`。

---

## 验证

仓库里**没有测试项目**。验证靠 `.tmp/` 下的一次性探针工程，它们不入库、不参与 CI。

写法见 [build-and-release.md](docs/build-and-release.md) 第 7 节。

---

## License

MIT，见 [LICENSE](LICENSE)。

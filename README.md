# All Mod's Sky 2.0.6

[English](README.en.md) | **简体中文**

> 《No Man's Sky》Windows 模组管理器  
> .NET 8 + WPF · 单文件发布 · 中文界面 · 兼容旧版 NMSModManager 数据

All Mod's Sky 是一个面向《No Man's Sky》的本地模组管理器。它不把模组复制到另一套“库”里，而是直接管理游戏真实的：

```text
<游戏目录>\GAMEDATA\MODS
<游戏目录>\GAMEDATA\MODS_disabled
```

启用模组就是让文件留在 `MODS`，停用就是把目录或 `.pak` 文件移动到同级的 `MODS_disabled`。因此启停是同一块盘上的重命名/移动操作，不占额外空间，也不会因为“库”和游戏目录不同步而产生两份文件。

---

## 目录

- [1. 项目概览](#1-项目概览)
- [2. 核心概念](#2-核心概念)
- [3. 功能详解](#3-功能详解)
  - [3.1 模组库](#31-模组库)
  - [3.2 启用、停用与同步](#32-启用停用与同步)
  - [3.3 外部模组与收编](#33-外部模组与收编)
  - [3.4 批量选取与拖拽排序](#34-批量选取与拖拽排序)
  - [3.5 冲突检测与加载顺序](#35-冲突检测与加载顺序)
  - [3.6 日志诊断](#36-日志诊断)
  - [3.7 AMUMSS 工具链](#37-amumss-工具链)
  - [3.8 发现模组（Nexus / oogc）](#38-发现模组nexus--oogc)
  - [3.9 存档读取](#39-存档读取)
  - [3.10 设置](#310-设置)
  - [3.11 界面与交互](#311-界面与交互)
  - [3.12 关于与更新](#312-关于与更新)
- [4. 数据目录与兼容格式](#4-数据目录与兼容格式)
- [5. 系统要求](#5-系统要求)
- [6. 运行方式](#6-运行方式)
- [7. 构建、测试与发布](#7-构建测试与发布)
- [8. 项目结构](#8-项目结构)
- [9. 架构说明](#9-架构说明)
- [10. 常见问题](#10-常见问题)
- [11. 已知限制与设计取舍](#11-已知限制与设计取舍)
- [12. License](#12-license)

---

## 1. 项目概览

### 它解决什么问题

《No Man's Sky》从 5.50 开始直接读取 `GAMEDATA\MODS` 里的散装 `EXML` / `MBIN` 目录，旧式 `.pak` 仍可能出现在一些模组中。手动管理这些文件时常见的问题是：

- 想快速启用/停用一个模组，只能手动剪切目录；
- 分不清哪些目录是管理器导入的，哪些是手动放进 `MODS` 的；
- 多个模组改同一个 `EXML` 路径或替换同一个 `MBIN` 时，不知道谁覆盖谁；
- 改完文件后忘了处理 `DISABLEMODS.TXT`；
- 安装 AMUMSS、生成模组、收集 `CreatedMODS` 的流程很分散；
- Nexus / oogc 的模组需要下载后再手动解压、拖入。

本项目把上述流程集中到一个 WPF 桌面程序里。

### 主要能力

| 能力 | 说明 |
|---|---|
| 模组库 | 导入 zip / rar / 7z / tar / gz / bz2 / xz / zst / cab / pak / 文件夹，启用/停用、排序、批量操作 |
| 外部模组 | 识别不由本程序管理的 `MODS` 内容，可直接启停或“收编” |
| 部署同步 | 把界面状态同步到游戏目录，自动维护 `DISABLEMODS.TXT` |
| 启动模式 | 启动原版（停用全部模组）或启动 Mod 版（恢复上次启用状态）；两者先同步再启动 |
| 冲突检测 | 扫描 `EXML` / `MBIN` 的路径重叠，按加载顺序判断覆盖关系 |
| 加载顺序 | 读取/写入 `GCMODSETTINGS.MXML` 的 `ModPriority`，自动备份 |
| 日志诊断 | 从游戏日志里提取报错，并尝试归因到具体模组 |
| AMUMSS | 检测 AMUMSS、分析修复方案、调用 `BUILDMOD.bat`、收录输出 |
| 发现 | Nexus（API Key）与 oogc.cc 的浏览、分页、详情、浏览器跳转、随机抽 10 个（Nexus 不再提供关键词搜索） |
| 关于 | 项目主页、发布页、程序/数据目录、自动检查最新 GitHub Release；下载更新后替换 exe 并重启 |
| 存档 | 读取 `.hg` 存档的版本、名称、平台、模式、游玩时长 |
| 兼容 | 读写旧版 `NMSModManager` 的 `settings.json` / `library.json` |
| 便携 | 默认 AppData，支持 `--data` 指定任意数据目录 |

### 技术栈

- C# / .NET 8
- WPF（Windows Presentation Foundation）
- MVVM（项目内手写 `ObservableObject` / `RelayCommand` / `AsyncRelayCommand`）
- `System.Text.Json`
- 无第三方 NuGet 依赖
- 单文件发布（框架依赖，需要 .NET 8 Desktop Runtime）

---

## 2. 核心概念

### 2.1 库就是 MODS

本程序没有独立的“模组仓库目录”。界面里的库记录只是索引，真实文件始终在：

```text
<游戏目录>\GAMEDATA\MODS
```

记录中的 `FolderName`（旧版叫 `LibraryPath` / `DeployName`）就是 `MODS` 下的目录名。这样无论从资源管理器手动放模组，还是从本程序导入，游戏看到的都是同一份文件。

### 2.2 启用与停用

- **启用**：模组目录/文件位于 `GAMEDATA\MODS`
- **停用**：模组目录/文件位于 `GAMEDATA\MODS_disabled`

停用不是删除，随时可以重新启用。关闭的模组不会丢，只有明确选择“永久删除”才会删除磁盘文件。

### 2.3 外部模组

如果 `MODS` 或 `MODS_disabled` 里存在本程序索引中没有的目录/`.pak`，它们会被识别为“外部模组”。外部模组：

- 可以直接点“启用/停用”，程序会移动它们；
- 也可以点“收编”，把它加入本程序的 `library.json`，之后出现在主列表中；
- 收编不会移动文件，只是登记。

库为空时会自动展开外部模组面板，避免用户误以为“程序把模组弄丢了”。

### 2.4 列表顺序 = 加载优先级

模组库列表的顺序就是加载顺序。列表越靠上，优先级越高；发生冲突时，靠上的模组覆盖靠下的模组。

“同步到游戏”不会自动改 `GCMODSETTINGS.MXML`；需要显式点“应用顺序”，把当前列表顺序写入游戏的加载顺序配置。

### 2.5 5.50+ 的散装模组与旧式 pak

- 5.50+：模组通常是包含 `EXML` / `MBIN` 的目录；
- 旧式 `.pak`：仍可作为兼容项导入，程序会把 `.pak` 放到 `MODS` 下并登记；
- 一个记录可以是“目录模组”或“pak 模组”，由 `PakFiles` 是否为空区分。

### 2.6 DISABLEMODS.TXT

《No Man's Sky》用 `GAMEDATA\PCBANKS\DISABLEMODS.TXT` 控制是否加载模组。程序在同步时会：

- 只要还有启用的模组：删除/不保留 `DISABLEMODS.TXT`；
- 所有模组都停用，且设置里允许时：创建 `DISABLEMODS.TXT`。

设置项 `CreateDisableModsFile` 控制是否自动创建该文件。

---

## 3. 功能详解

### 3.1 模组库

模组库是程序的主页面。

#### 导入

支持：

- `.zip`
- `.rar` / `.7z` / `.cab`（使用 7-Zip 或 Bandizip）
- `.tar` / `.tar.gz` / `.tgz` / `.tar.bz2` / `.tbz2` / `.tar.xz` / `.txz` / `.tar.zst` / `.tzst`（Windows 自带 tar，或 7-Zip / Bandizip）
- `.pak`
- 普通模组文件夹

导入方式：

1. 点右上角“导入模组”，多选文件；
2. 点“添加文件夹”；
3. 把文件/文件夹直接拖进窗口任意位置。

导入流程大致为：

1. 解压/复制到临时目录；
2. 自动寻找压缩包里的“内容根目录”，避免多套一层文件夹；
3. 生成不冲突的目标目录名；
4. 放入 `GAMEDATA\MODS`；
5. 写入 `library.json`，默认启用；
6. 同步一次游戏状态。

解压提示：程序优先用 Windows 自带 `tar.exe` 处理 tar 系列；`.rar` / `.7z` / `.cab` 会依次尝试 7-Zip、Bandizip（`bz.exe`）和 Windows tar。找不到可用工具时会提示安装 7-Zip 或 Bandizip。

#### 搜索与筛选

- 搜索框：按模组名、版本、来源、目录名过滤；
- 筛选：全部 / 已启用 / 已停用 / 有问题；
- 顶部计数：总模组数、已启用数、已选数；
- “有问题”基于冲突/健康检查结果。

#### 行内操作

每个模组行提供：

- 启用/停用开关；
- 打开模组目录；
- 删除/移除菜单；
- 整行拖动排序（普通模式）；
- 状态、大小、版本、格式等信息。

#### 删除的两种语义

点删除时程序会区分：

- **移出列表**：先把模组停用（文件进入 `MODS_disabled`），再从索引中移除，文件仍保留；
- **永久删除…**：二次确认后，删除 `MODS` 与 `MODS_disabled` 中对应的目录/文件，无法通过程序恢复。

批量删除同样是永久删除，会明确显示模组数量和总大小。

#### 其他

- 打开 `MODS` 文件夹；
- 刷新游戏状态；
- 同步到游戏；
- 应用顺序；
- 外部模组面板；
- 批量操作条。

### 3.2 启用、停用与同步

#### 行内开关

切换某一行的开关时，程序会：

1. 更新记录并保存 `library.json`；
2. 调用部署同步；
3. 把目录/`.pak` 从 `MODS` 移到 `MODS_disabled`，或反向移动；
4. 更新 `DISABLEMODS.TXT`；
5. 刷新外部模组和界面状态。

#### “同步到游戏”按钮

用于把当前索引状态与磁盘状态重新对齐，适合：

- 手动改过 `MODS` 目录；
- 从别处复制了模组；
- 程序异常退出后状态不一致；
- 批量操作后想再确认一次。

同步会报告：

- 成功移动/同步数量；
- 失败项；
- 缺失项（索引里有、磁盘上找不到）。

#### 注意

游戏只在启动时扫描 `MODS`。启用/停用后需要**重启游戏**才会生效。

#### 原版 / Mod 版启动

侧边栏提供两个启动按钮：

- **启动 Mod 版**：如果之前点过“启动原版”，先恢复当时保存的启用状态；否则使用当前状态。随后同步并启动游戏。
- **启动原版**：记录当前启用的模组，把受管理的模组全部停用并同步（必要时创建 `DISABLEMODS.TXT`），再启动无 Mod 的游戏。

原版模式保存的启用 ID 写在数据目录的 `vanilla-launch.json`，下次点“启动 Mod 版”时恢复并删除。中途关闭管理器不会丢状态；如果同步失败，程序不会启动游戏；“启动原版”同步失败时会恢复原启用状态。

### 3.3 外部模组与收编

外部模组面板会列出：

- `MODS` 里不由本程序管理的目录；
- `MODS_disabled` 里不由本程序管理的目录；
- 根目录下的 `.pak` 文件。

每个外部模组提供：

- 当前状态（启用中/已停用）；
- 启用/停用；
- 收编。

收编后会创建一条 `ModRecord`，加入 `library.json`；文件位置不变。之后可以用主列表的开关统一管理。

库为空时，程序会自动打开外部模组面板，帮助用户接管已有模组。

### 3.4 批量选取与拖拽排序

#### 批量选取

工具栏“应用顺序”左边有“批量选取”按钮：

- 只有点击这个按钮才会进入批量选取模式；
- 非批量模式下单击或拖动模组行都不会进入批量模式，也不会显示批量条；
- 进入批量后，单击整行任意非交互区域即可选中/取消，不需要 Ctrl；
- 开关、按钮、滚动条等交互控件不会误触选中；
- 再点“退出批量”、点“取消选择”或按 `Esc` 退出。

批量条支持：

- 批量启用；
- 批量停用；
- 置顶/置底；
- 永久删除所选。

#### 拖拽排序

- 普通模式下按住整行任意非按钮区域拖动，即可调整加载顺序；
- 拖起的行有抬起、阴影效果；
- 松手后，所有受影响的行会从旧位置平滑滑到新位置，避免瞬间“挤压”；
- 顺序会写回 `library.json`；
- 需要让游戏读取新顺序时，再点“应用顺序”。

### 3.5 冲突检测与加载顺序

#### 冲突检测

“工具 → 冲突检测”会扫描库内模组的 `EXML` / `MBIN` 文件，按“是否碰到同一个 vanilla 路径”分组：

- **MBIN 替换冲突**：多个模组替换同一个 `MBIN` 路径，只有最后加载的生效，属于硬冲突；
- **EXML 同属性冲突**：多个 `EXML` 补丁改同一路径的同一属性，最后加载的生效；
- **EXML 同文件（属性不同）**：只是路径重叠，属性不同，可能可以共存；
- **EXML 补丁 + MBIN 替换**：允许叠加，补丁可以作用在替换后的文件上。

程序会给出：

- 冲突组；
- 涉及模组；
- 是否启用；
- 加载顺序；
- 预测的“胜出者”。

列表顺序越靠上，优先级越高。

#### 加载顺序

《No Man's Sky》的加载顺序保存在：

```text
<游戏目录>\Binaries\SETTINGS\GCMODSETTINGS.MXML
```

程序可以：

- 读取现有 `ModPriority`；
- 把当前列表顺序写入 `ModPriority`；
- 写入前自动备份为 `GCMODSETTINGS.MXML.bak`；
- 在文件不存在时提示先进游戏生成一次。

### 3.6 日志诊断

“工具 → 日志诊断”会扫描游戏日志，提取包含以下关键词的行：

```text
error / failed / failure / exception / unable to / could not /
cannot / missing / corrupt / invalid / crash / assert
```

然后：

- 按日志文件分组；
- 尝试把报错行归因到模组名；
- 显示“疑似有问题的模组”；
- 列出命中的日志原文。

为了控制内存占用，只读取日志尾部约 4 MB，每个文件最多约 20,000 行，最多保留 4,000 条错误。

### 3.7 AMUMSS 工具链

AMUMSS 是《No Man's Sky》的模组脚本/编译工具链。本程序不内置 AMUMSS，但会检测并调用用户已有的安装。

#### 检测

在设置中指定 AMUMSS 根目录，或让程序自动搜索。有效目录通常包含：

- `BUILDMOD.bat`
- `MBINCompiler.exe`

#### 分析/修复

“工具 → AMUMSS”可以：

- 检查当前模组目录结构；
- 分析需要哪些 lua 脚本；
- 生成修复/重建计划；
- 把相关 lua 复制到 AMUMSS 的 `ModScript` 目录。

#### 重建

重建流程：

1. 调用 `BUILDMOD.bat`（独立控制台，方便回答交互）；
2. 等待编译完成；
3. 从 AMUMSS 的 `CreatedMODS` 收集输出；
4. 把原模组目录移动到 `.bak_yyyyMMdd_HHmmss` 备份；
5. 用新输出替换原目录；
6. 失败时尝试恢复原目录。

#### 打开位置

工具页还提供：

- 打开 AMUMSS 目录；
- 打开 `ModScript`；
- 打开 `CreatedMODS`；
- 打开日志目录。

### 3.8 发现模组（Nexus / oogc）

#### Nexus Mods

- 需要个人 Nexus API Key；
- 支持分类、排序（热门/最新/最近更新等）；
- 分类标签会随窗口宽度自动换行；
- 本地分页（每页固定数量）；
- 卡片显示封面、标题、分类、版本、作者；
- 详情页可查看简介和文件列表；
- 可跳转浏览器页面、复制链接；本程序不直接下载模组；
- “随机抽 10 个”会从 Nexus 三种排序合并后的模组池中随机抽 10 个（Nexus 没有官方随机接口）。

没有 API Key 时：

- Nexus 列表不可用；
- 仍提供“在 Nexus 官网浏览全部模组”的浏览器入口；
- 可以手动下载后拖回本程序导入。

#### oogc.cc

- 不需要 API Key；
- 支持分类、排序、费用、关键词搜索；
- 服务器分页；
- 详情页、打开原页面；下载请在浏览器完成；
- 封面异步加载。

#### 加载体验

- 首次加载显示与真实卡片同尺寸的骨架屏（图片区 + 两行文字）；
- 数据到达后原位替换，避免页面跳动；
- 封面在后台加载，不阻塞卡片出现；
- 超过 25 秒或请求失败时，骨架屏会切换成明确的错误提示和“重试”；
- 分页支持上一页/下一页。

### 3.9 存档读取

“设置 → 存档”可以读取《No Man's Sky》的 `.hg` 存档信息。

`.hg` 的结构是若干“16 字节块头 + LZ4 压缩块”：

```text
magic(0xFEEDA1E5) / 压缩长度 / 解压长度 / 保留
```

程序只解压到能读到头部字段为止，不把几十 MB 的存档全部展开。

可显示：

- 存档文件；
- 存档版本（SaveVersion）；
- 存档名；
- 平台；
- 游戏模式；
- 游玩时长；
- 修改时间；
- 文件大小。

还会尝试从 `backup.*.zip` 文件名推断游戏版本（例如 `...560.zip` → 5.60）。

### 3.10 设置

设置页包括：

- 游戏目录：检测/浏览；
- 游戏版本：手动填写或自动检测；
- AMUMSS 目录；
- Nexus API Key；
- 深色/浅色主题；
- 是否自动创建 `DISABLEMODS.TXT`；
- 存档读取；
- 打开 MODS / 数据目录 / 日志；
- 以管理员身份重启（仅在该环境确实需要时使用）。

API Key 会保存到数据目录的 `settings.json`。如果 API Key 无法保存，优先检查数据目录是否可写，以及 `settings.json` 是否被其他程序占用。

### 3.11 界面与交互

- 左侧导航：模组库 / 发现模组 / 工具 / 设置 / 关于；
- 左下角：启动 Mod 版 / 启动原版按钮、当前模式提示、游戏状态卡、深色模式开关；
- 顶部状态栏：MODS 路径、计数、版本；
- 深色/浅色两套主题；
- 全局 Toast 提示：带缩放、上浮、外层柔光和阴影；
- 长操作使用忙碌遮罩；
- 发现页使用骨架屏；
- 左上角 64×64 动态 Logo：
  - 全息行星；
  - 20 秒一圈的拖尾光点卫星；
  - 卫星在远侧会从徽章下层经过，近侧重新露出；
  - 副标题 `A NMS MOD MANAGER`；
- 快捷键：
  - `Ctrl+O` 导入
  - `Ctrl+1` 模组库
  - `Ctrl+2` 发现
  - `Ctrl+3` 工具
  - `Ctrl+4` 设置
  - `Ctrl+5` 关于
  - `F5` 刷新
  - `Esc` 退出批量/收起提示

### 3.12 关于与更新

左侧“设置”下方新增“关于”页面：

- 显示当前版本、项目主页和发布页链接；
- 显示程序目录（exe 所在位置）和数据目录（settings/library 所在位置），可直接打开；
- 打开页面时自动联网检查 GitHub 的最新 Release；
- 显示最新版本、发布时间和 Release 说明；
- 如果发现新版本，提示用户点击“下载更新版本”；
- 下载后校验 SHA256（如果 Release 提供摘要），替换当前 exe 并自动重启；
- 安装前会再次弹窗确认；替换由后台 PowerShell 等待当前进程退出后完成；
- 如果 Release 没有 exe/zip 资产，则引导用户打开发布页手动处理；
- 检查失败时显示错误，并保留手动打开项目主页/发布页的入口。

更新检查使用公开 GitHub API，仓库地址为：

```text
https://github.com/Dlover32214/NMSModManager
```

---

## 4. 数据目录与兼容格式

### 4.1 数据目录优先级

程序按以下顺序寻找可写的数据目录：

1. `--data <目录>` 命令行参数；
2. `ALLMODSSKY_DATA` 环境变量；
3. `%APPDATA%\NMSModManager`（旧版兼容目录）；
4. `%APPDATA%\AllModsSky`；
5. `%LOCALAPPDATA%\NMSModManager`；
6. `%LOCALAPPDATA%\AllModsSky`；
7. `exe 同目录\NMSModManager.data`；
8. `exe 同目录\data`；
9. `%TEMP%\AllModsSky`。

如果旧版目录里已经有 `settings.json` / `library.json`，会优先复用旧目录，避免配置丢失。

手动指定：

```powershell
AllModsSky.exe --data "D:\NMSData"
```

### 4.2 数据文件

```text
<数据目录>\
  settings.json       设置
  library.json        模组索引
  app.log             应用日志
  crash.log           未处理异常日志（如发生）
  cache\import\       导入临时文件
  cache\download\     下载缓存
```

### 4.3 library.json

新版字段：

```json
{
  "SchemaVersion": 2,
  "Mods": [
    {
      "Id": "唯一 id",
      "Name": "模组名",
      "Source": "本地导入",
      "Version": "7.01",
      "FolderName": "MODS 下的目录名",
      "PakFiles": [],
      "Enabled": true,
      "Size": 12345,
      "ImportedAt": "2026-10-31T12:00:00+08:00",
      "Note": ""
    }
  ]
}
```

为了兼容旧版 NMSModManager，同时写出：

```json
{
  "LibraryPath": "MODS 下的目录名",
  "DeployName": "MODS 下的目录名（pak 模组为空）"
}
```

旧版只需要 `LibraryPath` / `DeployName` / `PakFiles` / `Enabled` 等字段即可读取。

### 4.4 settings.json

```json
{
  "GamePath": "E:\\SteamLibrary\\steamapps\\common\\No Man's Sky",
  "GameVersion": "7.10",
  "AmumssPath": "",
  "NexusApiKey": "",
  "Theme": "Dark",
  "CreateDisableModsFile": true,
  "LibraryPath": "旧版库路径（兼容字段）",
  "BackupOnDeploy": true
}
```

`LibraryPath` / `BackupOnDeploy` 是旧版保留字段，新版不依赖它们完成核心功能，但会保留写回，方便旧版继续读取。

### 4.5 旧版兼容细节

- 旧版 `library.json` 的 `/Date(1730000000000)/` 会解析为正常时间；
- 旧版 `LibraryPath` / `DeployName` 会映射为新版的 `FolderName`；
- 新版写出的 `ImportedAt` 使用 ISO 8601，旧版 `JavaScriptSerializer` 也能反序列化；
- 旧版独立库目录不再需要：两边实际都以游戏 `GAMEDATA\MODS` 为库；
- 旧版设置里的 `LibraryPath`、`BackupOnDeploy` 会保留。

---

## 5. 系统要求

| 项目 | 要求 |
|---|---|
| 操作系统 | Windows 10 / Windows 11，x64 |
| 运行时 | .NET 8 Desktop Runtime (x64) |
| 游戏 | 《No Man's Sky》，有 `GAMEDATA\PCBANKS` 或 `Binaries\NMS.exe` |
| 可选 | 7-Zip 或 Bandizip（`.rar` / `.7z` / `.cab`；tar 系列可用 Windows 自带 tar） |
| 可选 | AMUMSS（使用 AMUMSS 工具链时需要） |
| 网络 | 使用 Nexus / oogc 发现页时需要 |
| 权限 | 普通用户即可；不建议管理员运行 |

本程序发布为**框架依赖单文件**：

- exe 本体约 1.14 MiB；
- 运行时来自系统安装的 .NET 8 Desktop Runtime；
- 因此换电脑时需要确认目标机器已安装该运行时；
- 框架依赖单文件不支持压缩，WPF 也不适合安全裁剪，所以体积不会回到旧版 .NET Framework 的 600 KB。

---

## 6. 运行方式

### 直接运行

1. 解压 release 包；
2. 在资源管理器里双击 `AllModsSky.exe`；
3. 首次运行设置游戏目录（通常会自动检测 Steam 库）；
4. 如果 `MODS` 里已有模组，库为空时外部模组面板会自动展开，点“收编”即可接管。

### 不要用管理员运行

程序清单是普通用户权限（asInvoker）。以管理员运行时：

- 资源管理器向管理员进程拖放会被 Windows UIPI 阻止；
- 拖入模组会表现为“没反应”；
- 普通用户权限已经可以写游戏 `MODS` 时，不需要管理员。

如果确实遇到权限问题，优先检查目录 ACL，而不是直接提权。

### release 文件

- `release\AllModsSky.exe`
- `release\AllModsSky-2.0.6-win-x64.zip`
- `release\README.txt`（快速上手版）

---

## 7. 构建、测试与发布

### 环境

- .NET SDK 8
- Windows 10/11
- PowerShell（Windows PowerShell 5.1 或 PowerShell 7 均可）

### 构建

在项目根目录执行：

```powershell
dotnet build AllModsSky.slnx -c Release
```

### 运行

```powershell
dotnet run --project src\AllModsSky.App\AllModsSky.App.csproj -c Release
```

### 自检

项目自带一个无第三方测试框架的控制台自检程序：

```powershell
$env:ALLMODSSKY_DATA = "$PWD\.tmp\selftest"
dotnet run --project tests\AllModsSky.SelfTest\AllModsSky.SelfTest.csproj -c Release
```

当前自检覆盖：

- 导入/解压/内容根识别；
- 启用/停用与文件移动；
- 外部模组识别/收编；
- `DISABLEMODS.TXT`；
- 冲突扫描；
- 加载顺序读写与备份；
- AMUMSS 分析/收集；
- 旧版数据迁移；
- 其他核心路径。

自检结果应为：

```text
结果：通过 111，失败 0
```

### 发布单文件

```powershell
dotnet publish src\AllModsSky.App\AllModsSky.App.csproj `
  -c Release -r win-x64 --no-self-contained `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=false `
  -p:DebugType=none `
  -p:PublishReadyToRun=false `
  -o release\publish
```

然后把 `release\publish\AllModsSky.exe` 复制到 `release\`。

`EnableCompressionInSingleFile` 必须为 `false`：框架依赖单文件不支持压缩，开启会报 `NETSDK1176`。

---

## 8. 项目结构

```text
NMSModManager\
├─ AllModsSky.slnx                 解决方案
├─ README.md                       中文说明（默认）
├─ README.en.md                    English README
├─ src\
│  ├─ AllModsSky.Core\             UI 无关的核心逻辑
│  │  ├─ Models.cs                 模组/设置/游戏信息模型
│  │  ├─ Infrastructure.cs         AppPaths / JsonStore / FileOps / 日志
│  │  ├─ ModLibrary.cs             library.json 读写与规范化
│  │  ├─ Importer.cs               zip/rar/7z/tar/gz/bz2/xz/zst/cab/pak 导入
│  │  ├─ DeploymentService.cs      MODS / MODS_disabled 同步
│  │  ├─ VanillaLaunchStore.cs     原版启动状态保存/恢复
│  │  ├─ ConflictAnalyzer.cs       EXML/MBIN 冲突扫描
│  │  ├─ LoadOrderService.cs       GCMODSETTINGS.MXML
│  │  ├─ LogDiagnostics.cs         游戏日志诊断
│  │  ├─ AmumssService.cs          AMUMSS 检测/分析/重建/收集
│  │  ├─ NexusClient.cs            Nexus API
│  │  ├─ OogcClient.cs             oogc.cc
│  │  ├─ SaveGameReader.cs         .hg 存档读取（LZ4 块）
│  │  ├─ UpdateService.cs          GitHub Release 检查、下载与自替换更新
│  │  ├─ LegacyDataMigrator.cs     旧版 NMSModManager 数据兼容
│  │  └─ Resources\mapping.json    内置映射数据
│  └─ AllModsSky.App\              WPF 界面
│     ├─ MainWindow.xaml(.cs)      主窗口、侧栏、Toast、动态 Logo
│     ├─ ViewModels\               页面 ViewModel
│     ├─ Views\                    Library / Discover / Tools / Settings / About
│     ├─ Themes\                   Dark / Light / Styles
│     ├─ Services\                 Dialog / Shell UI / 主题等
│     ├─ Controls\                 控件辅助
│     └─ app.ico / app.manifest
├─ tests\
│  └─ AllModsSky.SelfTest\         控制台自检（111 项）
├─ tools\                          辅助脚本（非程序运行必需）
├─ release\                        最终发布产物
├─ dist\                           历史构建/临时发布目录
└─ .tmp\                           本地临时目录
```

---

## 9. 架构说明

### 9.1 分层

```text
WPF Views
   ↓ 绑定
ViewModels (App)
   ↓ 调用
Workspace / Services (App + Core)
   ↓
Core: Import / Deployment / Conflict / LoadOrder / AMUMSS / Nexus / Oogc / Saves
   ↓
File system + Game directories
```

- **Core** 不引用 WPF，方便自检和复用；
- **App** 只负责界面、对话框、主题和 MVVM 编排；
- **SelfTest** 直接调用 Core，覆盖文件操作和解析逻辑。

### 9.2 关键服务

| 服务 | 职责 |
|---|---|
| `AppPaths` | 数据目录解析、路径回退、日志路径 |
| `JsonStore` | 缩进 JSON、原子写入、`.bak` 备份 |
| `ModLibrary` | 索引加载/保存/规范化/排序 |
| `Importer` | 压缩包/文件夹/pak 导入 |
| `DeploymentService` | 启停文件移动、外部模组、`DISABLEMODS.TXT` |
| `ConflictAnalyzer` | EXML/MBIN 路径与属性冲突 |
| `LoadOrderService` | 读取/写入 `GCMODSETTINGS.MXML` |
| `LogDiagnostics` | 日志错误提取与模组归因 |
| `AmumssService` | AMUMSS 检测、重建、收录 |
| `NexusClient` / `OogcClient` | 网络资源列表/详情 |
| `SaveGameReader` | `.hg` 头部字段读取 |
| `LegacyDataMigrator` | 旧版数据读取与迁移 |

### 9.3 设计原则

- **单一真实位置**：模组只在游戏 `MODS`/`MODS_disabled`，不复制第二份库；
- **索引可丢、文件不丢**：`library.json` 丢失时，外部模组面板仍能发现磁盘上的模组；
- **破坏性操作二次确认**：永久删除、应用顺序等都有确认；
- **写入前备份**：加载顺序、AMUMSS 原目录等关键写入前备份；
- **可诊断**：`app.log` / `crash.log` 记录核心操作和异常；
- **旧数据优先兼容**：能读旧版就复用，不强行要求迁移。

---

## 10. 常见问题

### 拖入没有反应

- 确认不是以管理员身份运行（UIPI 会阻止从资源管理器拖放到管理员进程）；
- 确认文件是 zip / rar / 7z / tar / gz / bz2 / xz / zst / cab / pak / 文件夹；
- 不要直接在压缩包内双击 exe，先解压；
- 也可以改用“导入模组 / 添加文件夹”按钮。

### 库是空的，但游戏里明明有模组

这是正常的首次/换机状态：索引里没有记录，但磁盘上有文件。库为空时外部模组面板会自动展开，点“收编”即可登记。

### 状态栏显示“MODS 不可写”

- 程序会在 `app.log` 记录具体的 `UnauthorizedAccessException` 等异常；
- 优先从资源管理器正常双击 exe；
- 检查游戏目录 ACL、杀毒软件/受控文件夹访问；
- 普通用户权限通常已经足够，不建议直接管理员运行。

### 启用/停用后游戏没变化

文件已经移动，但游戏只在启动时扫描 `MODS`。完全退出游戏再启动。

### RAR / 7z / tar 导入失败

程序不内置压缩库：`.rar` / `.7z` / `.cab` 需要 7-Zip 或 Bandizip（`bz.exe`）；tar 系列优先使用 Windows 自带 `tar.exe`。安装其中一个解压工具，或把压缩包转换为 zip。

### Nexus 列表为空 / 提示 API Key 无效

- 到 Nexus 账号设置生成个人 API Key；
- 填在“设置 → Nexus Mods”并保存；
- 没有 Key 时仍可使用浏览器入口手动下载。

### AMUMSS 找不到

设置的 AMUMSS 目录应包含 `BUILDMOD.bat` 或 `MBINCompiler.exe`。程序不附带 AMUMSS。

### 旧版 NMSModManager 的数据还能用吗

可以。程序优先读取 `%APPDATA%\NMSModManager`，并兼容旧版 `LibraryPath` / `DeployName` / `PakFiles` / `/Date(...)` / `BackupOnDeploy` 等字段。

### 为什么是 1.14 MiB，而不是 600 KB

旧版 600 KB 是 .NET Framework 4.8 程序，运行时由 Windows 自带。本版是 .NET 8 WPF，使用框架依赖单文件，运行时来自系统安装的 .NET 8 Desktop Runtime；WPF 不适合裁剪，因此 1 MB 出头是当前代码形态的合理体积。

### 数据目录能改到其他盘吗

可以：

```powershell
AllModsSky.exe --data "D:\NMSData"
```

或设置环境变量 `ALLMODSSKY_DATA`。

---

## 11. 已知限制与设计取舍

1. **需要 .NET 8 Desktop Runtime**  
   不是自包含发布；换机器要确认运行时存在。

2. **启停后需要重启游戏**  
   程序只移动文件，游戏不会热重载 `MODS`。

3. **RAR/7z 依赖外部解压工具**  
   为了保持零第三方 NuGet 依赖，压缩格式交给本机 7-Zip 或 Bandizip；tar 系列使用 Windows 自带 tar。

4. **Nexus 需要 API Key，且受网络/接口限制**  
   没有 Key 时只能走浏览器手动下载。

5. **AMUMSS 不随程序分发**  
   需要用户自行安装并指定目录。

6. **不做云同步；更新需要确认**  
   数据在本机；关于页自动检查版本，只有点击“下载更新版本”才会下载、替换并重启。

7. **旧版格式兼容是“字段级”兼容**  
   新版仍以游戏 `MODS` 为唯一真实库；旧版如果配置了独立的旧库目录，新版不会复制出第二份模组。

8. **单文件发布不是自包含**  
   体积小是以“系统已有 .NET 8”为代价的；如果需要完全离线自包含，体积会显著增加。

9. **界面为中文单语言**  
   当前没有多语言资源切换。

10. **不主动修改系统设置**  
    程序只管理游戏模组目录和自己的数据目录；不会改系统终端、开始菜单等设置。

---

## 12. License

MIT License（以项目声明为准）。

---

## 附：快速上手

```text
1. 安装 .NET 8 Desktop Runtime
2. 解压 release 包，双击 AllModsSky.exe（不要管理员）
3. 设置游戏目录（通常自动检测）
4. 拖入 zip / 文件夹，或点“导入模组”
5. 用开关启用/停用；需要时点“同步到游戏”
6. 重启《No Man's Sky》
```

如果库为空但 `MODS` 里已有内容：打开“外部模组”面板 → 收编 → 用主列表统一管理。

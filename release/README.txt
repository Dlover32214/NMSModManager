All Mod's Sky 2.1.0  ·  单文件版（.NET 10）
====================================================

一、运行（重要）
----------------
1. 在资源管理器里直接双击 AllModsSky.exe，单文件，约 1.35 MB。
   · 不要从聊天窗口的文件卡片打开（可能带沙箱限制）。
   · 不要用“以管理员身份运行”：管理员进程收不到资源管理器拖放（Windows UIPI 限制）。
2. 需要 .NET 10 Desktop Runtime (x64)。换电脑如提示缺少运行时，安装：
   https://dotnet.microsoft.com/download/dotnet/10.0/runtime
3. 数据目录默认使用旧版 NMSModManager 的目录，格式也兼容：
      %APPDATA%\NMSModManager
   如果该目录已有旧版 settings.json / library.json，会直接读取，不会丢配置。
   不可写时依次回退：
      %APPDATA%\AllModsSky → %LOCALAPPDATA% → exe 旁的 NMSModManager.data / data → %TEMP%
   也可以手动指定：
      AllModsSky.exe --data "D:\NMSData"
4. API Key 保存在数据目录的 settings.json 里。
   如果之前把 Key 填在旧版便携 data\ 里，重新填一次并保存即可。
5. 界面语言在「设置 → 界面语言」里选择：跟随系统 / 简体中文 / English。
   保存设置后立即切换界面，不需要重启；语言也保存在 settings.json 的 Language 字段里。

二、这一版的核心变化
--------------------
2.1.0 采用新的「模组 / 模组库 / 配置」模型：

· 运行时迁移到 .NET 10（net10.0-windows），仍为框架依赖单文件。
· 新增中英双语：设置页可切换「跟随系统 / 简体中文 / English」，保存后立即切换界面。
· 模组健康检查、冲突分析、工具页、发现页和对话框的文案已纳入本地化目录。

· 模组页：只显示当前配置里的模组，列表顺序就是加载顺序。
· 模组库页：全局池子，导入、收编外部模组、删除/移出都在这里。
· 配置：一套配置 = 一份模组清单 + 每个模组的启停状态。
  - 内置「无模组」永远在菜单最上面，不可改名、不可删除；
    切到它并点「启动游戏」，就是不带模组的原版。
  - 其余配置可以新建（复制当前这套）、重命名、删除；
    只剩一套配置时不能删。
  - 删除配置只删清单，模组文件和记录都不动。
  - 当前配置记在 library.json 的 ActiveProfileId 里，下次启动还是它。

· 「添加模组」从模组库里勾选还没进当前配置的模组；
  「移出配置」只是把它从这套配置里拿掉，文件不动，之后还能加回来。
· 模组库支持批量加入配置、批量移出；外部模组可以逐条或全部收编。
· 每个模组可以手动填一个链接（Nexus / oogc / 论坛等），
  点链接图标直接打开；再点编辑图标可修改或清除。
· 模组健康检查：识别 EXML / MBIN / 旧式 pak / MXML / lua，
  提示旧 pak 在 5.50 后不生效、MXML 需转 EXML、Globals 放错位置、目录名超长等问题。
· 拖拽排序：按住行会先浮起，拖动时跟着光标走半透明预览，并显示插入位置提示线。
· 日志改到数据目录的 logs\latest.log；超过 2 MB 自动压缩归档，只留最近 5 份。
· 数据写入更稳：JSON 临时文件显式 fsync 后再替换；library.json 损坏会先备份并锁死写入，
  避免空库把原文件覆盖掉。

三、启动游戏
------------
左下角只有一个「启动游戏」：
1. 先按当前配置同步启停状态到游戏目录；
2. 当前配置里有启用的模组，就带模组启动；
3. 当前配置是「无模组」或全部停用，就停用全部模组后启动原版；
4. 同步失败不会启动游戏。

不再需要旧版的 vanilla-launch.json：原版 / Mod 版由当前配置决定，
不会再出现“上次启动原版后忘了恢复”的两套状态对不上。

四、与旧版数据兼容
------------------
· library.json 升级为 SchemaVersion 3：
  Mods 是全局模组库，Profiles 是配置，ActiveProfileId 是当前配置。
· 旧版只有一份启停状态时，会自动收成一套「默认」配置，不会让所有模组掉进模组库。
· 仍同步写 FolderName / LibraryPath / DeployName / PakFiles，
  旧版 NMSModManager 可以继续读取；新版也能读旧版字段。
· settings.json 继续使用 GamePath / AmumssPath / Theme / NexusApiKey / GameVersion /
  CreateDisableModsFile，并保留旧版 LibraryPath / BackupOnDeploy 字段。
· 旧版 /Date(时间戳)/ 会自动解析并迁移。
· 模组文件仍然只有一份：<游戏>\GAMEDATA\MODS（停用区 MODS_disabled）。

五、常用操作
------------
· 导入：拖 zip / 文件夹 / pak 到窗口任意位置，或点「导入模组 / 添加文件夹」。
· 已有模组但列表是空的：看模组库页的「外部模组」面板，逐条或全部收编。
· 启用 / 停用：模组页开关；文件在 MODS 与 MODS_disabled 之间移动。
· 应用顺序：把当前配置的列表顺序写入 GCMODSETTINGS.MXML（自动备份）。
· 删除有两种：
  - 移出配置 / 移出模组库：文件保留，停用到 MODS_disabled；
  - 永久删除：二次确认后删磁盘文件，无法通过程序恢复。
· 配置菜单：模组页工具栏「配置 · 当前名字」，可切换、重命名、新建、删除。

六、快捷键
----------
Ctrl+O 导入
Ctrl+1 模组
Ctrl+2 模组库
Ctrl+3 发现
Ctrl+4 工具
Ctrl+5 设置
Ctrl+6 关于
F5 刷新
Esc 退出批量选取 / 收起提示

七、说明
--------
· 修改加载顺序、安装 / 停用模组前建议先关闭游戏。
· 日志与崩溃信息都在数据目录的 logs\ 下（latest.log / crash.log / 历史 .log.gz）。
· 程序只读取游戏目录和存档；不会上传模组或 API Key。
· 关于页进入时自动检查 GitHub Release；只有点击「下载更新版本」才会下载、
  校验 SHA256（发布页提供时）、替换 exe 并重启。

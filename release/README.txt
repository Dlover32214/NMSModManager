All Mod's Sky 2.0.0  ·  单文件版（.NET 8）
====================================================

一、运行（重要）
----------------
1. 在资源管理器里直接双击 AllModsSky.exe，单文件，约 1.13 MB。
   · 不要从聊天窗口的文件卡片打开（可能带沙箱限制）。
   · 不要用“以管理员身份运行”：管理员进程收不到资源管理器拖放（Windows UIPI 限制）。
2. 需要 .NET 8 Desktop Runtime (x64)。本机已安装 8.0.25；
   换电脑如提示缺少运行时，安装：
   https://dotnet.microsoft.com/download/dotnet/8.0/runtime
3. 数据目录默认使用旧版 NMSModManager 的目录，格式也兼容：
      %APPDATA%\NMSModManager
   如果该目录已有旧版 settings.json / library.json，会直接读取，不会丢配置。
   不可写时依次回退：
      %APPDATA%\AllModsSky → %LOCALAPPDATA% → exe 旁的 NMSModManager.data / data → %TEMP%
   也可以手动指定：
      AllModsSky.exe --data "D:\NMSData"
4. API Key 保存在数据目录的 settings.json 里。
   如果之前把 Key 填在旧版便携 data\ 里，重新填一次并保存即可。

二、与旧版 NMSModManager 的数据兼容
------------------------------------
· library.json 同时写 FolderName（新版）和 LibraryPath / DeployName（旧版），
  旧版可以直接读取；新版也能读取旧版的 LibraryPath / DeployName / PakFiles。
· settings.json 保留旧版的 LibraryPath / BackupOnDeploy 字段，
  并继续使用 GamePath / AmumssPath / Theme / NexusApiKey / GameVersion /
  CreateDisableModsFile，旧版可以继续读写。
· 旧版 library.json 里的 /Date(时间戳)/ 会自动解析并迁移。
· 模组文件仍然只有一份：<游戏>\GAMEDATA\MODS（停用区 MODS_disabled）。

三、本次更新
------------
· 左上角品牌图标扩大为 64×64 全息行星 + 越出徽章背景的拖尾光点卫星：
  卫星每 20 秒绕行星一圈；副标题为 A NMS MOD MANAGER。
· 左下角恢复“启动游戏”按钮（在游戏版本卡上方）。
· 批量选择：点“批量选择”进入后，单击整行（按钮/开关/滚动条除外）即可选中/取消，
  不需要 Ctrl；再点“退出批量”或按 Esc 退出。
· 拖拽排序：拖起的行有抬起/阴影，松手后所有受影响的行从旧位置平滑滑到新位置。
· 提示气泡：比原来大 15%，从下方小尺寸放大进入；柔光和阴影在气泡外侧。
· 发现页分页：上一页/下一页都能用。
· 发现页加载：先显示与真实卡片同尺寸的骨架（图片 + 两行文字），
  数据到达后原位替换；超过 25 秒或失败会切成明确错误 + 重试，不保留骨架。
· 修复 API Key 不保存：密码框改为直接事件同步，保存后写入 settings.json。
· 数据目录改为兼容 NMSModManager，旧配置可直接用。

四、为什么是 1.13 MB，而不是 600 KB
------------------------------------
· 旧版 600 KB 是 .NET Framework 4.8 程序：Windows 自带 4.8 运行时，
  程序本身只需要打包自己的 IL。
· 本版是 .NET 8 WPF，采用“框架依赖 + 单文件”：运行时来自系统安装的
  .NET 8 Desktop Runtime，exe 里只装程序自身。框架依赖单文件不支持压缩，
  WPF 也不支持安全裁剪，1 MB 出头基本是这套代码的下限。
· 如果要 600 KB 且不装 .NET 8，只能移植回 .NET Framework 4.8；
  那是代码移植，不是打包参数，另行处理。

五、模组放在哪
--------------
  <游戏目录>\GAMEDATA\MODS
关闭（停用）的模组移动到同级的 MODS_disabled，不会删除。
重新打开开关会自动移回 MODS。导入的模组默认立即启用。
切换后需要重启游戏才会重新读取 MODS。

六、第一次使用 / 已有模组
--------------------------
如果模组已经在 GAMEDATA\MODS 或 MODS_disabled 里，但列表是空的：
· 程序会在库为空时自动展开“外部模组”面板；
· 每一行都有：启用 / 停用 / 收编；
· 点“收编”会把它加入库（文件位置不变），之后就能用列表里的开关；
· 或者直接点“启用/停用”，立即移动文件。

拖入 zip / 文件夹 / pak 到窗口任意位置即可导入；
也可以点右上角“导入模组 / 添加文件夹”。

七、常见问题
------------
· 拖入没反应：
  - 确认不是以管理员身份运行（管理员进程无法接收资源管理器拖放）；
  - 先解压 zip，不要在压缩包里直接双击 exe。
· 库是空的，但游戏里明明有模组：
  - 看“外部模组”面板（库为空时会自动展开），点“收编”。
· 状态栏显示“MODS 不可写”：
  - 程序会把具体原因写进数据目录的 app.log；
  - 从资源管理器正常双击通常就有权限；仍失败时把 app.log 最后几行发来。
· 启用/停用后游戏没变化：
  - 文件已经移动，但游戏只在启动时扫描 MODS，重启游戏生效。

八、功能
--------
· 模组库：导入 zip / rar / 7z / pak / 文件夹（支持拖拽），启停、批量、拖动排序
· 部署：把启停状态同步到游戏；自动处理 DISABLEMODS.TXT
· 外部模组：识别 MODS 里不由本程序管理的模组，可停用 / 收编
· 冲突检测、加载顺序（GCMODSETTINGS.MXML，自动备份）、日志诊断
· AMUMSS：复制 lua、调用 BUILDMOD 重建、收录输出（原目录自动备份）
· 发现：Nexus（需 API Key）与 oogc.cc 资源浏览；分页、骨架加载
· 存档：读取 .hg 版本 / 存档名 / 游玩时长
· 深色 / 浅色主题

九、快捷键
----------
Ctrl+O 导入；Ctrl+1 模组库；Ctrl+2 发现；Ctrl+3 工具；Ctrl+4 设置；
F5 刷新；Esc 退出批量选择 / 收起提示。

十、说明
--------
· 修改加载顺序、安装 / 停用模组前建议先关闭游戏。
· 日志与崩溃信息都在数据目录下（app.log / crash.log）。

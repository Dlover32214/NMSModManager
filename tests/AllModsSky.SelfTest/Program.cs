using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AllModsSky.Core;

namespace AllModsSky.SelfTest;

internal static class Program
{
    private static int _passed;
    private static int _failed;
    private static string _root = "";

    private static int Main()
    {
        _root = Path.Combine(AppContext.BaseDirectory, "selftest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);

        AppPaths.UseRoot(Path.Combine(_root, "appdata"));
        AppPaths.UseLegacyRoot(Path.Combine(_root, "legacy"));
        Directory.CreateDirectory(AppPaths.LegacyRoot);
        SettingsStore.Reset();

        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            TestInfrastructure();
            TestVersionParser();
            TestImporter();
            TestLibrary();
            TestDeployment();
            TestConflicts();
            TestLoadOrder();
            TestLogs();
            TestSaveReader();
            TestAmumss();
            TestLegacyMigration();
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine("UNEXPECTED: " + ex);
        }
        finally
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        Console.WriteLine();
        Console.WriteLine($"结果：通过 {_passed}，失败 {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    // ---------- 基础设施 ----------

    private static void TestInfrastructure()
    {
        Section("基础设施 / JSON / 路径");

        var settings = new AppSettings
        {
            GamePath = @"E:\SteamLibrary\steamapps\common\No Man's Sky",
            NexusApiKey = "abc",
            Theme = "Light",
            GameVersion = "7.10"
        };

        var file = Path.Combine(_root, "settings.json");
        JsonStore.Save(file, settings);
        JsonStore.Save(file, settings);
        var loaded = JsonStore.Load<AppSettings>(file);

        Check("JSON 往返（含中文/路径）", loaded != null && loaded.Theme == "Light" && loaded.NexusApiKey == "abc");
        Check("重复保存产生 .bak", File.Exists(file + ".bak"));
        Check("中文不转义", JsonStore.Serialize(new { name = "中文模组" }).Contains("中文模组"));

        Check("大小格式化 KB", Humanize.Size(1536) == "1.5 KB", Humanize.Size(1536));
        Check("大小格式化 MB", Humanize.Size(3 * 1024 * 1024).EndsWith("MB"), Humanize.Size(3 * 1024 * 1024));

        var safe = FileOps.SafeFileName("a:b*c?d\"e<f>g|h", 100);
        Check("非法文件名字符被替换", !safe.Contains(':') && !safe.Contains('*') && !safe.Contains('?'));

        var dirA = NewDir("fileops", "a");
        Directory.CreateDirectory(Path.Combine(dirA, "sub"));
        File.WriteAllText(Path.Combine(dirA, "sub", "x.txt"), "hello");
        var dirB = Path.Combine(_root, "fileops", "b", "moved");
        FileOps.MoveDirectory(dirA, dirB);
        Check("目录移动", File.Exists(Path.Combine(dirB, "sub", "x.txt")) && !Directory.Exists(dirA));

        Check("目录大小", FileOps.DirSize(dirB) == 5);
    }

    private static void TestVersionParser()
    {
        Section("版本解析与适配");

        var exact = VersionParser.Parse("Multiplies Stack Sizes 4241 7.01");
        Check("解析 7.01", exact != null && Math.Abs(exact.Value - 7.01) < 0.0001, exact?.Raw);

        var wildcard = VersionParser.Parse("CraftableUpgradeMods 1944 Patch-7.xAndUp");
        Check("解析 7.x（最低版本）", wildcard != null && wildcard.IsMinimum && Math.Abs(wildcard.Value - 7) < 0.0001);

        Check("无版本返回 null", VersionParser.Parse("Readme") == null);

        Check("7.01 对 7.10 兼容", VersionParser.Evaluate("7.01", "7.10").State == "Ok",
            VersionParser.Evaluate("7.01", "7.10").Text);
        Check("5.60 对 7.10 警告", VersionParser.Evaluate("5.60", "7.10").State == "Warn",
            VersionParser.Evaluate("5.60", "7.10").Text);
        Check("7.x 对 7.10 兼容", VersionParser.Evaluate("7.xAndUp", "7.10").State == "Ok");
        Check("游戏版本未知", VersionParser.Evaluate("7.01", "").State == "Unknown");
    }

    // ---------- 导入 / 库 ----------

    private static void TestImporter()
    {
        Section("导入器");

        var library = NewDir("import", "lib");
        var source = NewDir("import", "src");

        var zipPath = Path.Combine(source, "CoolMod.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddZipEntry(archive, "CoolMod/readme.txt", "hello");
            AddZipEntry(archive, "CoolMod/Globals/GCGAMEPLAYGLOBALS.EXML", "<Property name=\"Foo\" value=\"1\" />");
            AddZipEntry(archive, "CoolMod/METADATA/UI/Test.EXML", "<Property name=\"Bar\" value=\"2\" />");
        }

        var result = Importer.ImportOne(zipPath, library, new ImportOptions());
        Check("zip 导入成功", result.Ok, result.Message);
        Check("文件夹名来自模组名", Directory.Exists(Path.Combine(library, "CoolMod")));
        Check("EXML 文件已落盘", File.Exists(Path.Combine(library, "CoolMod", "Globals", "GCGAMEPLAYGLOBALS.EXML")));
        Check("记录指向正确目录", result.Record != null && result.Record.FolderName == "CoolMod");
        Check("非 pak 模组 PakFiles 为空", result.Record is { PakFiles.Count: 0 });

        var duplicate = Importer.ImportOne(zipPath, library, new ImportOptions { NameExists = n => n == "CoolMod" });
        Check("重名自动加序号", duplicate.Ok && duplicate.DisplayName == "CoolMod (2)", duplicate.DisplayName);

        // GAMEDATA\MODIS\<mod> 整包结构应被剥掉
        var layout = NewDir("import", "layout", "LayoutMod", "GAMEDATA", "MODS", "LayoutMod");
        File.WriteAllText(Path.Combine(layout, "a.MBIN"), "mbin");
        var layoutResult = Importer.ImportOne(Path.Combine(_root, "import", "layout", "LayoutMod"), library,
            new ImportOptions());
        Check("GAMEDATA 结构被解开", layoutResult.Ok &&
                                  File.Exists(Path.Combine(library, "LayoutMod", "a.MBIN")), layoutResult.Message);

        // 单个 pak
        var pakPath = Path.Combine(source, "Single.pak");
        File.WriteAllBytes(pakPath, new byte[] { 1, 2, 3, 4 });
        var pakResult = Importer.ImportOne(pakPath, library, new ImportOptions());
        Check("pak 导入成功", pakResult.Ok);
        Check("pak 记录含相对文件名", pakResult.Record is { PakFiles.Count: 1 } &&
                                  pakResult.Record.PakFiles[0].EndsWith("Single.pak"));
        Check("pak 被包进同名文件夹", File.Exists(Path.Combine(library, "Single", "Single.pak")));

        var invalid = Importer.ImportOne(Path.Combine(source, "readme.txt"), library, new ImportOptions());
        Check("不支持格式被拒", !invalid.Ok);
    }

    private static void TestLibrary()
    {
        Section("模组库");

        var library = new ModLibrary();
        library.Load();
        Check("首次运行为空", library.Mods.Count == 0);

        var a = new ModRecord { Name = "Alpha", FolderName = "Alpha", Enabled = true, Size = 100 };
        var b = new ModRecord { Name = "Beta", FolderName = "Beta", Enabled = false, Size = 200 };
        var c = new ModRecord { Name = "中文模组", FolderName = "中文模组", Enabled = true, Size = 300 };
        library.Add(a);
        library.Add(b);
        library.Add(c);
        Check("保存成功", library.Save(), library.LastSaveError);

        var reloaded = new ModLibrary();
        reloaded.Load();
        Check("重新载入数量", reloaded.Mods.Count == 3);
        Check("顺序保持", reloaded.Mods[0].Name == "Alpha" && reloaded.Mods[1].Name == "Beta");
        Check("中文字段无损", reloaded.Mods[2].Name == "中文模组", reloaded.Mods[2].Name);

        reloaded.Move(1, 0);
        Check("移动顺序", reloaded.Mods[0].Name == "Beta");

        reloaded.MoveMany(new[] { reloaded.Mods[0] }, false);
        Check("移到末尾", reloaded.Mods[^1].Name == "Beta");

        var messy = new ModRecord { Name = "Messy", FolderName = "", Id = "" };
        reloaded.Add(messy);
        var fixes = reloaded.Normalize();
        Check("补齐目录名/Id", fixes > 0 && messy.FolderName.Length > 0 && messy.Id.Length > 0);

        var dup = new ModRecord { Name = "Messy", FolderName = "Messy" };
        reloaded.Add(dup);
        reloaded.Normalize();
        Check("重复目录名被改名", dup.FolderName != "Messy", dup.FolderName);
    }

    // ---------- 部署 ----------

    private static void TestDeployment()
    {
        Section("部署 / 启停 / 外部模组");

        var game = NewGame("deploy");
        var modsDir = GameLocator.ModsDir(game);
        var parkedDir = GameLocator.ParkedDir(game);
        Directory.CreateDirectory(modsDir);
        Directory.CreateDirectory(parkedDir);

        Directory.CreateDirectory(Path.Combine(modsDir, "Alpha"));
        File.WriteAllText(Path.Combine(modsDir, "Alpha", "a.EXML"), "x");
        Directory.CreateDirectory(Path.Combine(parkedDir, "Beta"));
        File.WriteAllText(Path.Combine(parkedDir, "Beta", "b.EXML"), "x");
        Directory.CreateDirectory(Path.Combine(modsDir, "Gamma")); // 状态漂移：记录停用但在 MODS 里

        var alpha = Rec("Alpha", "Alpha", true);
        var beta = Rec("Beta", "Beta", false);
        var gamma = Rec("Gamma", "Gamma", false);
        var records = new List<ModRecord> { alpha, beta, gamma };

        var service = new DeploymentService();
        var report = service.Sync(game, records, true);

        Check("同步无失败", report.Failed == 0, string.Join("; ", report.Messages));
        Check("启用的保持原位", Directory.Exists(Path.Combine(modsDir, "Alpha")));
        Check("停用的保持停用", Directory.Exists(Path.Combine(parkedDir, "Beta")));
        Check("漂移的记录被移到停用区", Directory.Exists(Path.Combine(parkedDir, "Gamma")) &&
                                  !Directory.Exists(Path.Combine(modsDir, "Gamma")));
        Check("有启用模组时不写 DISABLEMODS", !File.Exists(GameLocator.DisableModsFile(game)));

        alpha.Enabled = false;
        service.Sync(game, records, true);
        Check("停用后移动到停用区", Directory.Exists(Path.Combine(parkedDir, "Alpha")) &&
                                !Directory.Exists(Path.Combine(modsDir, "Alpha")));
        Check("全部停用时写入 DISABLEMODS", File.Exists(GameLocator.DisableModsFile(game)));

        alpha.Enabled = true;
        service.Sync(game, records, true);
        Check("重新启用后回到 MODS", Directory.Exists(Path.Combine(modsDir, "Alpha")));
        Check("有启用模组后删除 DISABLEMODS", !File.Exists(GameLocator.DisableModsFile(game)));

        // 旧式 pak
        var pakFolder = Path.Combine(modsDir, "PakMod");
        Directory.CreateDirectory(pakFolder);
        File.WriteAllBytes(Path.Combine(pakFolder, "Test.pak"), new byte[] { 9, 9, 9 });
        var pak = Rec("PakMod", "PakMod", true, "Test.pak");
        records.Add(pak);

        service.Sync(game, records, true);
        Check("pak 被部署到 MODS 根目录", File.Exists(Path.Combine(modsDir, "Test.pak")));
        Check("pak 暂存目录被清理", !Directory.Exists(pakFolder));

        pak.Enabled = false;
        service.Sync(game, records, true);
        Check("pak 停用后进停用区", File.Exists(Path.Combine(parkedDir, "PakMod", "Test.pak")) &&
                              !File.Exists(Path.Combine(modsDir, "Test.pak")));

        pak.Enabled = true;
        service.Sync(game, records, true);
        Check("pak 重新启用", File.Exists(Path.Combine(modsDir, "Test.pak")));

        // 外部模组
        Directory.CreateDirectory(Path.Combine(modsDir, "ExternalMod"));
        File.WriteAllText(Path.Combine(modsDir, "ExternalMod", "e.EXML"), "x");
        File.WriteAllBytes(Path.Combine(modsDir, "Loose.pak"), new byte[] { 1 });

        var external = service.ListExternal(game, records);
        Check("识别外部文件夹", external.Any(e => e.Name == "ExternalMod" && !e.IsFile));
        Check("识别外部 pak 文件", external.Any(e => e.Name == "Loose.pak" && e.IsFile));

        var ext = external.First(e => e.Name == "ExternalMod");
        service.SetExternal(game, ext, false);
        Check("外部模组可停用", Directory.Exists(Path.Combine(parkedDir, "ExternalMod")));

        var extParked = service.ListExternal(game, records).First(e => e.Name == "ExternalMod");
        Check("停用区里的外部模组被识别", !extParked.Enabled);

        var adopted = service.Adopt(game, extParked, n => records.Any(r => r.Name == n));
        Check("收编外部模组", adopted.Name == "ExternalMod" && !adopted.Enabled);

        service.DeleteFiles(game, adopted);
        Check("收编后删除文件", !Directory.Exists(Path.Combine(parkedDir, "ExternalMod")));

        Check("Presence 识别状态", service.Presence(game, alpha) == ModPresence.InMods);
        alpha.Enabled = false;
        service.Sync(game, records, true);
        Check("Presence 识别停用", service.Presence(game, alpha) == ModPresence.Parked);
    }

    // ---------- 冲突 / 加载顺序 ----------

    private static void TestConflicts()
    {
        Section("冲突分析");

        var root = NewDir("conflicts");
        var a = Path.Combine(root, "A");
        var b = Path.Combine(root, "B");
        Directory.CreateDirectory(Path.Combine(a, "Globals"));
        Directory.CreateDirectory(Path.Combine(b, "Globals"));
        Directory.CreateDirectory(Path.Combine(a, "METADATA"));
        Directory.CreateDirectory(Path.Combine(b, "METADATA"));

        File.WriteAllText(Path.Combine(a, "Globals", "GCGAME.EXML"), "<Property name=\"Foo\" value=\"1\" />");
        File.WriteAllText(Path.Combine(b, "Globals", "GCGAME.EXML"), "<Property name=\"Bar\" value=\"1\" />");
        File.WriteAllText(Path.Combine(a, "METADATA", "Same.MBIN"), "m");
        File.WriteAllText(Path.Combine(b, "METADATA", "Same.MBIN"), "m");

        var targets = new[]
        {
            new ConflictTarget { Name = "A", Folder = a, Enabled = true, Order = 0 },
            new ConflictTarget { Name = "B", Folder = b, Enabled = true, Order = 1 }
        };

        var result = ConflictAnalyzer.Scan(targets);
        var exml = result.Conflicts.FirstOrDefault(c => c.VanillaPath.EndsWith("GCGAME.EXML", StringComparison.OrdinalIgnoreCase));
        var mbin = result.Conflicts.FirstOrDefault(c => c.VanillaPath.EndsWith("SAME.MBIN", StringComparison.OrdinalIgnoreCase));

        Check("发现 EXML 重叠", exml != null);
        Check("EXML 属性不同不算硬冲突", exml is { HardMbin: false, SharedProperties.Count: 0 }, exml?.Kind);
        Check("MBIN 同路径为硬冲突", mbin is { HardMbin: true });
        Check("优先级最高者胜出", exml?.WinnerName == "A", exml?.WinnerName);

        File.WriteAllText(Path.Combine(b, "Globals", "GCGAME.EXML"), "<Property name=\"Foo\" value=\"2\" />");
        var result2 = ConflictAnalyzer.Scan(targets);
        var exml2 = result2.Conflicts.First(c => c.VanillaPath.EndsWith("GCGAME.EXML", StringComparison.OrdinalIgnoreCase));
        Check("同属性被识别", exml2.SharedProperties.Contains("Foo"));
        Check("汇总文本", result2.Summary.Contains("重叠"), result2.Summary);
    }

    private static void TestLoadOrder()
    {
        Section("加载顺序");

        var game = NewGame("loadorder");
        var settingsDir = Path.Combine(game, "Binaries", "SETTINGS");
        Directory.CreateDirectory(settingsDir);
        var file = Path.Combine(settingsDir, "GCMODSETTINGS.MXML");

        File.WriteAllText(file, """
<?xml version="1.0" encoding="utf-8"?>
<Data template="GcModSettings">
	<Property name="DisableAllMods" value="false" />
	<Property name="Data" value="GcModSettingsInfo" _index="0">
		<Property name="Name" value="MODA" />
		<Property name="Author" value="Someone" />
		<Property name="ModPriority" value="0" />
		<Property name="Enabled" value="true" />
	</Property>
	<Property name="Data" value="GcModSettingsInfo" _index="1">
		<Property name="Name" value="MODB" />
		<Property name="Author" value="" />
		<Property name="ModPriority" value="1" />
		<Property name="Enabled" value="true" />
	</Property>
	<Property name="Data" value="GcModSettingsInfo" _index="2">
		<Property name="Name" value="MODC" />
		<Property name="Author" value="" />
		<Property name="ModPriority" value="2" />
		<Property name="Enabled" value="false" />
	</Property>
</Data>
""", new UTF8Encoding(true));

        var service = new LoadOrderService();
        var read = service.Read(game);
        Check("读取 3 条优先级", read.Entries.Count == 3, read.Error);
        Check("按优先级排序", read.Entries[0].Name == "MODA" && read.Entries[2].Name == "MODC");
        Check("读取 Enabled", read.Entries.First(e => e.Name == "MODC").Enabled == false);

        var apply = service.Apply(game, new[] { "MODB", "MODA", "MODC" });
        Check("写入 3 条", apply.Updated == 3 && apply.Error == null, apply.Error);
        Check("生成备份", File.Exists(file + ".bak"));

        var after = service.Read(game);
        Check("MODB 变为 0", after.Entries.First(e => e.Name == "MODB").Priority == 0);
        Check("MODA 变为 1", after.Entries.First(e => e.Name == "MODA").Priority == 1);
        Check("其他字段保留", File.ReadAllText(file).Contains("Someone"));
        Check("排序结果", after.Entries[0].Name == "MODB");

        var missing = service.Apply(game, new[] { "MODA", "NOPE" });
        Check("找不到的模组被计数", missing.Updated == 1 && missing.Missing == 1,
            $"updated={missing.Updated} missing={missing.Missing}");

        Check("XML 名称转义", LoadOrderService.ToXmlName("a&b") == "A&amp;B");
    }

    // ---------- 日志 / 存档 / AMUMSS ----------

    private static void TestLogs()
    {
        Section("日志诊断");

        var game = NewGame("logs");
        var gamedata = Path.Combine(game, "GAMEDATA");
        Directory.CreateDirectory(gamedata);

        File.WriteAllText(Path.Combine(gamedata, "FullLog.txt"), """
[INFO] starting
[ERROR] Failed to load mod CoolMod (missing EXML)
[WARN] something
[ERROR] crash while reading CoolMod/Globals/GC.EXML
[ERROR] unrelated failure in renderer
""");

        var mods = new List<ModRecord> { Rec("CoolMod", "CoolMod", true) };
        var result = LogDiagnostics.Scan(game, mods);

        Check("找到日志文件", result.LogFiles.Count > 0);
        Check("识别报错行", result.Errors.Count >= 2, result.Errors.Count.ToString());
        Check("定位到模组", result.Suspects.ContainsKey("CoolMod"), string.Join(",", result.Suspects.Keys));
        Check("命中行数上限", result.Suspects["CoolMod"].Count == 2, result.Suspects["CoolMod"].Count.ToString());

        var empty = LogDiagnostics.Scan(NewGame("logs_empty"), mods);
        Check("没有日志时给出提示", empty.Note != null && empty.LogFiles.Count == 0);
    }

    private static void TestSaveReader()
    {
        Section("存档读取");

        var dir = NewDir("saves");
        var path = Path.Combine(dir, "save.hg");
        var json = Encoding.UTF8.GetBytes(
            "{\"F2P\":1234,\"8>q\":\"Win|Final\",\"<h0\":{\"Pk4\":\"TestSave\",\"Lg8\":3660}}");
        var block = Lz4LiteralBlock(json);

        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(0xFEEDA1E5u);
            writer.Write((uint)block.Length);
            writer.Write((uint)json.Length);
            writer.Write(0u);
            writer.Write(block);
        }

        var info = SaveGameReader.ReadOne(path, out var error);
        Check("读取存档头", info != null, error);
        Check("存档版本", info?.SaveVersion == 1234, info?.SaveVersion.ToString());
        Check("存档名", info?.SaveName == "TestSave", info?.SaveName);
        Check("平台", info?.Platform == "Win|Final", info?.Platform);
        Check("游玩时长", info?.PlayText == "1 小时 1 分", info?.PlayText);

        var scan = SaveGameReader.Scan(dir);
        Check("扫描目录", scan.Saves.Count == 1, scan.Error);

        Check("键名映射已加载", SaveKeyMap.Count > 1000, SaveKeyMap.Count.ToString());
        Check("映射 SaveName", SaveKeyMap.KeyOf("SaveName") == "Pk4");

        // 真实存档（本机存在时做一次只读验证）
        var realSave = GameLocator.FindSaveDir();
        if (!string.IsNullOrEmpty(realSave))
        {
            var real = SaveGameReader.Scan(realSave);
            if (real.Saves.Count > 0)
            {
                var newest = real.Saves[0];
                Check("真实存档可读出版本", newest.SaveVersion > 0, newest.SaveVersion.ToString());
                Check("真实存档可读出名字", newest.SaveName.Length > 0, newest.SaveName);
                Console.WriteLine($"     真实存档：{newest.FileName} · v{newest.SaveVersion} · {newest.SaveName} · {newest.PlayText}");
            }
        }
    }

    private static void TestAmumss()
    {
        Section("AMUMSS 集成");

        var amumss = NewDir("amumss");
        Directory.CreateDirectory(Path.Combine(amumss, "MODBUILDER"));
        File.WriteAllText(Path.Combine(amumss, "MODBUILDER", "AMUMSSVersion.txt"), "5.8.1.1w");
        File.WriteAllText(Path.Combine(amumss, "BUILDMOD.bat"), "@echo off\nset \"_BMver=5.8\"\n");
        File.WriteAllBytes(Path.Combine(amumss, "MBINCompiler.exe"), Array.Empty<byte>());

        var service = new AmumssService();
        var info = service.Inspect(amumss);
        Check("识别 AMUMSS 目录", info.IsValid);
        Check("读取 AMUMSS 版本", info.Version == "5.8.1.1w", info.Version);
        Check("读取 BUILDMOD 版本", info.BuildModVersion == "5.8", info.BuildModVersion);
        Check("检测编译器", info.HasCompiler);

        var modFolder = NewDir("amumssmod", "TestMod");
        File.WriteAllText(Path.Combine(modFolder, "script.lua"), "print('hi')");
        File.WriteAllText(Path.Combine(modFolder, "AMUMSS_v5.7.0.txt"), "");
        File.WriteAllText(Path.Combine(modFolder, "old.EXML"), "<Property name=\"Foo\" />");

        var plan = service.Analyze(modFolder, "TestMod", amumss);
        Check("发现 lua 源脚本", plan.LuaFiles.Count == 1);
        Check("读取构建标记", plan.BuiltWith == "5.7.0", plan.BuiltWith);
        Check("判断需要重建", plan.NeedsRebuild);
        Check("计划摘要", plan.Summary.Contains("lua"), plan.Summary);

        var copied = service.PrepareScripts(amumss, plan);
        Check("复制脚本到 ModScript", copied == 1 &&
                                  File.Exists(Path.Combine(amumss, "ModScript", "script.lua")));

        var args = service.BuildArguments("7.10", false);
        Check("构建命令含游戏版本", args.Contains("-GameVersion 7.10"), args);

        var output = Path.Combine(amumss, "CreatedMODS", "TestMod");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "new.EXML"), "<Property name=\"Bar\" />");

        var collect = service.Collect(amumss, plan);
        Check("收录 AMUMSS 输出", collect.Ok, collect.Error);
        Check("新输出已写入", File.Exists(Path.Combine(modFolder, "new.EXML")));
        Check("旧文件被替换", !File.Exists(Path.Combine(modFolder, "old.EXML")));
        Check("lua 源脚本保留", File.Exists(Path.Combine(modFolder, "script.lua")));
        Check("原目录已备份", collect.BackupPath != null && Directory.Exists(collect.BackupPath));
    }

    private static void TestLegacyMigration()
    {
        Section("旧版数据迁移");

        File.WriteAllText(Path.Combine(AppPaths.LegacyRoot, "settings.json"),
            "{\"GamePath\":\"E:\\\\SteamLibrary\\\\steamapps\\\\common\\\\No Man's Sky\",\"Theme\":\"Light\",\"NexusApiKey\":\"key123\",\"CreateDisableModsFile\":false}");

        File.WriteAllText(Path.Combine(AppPaths.LegacyRoot, "library.json"),
            """
{
  "Mods": [
    {
      "Id": "abc",
      "Name": "Old Mod",
      "Source": "本地导入",
      "Version": "7.01",
      "LibraryPath": "Old Mod",
      "DeployName": "Old Mod",
      "PakFiles": [],
      "Enabled": true,
      "Size": 12345,
      "ImportedAt": "/Date(1700000000000)/",
      "Note": ""
    }
  ]
}
""");

        var legacy = LegacyDataMigrator.Read();
        Check("发现旧版设置", legacy.SettingsFound && legacy.Settings != null);
        Check("读取旧版游戏路径", legacy.Settings!.GamePath.Contains("No Man's Sky"), legacy.Settings.GamePath);
        Check("读取旧版主题", legacy.Settings.Theme == "Light");
        Check("发现旧版库", legacy.LibraryFound);
        Check("迁移模组记录", legacy.Mods.Count == 1 && legacy.Mods[0].Name == "Old Mod");
        Check("迁移目录名", legacy.Mods[0].FolderName == "Old Mod");
        Check("解析 /Date()/", legacy.Mods[0].ImportedAt.Year == 2023, legacy.Mods[0].ImportedAt.ToString("O"));

        var library = new ModLibrary();
        FileOps.TryDeleteFile(AppPaths.DataFile); // 清掉前面的测试库，验证「无新库时自动迁移」
        library.Load();
        Check("库加载时自动迁移", library.Mods.Count == 1, library.LoadMessage);
    }

    // ---------- 工具 ----------

    private static void Section(string name) => Console.WriteLine("\n== " + name + " ==");

    private static void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine("  [PASS] " + name);
        }
        else
        {
            _failed++;
            Console.WriteLine("  [FAIL] " + name + (string.IsNullOrEmpty(detail) ? "" : "  ->  " + detail));
        }
    }

    private static string NewDir(params string[] parts)
    {
        var path = Path.Combine(new[] { _root }.Concat(parts).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    private static string NewGame(string name)
    {
        var game = NewDir("games", name);
        Directory.CreateDirectory(Path.Combine(game, "GAMEDATA", "PCBANKS"));
        return game;
    }

    private static ModRecord Rec(string name, string folder, bool enabled, params string[] paks) => new()
    {
        Name = name,
        FolderName = folder,
        Enabled = enabled,
        PakFiles = paks.ToList()
    };

    private static void AddZipEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    /// <summary>构造只含字面量的 LZ4 块（合法且可被解码器还原）。</summary>
    private static byte[] Lz4LiteralBlock(byte[] data)
    {
        using var stream = new MemoryStream();
        var length = data.Length;

        if (length < 15)
        {
            stream.WriteByte((byte)(length << 4));
        }
        else
        {
            stream.WriteByte(0xF0);
            var extra = length - 15;
            while (extra >= 255)
            {
                stream.WriteByte(255);
                extra -= 255;
            }
            stream.WriteByte((byte)extra);
        }

        stream.Write(data);
        return stream.ToArray();
    }
}

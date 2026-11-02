using System.Text;
using System.Xml.Linq;
using AllModsSky.Core;

// AllModsSky 自检：不依赖真实游戏目录，全部在临时目录里构造场景。
// 运行：dotnet run --project verify\AllModsSky.SelfCheck\AllModsSky.SelfCheck.csproj -c Release
// 约定：全部通过退出码 0；有失败退出码 1。

var suite = new CheckSuite();

// ───────────────────────── 1. 加载顺序 / BOM / 原子写 ─────────────────────────
suite.Section("LoadOrderService：读取、写优先级、BOM、备份、原子写");

var game = T.NewGameDir();
var settingsFile = new LoadOrderService().SettingsFile(game);
T.WriteMxml(settingsFile, bom: true);

var order = new LoadOrderService();
var read = order.Read(game);
suite.Check("Read 解析出 3 条", read.Ok && read.Entries.Count == 3, read.Error);
suite.Check("Read 按 ModPriority 排序",
    string.Join(",", read.Entries.Select(e => e.Name)) == "MODB,MODC,MODA");
suite.Check("Read 读出 Enabled 字段", read.Entries.Single(e => e.Name == "MODB").Enabled == false);

var apply = order.Apply(game, new[] { "MODC", "MODA", "MODB" });
suite.Check("Apply 更新 3 条优先级", apply.Ok && apply.Updated == 3 && apply.Missing == 0,
    $"Updated={apply.Updated} Missing={apply.Missing} Error={apply.Error}");

var afterApply = File.ReadAllBytes(settingsFile);
suite.Check("Apply 后 BOM 仍在", afterApply.Length >= 3 && afterApply[0] == 0xEF && afterApply[1] == 0xBB);
suite.Check("Apply 后行尾仍是 CRLF", Encoding.UTF8.GetString(afterApply).Contains("\r\n"));
suite.Check("Apply 前生成 .bak", File.Exists(settingsFile + ".bak"));
suite.Check("Apply 后没有残留 .tmp", Directory.GetFiles(Path.GetDirectoryName(settingsFile)!, "*.tmp").Length == 0);

var reread = order.Read(game);
suite.Check("Apply 后读回顺序正确",
    string.Join(",", reread.Entries.Select(e => $"{e.Name}:{e.Priority}")) == "MODC:0,MODA:1,MODB:2");
suite.Check("Apply 没有动 Author 等非目标字段",
    Prop(settingsFile, "MODA", "Author") == "Tester");

var bytesBefore = File.ReadAllBytes(settingsFile);
var applyAgain = order.Apply(game, new[] { "MODC", "MODA", "MODB" });
suite.Check("重复 Apply 报告 0 更新", applyAgain.Updated == 0);
suite.Check("重复 Apply 不重写文件", File.ReadAllBytes(settingsFile).SequenceEqual(bytesBefore));

var applyMissing = order.Apply(game, new[] { "MODC", "MODZ", "MODA", "MODB" });
suite.Check("Apply 统计缺失条目", applyMissing.Missing == 1);

T.WriteMxml(settingsFile, bom: false);
var applyNoBom = order.Apply(game, new[] { "MODA", "MODB", "MODC" });
suite.Check("无 BOM 的输入写回后不会凭空加 BOM", applyNoBom.Ok && File.ReadAllBytes(settingsFile)[0] != 0xEF);

var emptyGame = T.NewGameDir();
var readMissing = order.Read(emptyGame);
suite.Check("缺少 MXML 时 Read 报错而不是抛异常", !readMissing.Ok && readMissing.Error != null);

suite.Check("ToXmlName 转成大写并转义", LoadOrderService.ToXmlName("a&b") == "A&amp;B");

// ───────────────────────── 2. 游戏内 Enabled 开关写回 ─────────────────────────
suite.Section("LoadOrderService.ApplyEnabledStates：只改 Enabled");

T.WriteMxml(settingsFile, bom: true);
var enabledResult = order.ApplyEnabledStates(game, new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
{
    ["moda"] = false, // 大小写不敏感
    ["MODB"] = true,
    ["MODZ"] = true  // MXML 里没有，应计入 Missing
});

suite.Check("Enabled 改写 2 条（名字大小写不敏感）", enabledResult.Ok && enabledResult.Updated == 2,
    $"Updated={enabledResult.Updated} Error={enabledResult.Error}");
suite.Check("Enabled 缺失条目计入 Missing", enabledResult.Missing == 1);
suite.Check("MODA 的 Enabled 变成 false", Prop(settingsFile, "MODA", "Enabled") == "false");
suite.Check("MODB 的 Enabled 变成 true", Prop(settingsFile, "MODB", "Enabled") == "true");
suite.Check("MODC 的 Enabled 未被波及", Prop(settingsFile, "MODC", "Enabled") == "true");
suite.Check("改 Enabled 不动 ModPriority", Prop(settingsFile, "MODA", "ModPriority") == "2");
suite.Check("改 Enabled 不动 Author", Prop(settingsFile, "MODA", "Author") == "Tester");
suite.Check("改 Enabled 后 BOM 仍在", File.ReadAllBytes(settingsFile)[0] == 0xEF);
suite.Check("改 Enabled 前生成 .bak", File.Exists(settingsFile + ".bak"));

var noBomGame = T.NewGameDir();
var noBomFile = new LoadOrderService().SettingsFile(noBomGame);
T.WriteMxml(noBomFile, bom: false);
new LoadOrderService().ApplyEnabledStates(noBomGame, new Dictionary<string, bool> { ["MODA"] = false });
suite.Check("无 BOM 时改 Enabled 不会凭空加 BOM", File.ReadAllBytes(noBomFile)[0] != 0xEF);

var noXml = order.ApplyEnabledStates(emptyGame, new Dictionary<string, bool> { ["A"] = true });
suite.Check("缺少 MXML 时写 Enabled 返回错误", !noXml.Ok);

// ───────────────────────── 3. 管理器状态 vs 游戏状态对账 ─────────────────────────
suite.Section("ModStateService：检测漂移并按管理器状态修复");

T.WriteMxml(settingsFile, bom: true);
var modsDir = GameLocator.ModsDir(game);
var parkedDir = GameLocator.ParkedDir(game);
Directory.CreateDirectory(Path.Combine(modsDir, "ModA"));
Directory.CreateDirectory(Path.Combine(parkedDir, "ModB"));
Directory.CreateDirectory(Path.Combine(modsDir, "ModD"));

var records = new List<ModRecord>
{
    new() { Name = "ModA", FolderName = "ModA", Enabled = true },   // 文件夹在 MODS，游戏里却是关的
    new() { Name = "ModB", FolderName = "ModB", Enabled = false },  // 文件夹已停用，游戏里却还开着
    new() { Name = "ModD", FolderName = "ModD", Enabled = true },   // MXML 里还没有条目（游戏未登记）
    new() { Name = "ModPak", FolderName = "ModPak", Enabled = true, PakFiles = new List<string> { "legacy.pak" } }
};
order.ApplyEnabledStates(game, new Dictionary<string, bool> { ["MODA"] = false, ["MODB"] = true });

var states = new ModStateService();
var drifts = states.Detect(game, records);
suite.Check("检测到 3 处不一致", drifts.Count == 3, string.Join("；", drifts.Select(d => d.Text)));
suite.Check("ModA 判定为游戏内已关闭",
    drifts.Single(d => d.FolderName == "ModA").Kind == ModStateDrift.DisabledInGame);
suite.Check("ModB 判定为游戏内仍启用",
    drifts.Single(d => d.FolderName == "ModB").Kind == ModStateDrift.EnabledInGame);
suite.Check("ModD 判定为游戏尚未登记",
    drifts.Single(d => d.FolderName == "ModD").Kind == ModStateDrift.MissingInSettings);
suite.Check("旧式 pak 模组不参与 Enabled 对账", drifts.All(d => d.FolderName != "ModPak"));

var beforeReconcile = File.ReadAllBytes(settingsFile);
var dryRun = states.Reconcile(game, records, write: false);
suite.Check("只读模式发现差异但不写文件",
    dryRun.Drifts.Count == 3 && File.ReadAllBytes(settingsFile).SequenceEqual(beforeReconcile));

var repaired = states.Reconcile(game, records);
suite.Check("按管理器状态修复 2 条", repaired.Ok && repaired.Repaired == 2,
    $"Repaired={repaired.Repaired} Error={repaired.Error}");
suite.Check("未登记条目计入 Skipped", repaired.Skipped == 1);
suite.Check("修复后只剩 1 处（等游戏登记）", states.Detect(game, records).Count == 1);
suite.Check("修复后 BOM 仍在", File.ReadAllBytes(settingsFile)[0] == 0xEF);
suite.Check("修复后 ModPriority 未被改动", Prop(settingsFile, "MODA", "ModPriority") == "2");

// ───────────────────────── 4. 部署：文件夹搬迁 / DISABLEMODS / 外部模组 ─────────────────────────
suite.Section("DeploymentService：启停搬迁、DISABLEMODS.TXT、外部模组识别");

var game2 = T.NewGameDir();
var modsDir2 = GameLocator.ModsDir(game2);
var parkedDir2 = GameLocator.ParkedDir(game2);
Directory.CreateDirectory(Path.Combine(parkedDir2, "PackA")); // 应搬到 MODS
Directory.CreateDirectory(Path.Combine(modsDir2, "PackB"));   // 应搬到停用区

var packA = new ModRecord { Name = "PackA", FolderName = "PackA", Enabled = true };
var packB = new ModRecord { Name = "PackB", FolderName = "PackB", Enabled = false };
var deployment = new DeploymentService();

var deploy = deployment.Sync(game2, new[] { packA, packB }, createDisableFile: true);
suite.Check("Sync 启用 1 个、停用 1 个", deploy.Added == 1 && deploy.Removed == 1,
    $"Added={deploy.Added} Removed={deploy.Removed} Summary={deploy.Summary}");
suite.Check("PackA 已进入 MODS", Directory.Exists(Path.Combine(modsDir2, "PackA")));
suite.Check("PackB 已进入停用区", Directory.Exists(Path.Combine(parkedDir2, "PackB")));
suite.Check("还有启用模组时不写 DISABLEMODS.TXT", !File.Exists(GameLocator.DisableModsFile(game2)));
suite.Check("Presence 识别 MODS 里的模组", deployment.Presence(game2, packA) == ModPresence.InMods);
suite.Check("Presence 识别停用区里的模组", deployment.Presence(game2, packB) == ModPresence.Parked);

packA.Enabled = false;
var deployAllOff = deployment.Sync(game2, new[] { packA, packB }, createDisableFile: true);
suite.Check("全部停用时写入 DISABLEMODS.TXT",
    deployAllOff.Removed == 1 && File.Exists(GameLocator.DisableModsFile(game2)));

Directory.CreateDirectory(Path.Combine(modsDir2, "ExternalPack"));
var external = deployment.ListExternal(game2, new[] { packA, packB });
suite.Check("ListExternal 识别未托管的文件夹", external.Any(e => e.Name == "ExternalPack"));
suite.Check("ListExternal 不把托管模组算成外部模组", external.All(e => e.Name != "PackA" && e.Name != "PackB"));

suite.Check("游戏目录无效时 Sync 报失败而不是抛异常",
    deployment.Sync(Path.Combine(Path.GetTempPath(), "not-a-game-" + Guid.NewGuid().ToString("N")),
        new[] { packA }, createDisableFile: false).Failed == 1);

// ───────────────────────── 汇总 ─────────────────────────
suite.Finish();
return suite.Failed == 0 ? 0 : 1;

// ───────────────────────── 辅助函数 ─────────────────────────
static string? Prop(string file, string modName, string propName)
{
    var doc = XDocument.Load(file);
    var block = doc.Root?.Elements("Property")
        .FirstOrDefault(p => (string?)p.Attribute("name") == "Data"
                             && (string?)p.Attribute("value") == "GcModSettingsInfo"
                             && string.Equals((string?)p.Elements("Property")
                                 .FirstOrDefault(c => (string?)c.Attribute("name") == "Name")?.Attribute("value"),
                                 modName, StringComparison.OrdinalIgnoreCase));
    return (string?)block?.Elements("Property")
        .FirstOrDefault(c => (string?)c.Attribute("name") == propName)?.Attribute("value");
}

internal static class T
{
    public static string NewGameDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "allmodssky-selfcheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "GAMEDATA", "PCBANKS"));
        return root;
    }

    public static void WriteMxml(string file, bool bom)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var text = "<Data template=\"GcModSettings\">\r\n" + Block("MODA", 2, true) +
                   Block("MODB", 0, false) + Block("MODC", 1, true) + "</Data>\r\n";
        File.WriteAllText(file, text, new UTF8Encoding(bom));
    }

    private static string Block(string name, int priority, bool enabled) =>
        "\t<Property name=\"Data\" value=\"GcModSettingsInfo\">\r\n" +
        $"\t\t<Property name=\"Name\" value=\"{name}\" />\r\n" +
        "\t\t<Property name=\"Author\" value=\"Tester\" />\r\n" +
        "\t\t<Property name=\"ID\" value=\"1000\" />\r\n" +
        $"\t\t<Property name=\"ModPriority\" value=\"{priority}\" />\r\n" +
        $"\t\t<Property name=\"Enabled\" value=\"{(enabled ? "true" : "false")}\" />\r\n" +
        "\t\t<Property name=\"EnabledVR\" value=\"true\" />\r\n" +
        "\t</Property>\r\n";
}

internal sealed class CheckSuite
{
    private readonly List<string> _failures = new();

    public int Passed { get; private set; }
    public int Failed => _failures.Count;

    public void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("── " + title);
    }

    public void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
        {
            Passed++;
            Console.WriteLine("  [通过] " + name);
            return;
        }

        _failures.Add(name);
        Console.WriteLine("  [失败] " + name + (string.IsNullOrEmpty(detail) ? "" : "　→ " + detail));
    }

    public void Finish()
    {
        Console.WriteLine();
        Console.WriteLine($"结果：通过 {Passed}，失败 {Failed}");
        if (_failures.Count == 0) return;

        Console.WriteLine("失败项：");
        foreach (var failure in _failures) Console.WriteLine("  - " + failure);
    }
}

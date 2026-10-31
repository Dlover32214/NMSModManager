using System.Diagnostics;
using System.Text;

namespace AllModsSky.Core;

public sealed class AmumssInfo
{
    public string Path { get; init; } = "";
    public bool IsValid { get; init; }
    public string Version { get; init; } = "";
    public string BuildModVersion { get; init; } = "";
    public string CompilerVersion { get; init; } = "";
    public bool HasCompiler { get; init; }

    public string VersionLine
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Version)) parts.Add("AMUMSS " + Version);
            if (!string.IsNullOrEmpty(BuildModVersion)) parts.Add("BUILDMOD " + BuildModVersion);
            if (!string.IsNullOrEmpty(CompilerVersion)) parts.Add("MBINCompiler " + CompilerVersion);
            return string.Join(" · ", parts);
        }
    }
}

public sealed class RepairPlan
{
    public string Folder { get; init; } = "";
    public string ModName { get; init; } = "";
    public List<string> LuaFiles { get; init; } = new();
    public int Exml { get; init; }
    public int Mbin { get; init; }
    public string BuiltWith { get; init; } = "";
    public string Installed { get; init; } = "";
    public bool NeedsRebuild { get; init; }

    public string Summary
    {
        get
        {
            var kind = Exml > 0 && Mbin > 0 ? "混合" : Exml > 0 ? "EXML 补丁" : Mbin > 0 ? "MBIN 替换" : "空";
            var source = LuaFiles.Count > 0
                ? $"有 {LuaFiles.Count} 个 lua 源脚本（可重建）"
                : "没有 lua 源脚本（只能手动重做）";
            var built = string.IsNullOrEmpty(BuiltWith) ? "构建标记未知" : "构建标记 " + BuiltWith;
            return $"{kind} · {source} · {built}";
        }
    }
}

public sealed class CollectResult
{
    public int Copied { get; init; }
    public string? Error { get; init; }
    public string? BackupPath { get; init; }
    public bool Ok => Error == null && Copied > 0;
}

/// <summary>
/// AMUMSS 集成：识别安装位置、读版本、按当前游戏版本重新生成模组。
/// 流程：复制 lua 到 ModScript → 运行 BUILDMOD.bat（可见控制台）→ 从 CreatedMODS 收录结果。
/// </summary>
public sealed class AmumssService
{
    private static readonly string[] Candidates =
    {
        @"D:\AMUMSS",
        @"C:\AMUMSS",
        @"E:\AMUMSS",
        @"D:\Modding\AMUMSS"
    };

    public string? DetectPath(string? configured = null)
    {
        if (!string.IsNullOrWhiteSpace(configured) && IsValid(configured)) return configured;

        foreach (var candidate in Candidates)
            if (IsValid(candidate)) return candidate;

        try
        {
            foreach (var drive in Directory.GetLogicalDrives())
            {
                foreach (var relative in new[] { "AMUMSS", @"Games\AMUMSS", @"Modding\AMUMSS" })
                {
                    var probe = Path.Combine(drive, relative);
                    if (IsValid(probe)) return probe;
                }
            }
        }
        catch { }

        return null;
    }

    public bool IsValid(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        try
        {
            return Directory.Exists(dir) &&
                   (File.Exists(Path.Combine(dir, "BUILDMOD.bat")) ||
                    File.Exists(Path.Combine(dir, "MBINCompiler.exe")));
        }
        catch
        {
            return false;
        }
    }

    public AmumssInfo Inspect(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return new AmumssInfo { Path = dir ?? "", IsValid = false };

        var compiler = FindCompiler(dir);
        var buildMod = "";
        try
        {
            var bat = Path.Combine(dir, "BUILDMOD.bat");
            if (File.Exists(bat))
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    File.ReadAllText(bat), "set\\s+\"_BMver=([^\"]+)\"");
                if (match.Success) buildMod = match.Groups[1].Value;
            }
        }
        catch { }

        var compilerVersion = "";
        try
        {
            if (compiler != null)
                compilerVersion = FileVersionInfo.GetVersionInfo(compiler).FileVersion ?? "";
        }
        catch { }

        return new AmumssInfo
        {
            Path = dir,
            IsValid = IsValid(dir),
            Version = VersionOf(dir),
            BuildModVersion = buildMod,
            CompilerVersion = compilerVersion,
            HasCompiler = compiler != null
        };
    }

    public string VersionOf(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return "";
        try
        {
            var file = Path.Combine(dir, "MODBUILDER", "AMUMSSVersion.txt");
            if (File.Exists(file)) return File.ReadAllText(file).Trim();
        }
        catch { }
        return "";
    }

    public string? FindCompiler(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        foreach (var probe in new[]
                 {
                     Path.Combine(dir, "MBINCompiler.exe"),
                     Path.Combine(dir, "MODBUILDER", "MBINCompiler.exe")
                 })
        {
            if (File.Exists(probe)) return probe;
        }
        return null;
    }

    /// <summary>拿真实 MBIN 试编译，判断编译器认不认当前版本。0=识别，1=过旧，2=无法判断。</summary>
    public async Task<(int Code, string Detail)> ProbeCompilerAsync(
        string? amumssPath, string? sampleMbin, CancellationToken cancellation = default)
    {
        var exe = FindCompiler(amumssPath);
        if (exe == null) return (2, "找不到 MBINCompiler.exe");
        if (string.IsNullOrWhiteSpace(sampleMbin) || !File.Exists(sampleMbin))
            return (2, "没有可用于自检的 MBIN 样本");

        var tempDir = Path.Combine(Path.GetTempPath(), "ams_probe_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(tempDir);
            var sample = Path.Combine(tempDir, "probe.MBIN");
            File.Copy(sampleMbin, sample, true);

            var psi = new ProcessStartInfo(exe, $"\"{sample}\"")
            {
                WorkingDirectory = tempDir,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null) return (2, "无法启动 MBINCompiler");

            var output = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                return (2, "自检超时");
            }

            var text = output.ToString();
            if (text.Contains("not recognized", StringComparison.OrdinalIgnoreCase))
                return (1, "MBINCompiler 不认识这个文件（版本过旧）");
            if (text.Contains("files converted", StringComparison.OrdinalIgnoreCase))
                return (0, "MBINCompiler 可以识别当前版本的 MBIN");

            return (2, "自检结果不明：" + (text.Length > 200 ? text[..200] : text));
        }
        catch (Exception ex)
        {
            return (2, ex.Message);
        }
        finally
        {
            FileOps.TryDeleteDirectory(tempDir);
        }
    }

    public RepairPlan Analyze(string folder, string modName, string? amumssPath)
    {
        var luaFiles = new List<string>();
        var builtWith = "";
        var exml = 0;
        var mbin = 0;

        try
        {
            if (Directory.Exists(folder))
            {
                luaFiles.AddRange(Directory.GetFiles(folder, "*.lua", SearchOption.AllDirectories));

                var info = ModInspector.Inspect(folder);
                exml = info.Exml;
                mbin = info.Mbin;

                var marker = Directory.GetFiles(folder, "AMUMSS_v*.txt", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (marker != null)
                    builtWith = Path.GetFileNameWithoutExtension(marker).Replace("AMUMSS_v", "", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("分析 AMUMSS 修复计划失败：" + ex.Message);
        }

        var installed = VersionOf(amumssPath);
        var needsRebuild = !string.IsNullOrEmpty(builtWith) && !string.IsNullOrEmpty(installed) &&
                           !string.Equals(builtWith, installed, StringComparison.OrdinalIgnoreCase);

        return new RepairPlan
        {
            Folder = folder,
            ModName = modName,
            Exml = exml,
            Mbin = mbin,
            BuiltWith = builtWith,
            Installed = installed,
            NeedsRebuild = needsRebuild,
            LuaFiles = luaFiles
        };
    }

    public int PrepareScripts(string amumssPath, RepairPlan plan)
    {
        if (string.IsNullOrWhiteSpace(amumssPath)) return 0;

        var scriptDir = Path.Combine(amumssPath, "ModScript");
        try { Directory.CreateDirectory(scriptDir); }
        catch (Exception ex)
        {
            AppLog.Warn("创建 AMUMSS ModScript 失败：" + ex.Message);
            return 0;
        }

        var copied = 0;
        foreach (var lua in plan.LuaFiles)
        {
            try
            {
                File.Copy(lua, Path.Combine(scriptDir, Path.GetFileName(lua)), true);
                copied++;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"复制脚本失败 {lua}：{ex.Message}");
            }
        }

        return copied;
    }

    public string BuildArguments(string? gameVersion, bool allowCompilerUpdate)
    {
        var version = string.IsNullOrWhiteSpace(gameVersion) ? "5.60" : gameVersion.Trim();
        var args = new List<string>
        {
            "-GameVersion " + version,
            "-AutoUpdateAMUMSS N",
            "-AutoUpdateMBinCompiler " + (allowCompilerUpdate ? "Y" : "N"),
            "-CombineModPak N",
            "-CopyToGamefolder N",
            "-DEV_MODE N",
            "-UseExtraFilesInPAK N",
            "-UseLuaScriptInPak N",
            "-FileStructureLevel 1",
            "-CheckForModConflicts M",
            "-TestScript Y",
            "-SHOWOPTIONS N",
            "-SHOWSECTIONS N",
            "-SHOWEXTRASECTIONS N",
            "-SOUND N"
        };
        return string.Join(" ", args);
    }

    /// <summary>在独立控制台里运行 BUILDMOD.bat（AMUMSS 需要交互时用户能回答）。</summary>
    public Process? StartBuild(string amumssPath, string? gameVersion, bool allowCompilerUpdate)
    {
        var bat = Path.Combine(amumssPath, "BUILDMOD.bat");
        if (!File.Exists(bat)) throw new FileNotFoundException("找不到 BUILDMOD.bat", bat);

        var inner = $"\"{bat}\" {BuildArguments(gameVersion, allowCompilerUpdate)} & pause";
        var psi = new ProcessStartInfo("cmd.exe", "/c \"" + inner + "\"")
        {
            WorkingDirectory = amumssPath,
            UseShellExecute = true
        };

        return Process.Start(psi);
    }

    /// <summary>把 AMUMSS 的输出收录回模组目录（原目录先备份）。</summary>
    public CollectResult Collect(string? amumssPath, RepairPlan plan)
    {
        if (string.IsNullOrWhiteSpace(amumssPath) || !Directory.Exists(plan.Folder))
            return new CollectResult { Error = "AMUMSS 目录或模组目录不存在" };

        var nameKey = Normalize(plan.ModName);
        var candidates = new List<string>();

        try
        {
            foreach (var root in new[] { "CreatedMODS", "CreatedModPAKs" })
            {
                var dir = Path.Combine(amumssPath, root);
                if (!Directory.Exists(dir)) continue;

                candidates.AddRange(Directory.GetDirectories(dir)
                    .Where(d => NameMatches(Normalize(Path.GetFileName(d)), nameKey)));

                candidates.AddRange(Directory.GetFiles(dir, "*.pak")
                    .Where(f => NameMatches(Normalize(Path.GetFileNameWithoutExtension(f)), nameKey)));
            }
        }
        catch (Exception ex)
        {
            return new CollectResult { Error = ex.Message };
        }

        candidates = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (candidates.Count == 0)
            return new CollectResult { Error = "AMUMSS 的 CreatedMODS / CreatedModPAKs 里没找到匹配的输出" };

        var backup = plan.Folder.TrimEnd('\\') + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var moved = false;
        var copied = 0;

        try
        {
            if (Directory.Exists(plan.Folder))
            {
                Directory.Move(plan.Folder, backup);
                moved = true;
            }

            Directory.CreateDirectory(plan.Folder);

            foreach (var source in candidates)
            {
                try
                {
                    if (File.Exists(source))
                    {
                        File.Copy(source, Path.Combine(plan.Folder, Path.GetFileName(source)), true);
                        copied++;
                    }
                    else if (Directory.Exists(source))
                    {
                        FileOps.CopyTree(source, plan.Folder);
                        copied++;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"收录输出失败 {source}：{ex.Message}");
                }
            }

            if (copied == 0)
            {
                if (moved && Directory.Exists(backup))
                {
                    FileOps.TryDeleteDirectory(plan.Folder);
                    Directory.Move(backup, plan.Folder);
                }
                return new CollectResult { Error = "复制输出失败，已恢复原目录" };
            }

            // 把原目录里的 lua 源脚本补回来
            if (moved && Directory.Exists(backup))
            {
                foreach (var lua in Directory.GetFiles(backup, "*.lua", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(plan.Folder, Path.GetFileName(lua));
                    if (!File.Exists(target))
                    {
                        try { File.Copy(lua, target, true); }
                        catch { }
                    }
                }
            }

            return new CollectResult { Copied = copied, BackupPath = moved ? backup : null };
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "收录 AMUMSS 输出");
            return new CollectResult { Error = ex.Message, BackupPath = moved ? backup : null };
        }
    }

    private static bool NameMatches(string candidate, string key) =>
        candidate.Length > 0 && key.Length > 0 &&
        (candidate.Contains(key, StringComparison.OrdinalIgnoreCase) ||
         key.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).ToArray());
}

using System.Diagnostics;
using System.Management;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;

namespace BgWatch;

/// <summary>
/// 一次扫描：把"会自己跑起来的东西"全捞出来，并挂上它们此刻的真实占用。
/// 设计目标只有一个 —— 让用户打开就看到"谁在占资源、为什么、关了会怎样"。
/// </summary>
public static class Scanner
{
    public sealed record ProcSample(string Path, long MemMb, double Cpu, int Pid);

    public static List<Item> Scan(Action<string>? progress = null)
    {
        progress?.Invoke("采样进程占用…");
        var procs = SampleProcesses();

        var byPath = new Dictionary<string, (long Mem, double Cpu, int Count)>(StringComparer.Ordinal);
        var byPid = new Dictionary<int, ProcSample>();
        foreach (var p in procs)
        {
            byPid[p.Pid] = p;
            var key = Util.Norm(p.Path);
            if (key.Length == 0) continue;
            byPath.TryGetValue(key, out var agg);
            byPath[key] = (agg.Mem + p.MemMb, agg.Cpu + p.Cpu, agg.Count + 1);
        }

        var items = new List<Item>();

        progress?.Invoke("读取服务…");
        items.AddRange(ReadServices(byPid, byPath));

        progress?.Invoke("读取登录启动项…");
        items.AddRange(ReadRunEntries(byPath));

        progress?.Invoke("读取计划任务…");
        items.AddRange(ReadTasks(byPath));

        progress?.Invoke("读取启动文件夹…");
        items.AddRange(ReadStartupFolder(byPath));

        progress?.Invoke("整理…");
        DeDuplicate(items);
        return items
            .GroupBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(i => i.MemMb)
            .ThenByDescending(i => i.CpuPct)
            .ToList();
    }

    // ---------------------------------------------------------------- 进程采样

    /// <summary>
    /// 进程采样。
    /// 路径/内存走 WMI —— 因为 MSIX 打包应用（Claude / Codex 这类）用 Process.MainModule
    /// 会因权限被拒，拿不到路径，等于把它们的占用算成 0。
    /// CPU 走 Process.TotalProcessorTime（句柄级，比再扫一遍 WMI 便宜一个数量级）。
    /// </summary>
    private static List<ProcSample> SampleProcesses(int sampleMs = 250)
    {
        var ticks1 = CpuTicks();
        Thread.Sleep(sampleMs);
        var ticks2 = CpuTicks();

        var cores = Math.Max(1, Environment.ProcessorCount);
        var elapsed = Math.Max(0.05, sampleMs / 1000.0);
        var list = new List<ProcSample>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ExecutablePath, WorkingSetSize FROM Win32_Process");

            foreach (ManagementObject mo in searcher.Get())
            {
                try
                {
                    var pid = Convert.ToInt32(mo["ProcessId"] ?? 0);
                    if (pid == 0) continue;

                    double cpu = 0;
                    if (ticks1.TryGetValue(pid, out var a) && ticks2.TryGetValue(pid, out var b) && b >= a)
                        cpu = (b - a) / (elapsed * 10_000_000.0 * cores) * 100.0;

                    list.Add(new ProcSample(
                        (mo["ExecutablePath"] as string) ?? "",
                        Convert.ToInt64(mo["WorkingSetSize"] ?? 0L) / (1024 * 1024),
                        Math.Round(cpu, 2),
                        pid));
                }
                catch { }
                finally { mo.Dispose(); }
            }
        }
        catch { }

        return list;
    }

    private static Dictionary<int, ulong> CpuTicks()
    {
        var d = new Dictionary<int, ulong>(512);
        foreach (var p in Process.GetProcesses())
        {
            try { d[p.Id] = (ulong)p.TotalProcessorTime.Ticks; }
            catch { }
            finally { p.Dispose(); }
        }
        return d;
    }

    // ---------------------------------------------------------------- 服务

    private static IEnumerable<Item> ReadServices(
        Dictionary<int, ProcSample> byPid,
        Dictionary<string, (long Mem, double Cpu, int Count)> byPath)
    {
        var result = new List<Item>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name,DisplayName,Description,PathName,StartMode,State,ProcessId FROM Win32_Service");

            foreach (ManagementObject mo in searcher.Get())
            {
                try
                {
                    var name = (mo["Name"] as string) ?? "";
                    if (name.Length == 0) continue;

                    var startMode = (mo["StartMode"] as string) ?? "";
                    // 只保留"会自己起来"的和"已经被关掉的"；手动启动的服务不是默认后台负担
                    if (startMode.Equals("Manual", StringComparison.OrdinalIgnoreCase)) continue;

                    var pathName = (mo["PathName"] as string) ?? "";
                    var exe = Util.ExeFromCommandLine(pathName);
                    var pid = Convert.ToInt32(mo["ProcessId"] ?? 0);

                    var item = new Item
                    {
                        Key = "svc:" + name.ToLowerInvariant(),
                        Kind = Kind.Service,
                        Name = name,
                        Source = name,
                        Exe = exe,
                        Enabled = !startMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase),
                    };

                    var display = (mo["DisplayName"] as string) ?? "";
                    if (display.Length > 0) item.Name = display;

                    item.Packaged = (pathName).Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
                    item.Publisher = Util.Vendor(exe);

                    var desc = (mo["Description"] as string) ?? "";
                    item.What = BuildServiceWhat(name, display, exe, desc, item.Publisher, item.Packaged);
                    ClassifyService(item, name, exe);

                    // 占用：共享宿主（svchost）不虚报
                    var exeLower = (exe ?? "").ToLowerInvariant();
                    var isSvcHost = exeLower.EndsWith(@"\svchost.exe");
                    if (isSvcHost)
                    {
                        item.SharedHost = true;
                        item.Pids = pid > 0 ? 1 : 0;
                    }
                    else if (pid > 0 && byPid.TryGetValue(pid, out var ps))
                    {
                        item.MemMb = ps.MemMb;
                        item.CpuPct = ps.Cpu;
                        item.Pids = 1;
                    }
                    else
                    {
                        ApplyByPath(item, byPath);
                    }

                    result.Add(item);
                }
                catch { }
                finally { mo.Dispose(); }
            }
        }
        catch
        {
            // WMI 不可用时至少让其它来源还能显示
        }

        return result;
    }

    private static string BuildServiceWhat(string name, string display, string? exe, string desc,
        string publisher, bool packaged)
    {
        var who = publisher.Length > 0 ? publisher : (packaged ? "MSIX 打包应用" : "未知发布者");
        var tail = desc.Length > 0 && desc.Length <= 80 ? $"：{desc}" : "";
        var host = exe is null ? "" : $"（{Path.GetFileName(exe)}）";
        return $"{who} 的服务{host}{tail}";
    }

    private static void ClassifyService(Item item, string name, string? exe)
    {
        var exeLower = (exe ?? "").ToLowerInvariant();

        if (ServiceRules.Critical.Contains(name))
        {
            item.Safety = Safety.Critical;
            item.SystemManaged = true;
            item.SystemReason = "Windows 核心服务";
            item.Impact = "系统核心服务，关掉可能导致无法开机 / 无网络 / 无声音";
            return;
        }

        if (exeLower.Contains(@"\system32\driverstore\"))
        {
            item.Safety = Safety.Caution;
            item.SystemManaged = true;
            item.SystemReason = "硬件驱动配套";
            item.Impact = "硬件驱动配套服务，关掉会导致对应硬件功能或热键失效";
            return;
        }

        if (ServiceRules.LooksLikeSecurity(item.Publisher, exe))
        {
            item.Safety = Safety.Caution;
            item.SystemManaged = true;
            item.SystemReason = "安全防护";
            item.Impact = "安全软件组件：关掉会降低防护能力（杀毒/主动防御），建议保留";
            return;
        }

        if (ServiceRules.IsWindowsOwned(item.Publisher, exe))
        {
            item.SystemManaged = true;
            item.SystemReason = "Windows 组件";
        }

        if (item.Packaged)
        {
            item.Safety = Safety.Caution;
            item.Impact = "改为手动：开机不启动。⚠ 该服务来自 MSIX 应用，应用更新后会重置";
            return;
        }

        if (item.Publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            item.Safety = Safety.Caution;
            item.Impact = "微软组件，关掉可能影响某个 Windows 功能";
            return;
        }

        item.Safety = Safety.Safe;
        item.Impact = "改为手动：开机不启动，需要时程序仍可自己拉起它";
    }

    // ---------------------------------------------------------------- 注册表 Run

    private static readonly (RegistryHive Hive, string Path, string Label)[] RunKeys =
    {
        (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKCU"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM"),
    };

    private static IEnumerable<Item> ReadRunEntries(
        Dictionary<string, (long Mem, double Cpu, int Count)> byPath)
    {
        var approved = LoadApproved();
        var result = new List<Item>();

        foreach (var (hive, path, label) in RunKeys)
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(path);
                    if (key is null) continue;

                    foreach (var valueName in key.GetValueNames())
                    {
                        if (string.IsNullOrWhiteSpace(valueName)) continue;
                        if (key.GetValue(valueName) is not string cmd || cmd.Length == 0) continue;

                        var exe = Util.ExeFromCommandLine(cmd);
                        var item = new Item
                        {
                            Key = $"run:{(hive == RegistryHive.CurrentUser ? "hkcu" : "hklm")}:{valueName}".ToLowerInvariant(),
                            Kind = Kind.Run,
                            Name = valueName,
                            Source = $"{label}\\{path}|{valueName}",
                            Exe = exe,
                            Enabled = approved.TryGetValue(valueName, out var on) ? on : true,
                            Publisher = Util.Vendor(exe),
                        };

                        item.What = item.Publisher.Length > 0
                            ? $"{item.Publisher} 的登录启动项"
                            : "登录启动项（无签名信息）";

                        var exists = exe is not null && File.Exists(exe);
                        if (!exists)
                        {
                            item.Safety = Safety.Safe;
                            item.Impact = "目标文件已不存在 —— 卸载残留，可以放心关掉";
                        }
                        else if (ServiceRules.LooksLikeSecurity(item.Publisher, exe))
                        {
                            item.Safety = Safety.Caution;
                            item.SystemManaged = true;
                            item.SystemReason = "安全防护";
                            item.Impact = "安全软件组件：关掉会降低防护能力，建议保留";
                        }
                        else if (ServiceRules.IsWindowsOwned(item.Publisher, exe))
                        {
                            item.Safety = Safety.Caution;
                            item.SystemManaged = true;
                            item.SystemReason = "Windows 组件";
                            item.Impact = "微软组件，关掉可能少个系统功能";
                        }
                        else
                        {
                            item.Safety = Safety.Safe;
                            item.Impact = "不再随登录自动启动，随时可以手动打开";
                        }

                        ApplyByPath(item, byPath);
                        result.Add(item);
                    }
                }
                catch { }
            }
        }

        return result;
    }

    private static Dictionary<string, bool> LoadApproved()
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, sub) in new (RegistryHive, string)[]
                 {
                     (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
                     (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
                 })
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(sub);
                if (key is null) continue;
                foreach (var name in key.GetValueNames())
                {
                    if (key.GetValue(name) is byte[] { Length: > 0 } data)
                        map[name] = data[0] % 2 == 0;   // 0x02 启用 / 0x03 禁用
                }
            }
            catch { }
        }
        return map;
    }

    // ---------------------------------------------------------------- 计划任务

    private static IEnumerable<Item> ReadTasks(
        Dictionary<string, (long Mem, double Cpu, int Count)> byPath)
    {
        var result = new List<Item>();
        try
        {
            using var ts = new TaskService();
            foreach (var task in ts.AllTasks)
            {
                try
                {
                    var uri = (task.Path ?? "").TrimStart('\\');
                    if (uri.Length == 0) continue;

                    var exec = task.Definition?.Actions?.OfType<ExecAction>().FirstOrDefault();
                    var cmd = exec?.Path;
                    if (exec is not null && !string.IsNullOrWhiteSpace(exec.Arguments))
                        cmd = $"{cmd} {exec.Arguments}";

                    var exe = Util.ExeFromCommandLine(cmd);
                    var item = new Item
                    {
                        Key = "task:" + uri.ToLowerInvariant(),
                        Kind = Kind.Task,
                        Name = uri,
                        Source = uri,
                        Exe = exe,
                        Enabled = task.Enabled,
                        Publisher = Util.Vendor(exe),
                    };

                    var isMs = uri.StartsWith("Microsoft\\", StringComparison.OrdinalIgnoreCase);
                    var isTelemetry = uri.Contains("feedback", StringComparison.OrdinalIgnoreCase)
                                      || uri.Contains("ceip", StringComparison.OrdinalIgnoreCase);

                    item.What = isMs ? "微软内置计划任务" : "第三方计划任务";
                    if (exe is not null) item.What += $"（{Path.GetFileName(exe)}）";

                    if (isMs)
                    {
                        item.SystemManaged = true;
                        item.SystemReason = "Windows 内置任务";
                    }

                    if (isTelemetry)
                    {
                        item.Safety = Safety.Safe;
                        item.Impact = "遥测 / 客户体验改善任务，关掉不影响系统功能";
                    }
                    else if (isMs)
                    {
                        item.Safety = Safety.Caution;
                        item.Impact = "微软维护任务，关掉前请确认具体用途";
                    }
                    else
                    {
                        item.Safety = Safety.Safe;
                        item.Impact = "不再按计划自动触发";
                    }

                    ApplyByPath(item, byPath);
                    result.Add(item);
                }
                catch { }
            }
        }
        catch { }

        return result;
    }

    // ---------------------------------------------------------------- 启动文件夹

    private static IEnumerable<Item> ReadStartupFolder(
        Dictionary<string, (long Mem, double Cpu, int Count)> byPath)
    {
        var result = new List<Item>();

        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
        };

        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    var name = Path.GetFileName(file);
                    if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                    var exe = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                        ? Util.ResolveShortcut(file)
                        : file;

                    var item = new Item
                    {
                        Key = "folder:" + Util.Norm(file),
                        Kind = Kind.Folder,
                        Name = name,
                        Source = file,
                        Exe = exe,
                        Enabled = true,
                        Publisher = Util.Vendor(exe),
                    };

                    item.What = exe is null
                        ? "启动文件夹里的快捷方式（目标无法解析，可能是 UWP 应用）"
                        : $"启动文件夹快捷方式 → {Path.GetFileName(exe)}";

                    item.Safety = Safety.Safe;
                    item.Impact = "移到隔离目录，不删除；可以一键还原";

                    if (ServiceRules.IsWindowsOwned(item.Publisher, exe))
                    {
                        item.SystemManaged = true;
                        item.SystemReason = "Windows 组件";
                    }

                    ApplyByPath(item, byPath);
                    result.Add(item);
                }
            }
            catch { }
        }

        return result;
    }

    // ---------------------------------------------------------------- 公共

    /// <summary>
    /// 同一个 exe 可能同时被服务 + 登录项 + 计划任务引用（比如 AweSun）。
    /// 如果每行都报同一个进程的内存，合计会翻倍 —— 那是在骗人。
    /// 规则：每个 exe 只让一条报数字（登录项优先，名字更像用户认知的软件名），
    /// 其余显示"同「X」"，合计才不会虚高。
    /// </summary>
    private static void DeDuplicate(List<Item> items)
    {
        var priority = new Dictionary<Kind, int>
        {
            [Kind.Run] = 0,
            [Kind.Folder] = 1,
            [Kind.Task] = 2,
            [Kind.Service] = 3,
        };

        var claimed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var item in items.Where(i => i.MemMb > 0)
                                  .OrderBy(i => priority.TryGetValue(i.Kind, out var p) ? p : 9))
        {
            var key = Util.Norm(item.Exe);
            if (key.Length == 0) continue;

            if (claimed.TryGetValue(key, out var owner))
            {
                item.SharedWith = owner;
                item.MemMb = 0;
                item.CpuPct = 0;
            }
            else
            {
                claimed[key] = item.Name;
            }
        }
    }

    private static void ApplyByPath(Item item, Dictionary<string, (long Mem, double Cpu, int Count)> byPath)
    {
        var key = Util.Norm(item.Exe);
        if (key.Length == 0 || !byPath.TryGetValue(key, out var agg)) return;
        item.MemMb = agg.Mem;
        item.CpuPct = Math.Round(agg.Cpu, 2);
        item.Pids = agg.Count;
    }
}

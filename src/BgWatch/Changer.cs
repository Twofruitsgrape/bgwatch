using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;

namespace BgWatch;

/// <summary>
/// 修改 + 撤销。
/// 原则：每次动手前先把原值落到 undo.json；撤销就是按倒序把原值写回去。
/// 不做"批次"，不做"日志系统" —— 一个文件、一个按钮。
/// </summary>
public static class Changer
{
    private const string ApprovedPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "bgwatch");

    private static readonly string UndoPath = Path.Combine(Dir, "undo.json");
    private static readonly string Quarantine = Path.Combine(Dir, "quarantine");

    public sealed class Record
    {
        public string Kind { get; set; } = "";
        public string Key { get; set; } = "";
        public string Old { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTime At { get; set; } = DateTime.Now;
    }

    public static List<Record> Load()
    {
        try
        {
            if (!File.Exists(UndoPath)) return new List<Record>();
            return JsonSerializer.Deserialize<List<Record>>(File.ReadAllText(UndoPath)) ?? new List<Record>();
        }
        catch { return new List<Record>(); }
    }

    private static void Save(List<Record> records)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(UndoPath, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static int Count => Load().Count;

    // ---------------------------------------------------------------- 关掉

    /// <summary>成功返回 null，失败返回给用户看的错误。</summary>
    public static string? Disable(Item item)
    {
        // 空操作守卫：已经是关闭状态就不要再写一次，否则 undo 里会多出一条"改了其实没改"的记录
        if (!item.Enabled) return "已经是关闭状态，无需修改";

        try
        {
            return item.Kind switch
            {
                Kind.Service => DisableService(item),
                Kind.Run => DisableRun(item),
                Kind.Task => DisableTask(item),
                Kind.Folder => DisableFolder(item),
                _ => "不支持的类型",
            };
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string? DisableService(Item item)
    {
        if (!Util.IsElevated()) return "改服务需要管理员权限";
        var start = ReadServiceStart(item.Source);
        if (start is null) return "读不到服务的启动类型";
        if (start == 4) return "该服务已经是禁用状态";
        if (start is 0 or 1) return "引导/系统级服务不允许改";

        var rc = RunSc("config", item.Source, "start=", "demand");
        if (rc != 0) return $"sc 返回错误码 {rc}";

        Add(new Record { Kind = "svc", Key = item.Source, Old = start.Value.ToString(), Name = item.Name });
        return null;
    }

    private static int? ReadServiceStart(string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
            return key?.GetValue("Start") as int?;
        }
        catch { return null; }
    }

    /// <summary>
    /// 关掉登录启动项：**写 Explorer\StartupApproved\Run，绝不碰 Run 键本身**。
    /// 早期版本错把标记写进了 Run 键，会把命令字符串覆盖成二进制 —— 这里是红线。
    /// </summary>
    private static string? DisableRun(Item item)
    {
        var (hive, runPath, valueName) = ParseRunSource(item.Source);
        if (valueName.Length == 0) return "启动项来源解析失败";

        // 防御：确认 Run 里的值仍是命令字符串。不是就立刻停手，绝不覆盖。
        using (var check = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(runPath))
        {
            if (check?.GetValue(valueName) is not string)
                return "该启动项的值已不是命令字符串，已中止（避免破坏）";
        }

        using var approvedKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64)
            .CreateSubKey(ApprovedPath, writable: true);
        if (approvedKey is null) return "打不开 StartupApproved 键";

        var old = approvedKey.GetValue(valueName) as byte[];
        var data = new byte[12];
        data[0] = 0x03;
        BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);

        Add(new Record
        {
            Kind = "run",
            Key = $"{hive}|{valueName}",
            Old = old is null ? "" : Convert.ToBase64String(old),
            Name = item.Name,
        });

        approvedKey.SetValue(valueName, data, RegistryValueKind.Binary);
        return null;
    }

    private static string? DisableTask(Item item)
    {
        using var ts = new TaskService();
        var task = ts.GetTask(item.Source);
        if (task is null) return "找不到该计划任务";

        var old = task.Enabled;
        if (!old) return "该任务已经是禁用状态";

        Add(new Record { Kind = "task", Key = item.Source, Old = old.ToString(), Name = item.Name });
        task.Enabled = false;
        return null;
    }

    private static string? DisableFolder(Item item)
    {
        var src = item.Source;
        if (!File.Exists(src)) return "文件已不存在";

        var folder = Path.GetDirectoryName(src);
        if (folder is null || !CanWrite(folder))
            return "没有该启动文件夹的写入权限 —— 移走后将无法还原，已中止";

        var dest = Path.Combine(Quarantine, DateTime.Now.ToString("yyyyMMdd-HHmmss"), Path.GetFileName(src));
        Add(new Record { Kind = "folder", Key = src, Old = dest, Name = item.Name });

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Move(src, dest);
        return null;
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".bgwatch-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    // ---------------------------------------------------------------- 还原

    public static string? Restore(Item item)
    {
        var records = Load();
        var rec = records.LastOrDefault(r => r.Name == item.Name || r.Key == item.Source);
        if (rec is null) return "找不到可还原的记录";

        var err = ApplyRestore(rec);
        if (err is not null) return err;

        records.Remove(rec);
        Save(records);
        return null;
    }

    /// <summary>倒序还原全部。</summary>
    public static string RestoreAll()
    {
        var records = Load();
        if (records.Count == 0) return "没有需要还原的改动。";

        var ok = 0;
        var failed = new List<string>();

        for (var i = records.Count - 1; i >= 0; i--)
        {
            var err = ApplyRestore(records[i]);
            if (err is null) { ok++; records.RemoveAt(i); }
            else failed.Add($"{records[i].Name}: {err}");
        }

        Save(records);
        return failed.Count == 0
            ? $"已还原 {ok} 项。"
            : $"已还原 {ok} 项，{failed.Count} 项失败：{string.Join("；", failed.Take(3))}";
    }

    private static string? ApplyRestore(Record rec)
    {
        try
        {
            switch (rec.Kind)
            {
                case "svc":
                {
                    if (!Util.IsElevated()) return "需要管理员权限";
                    if (!int.TryParse(rec.Old, out var start)) return "原值损坏";
                    var mode = start switch { 2 => "auto", 3 => "demand", 4 => "disabled", _ => "auto" };
                    var rc = RunSc("config", rec.Key, "start=", mode);
                    return rc == 0 ? null : $"sc 返回 {rc}";
                }
                case "run":
                {
                    var parts = rec.Key.Split('|');
                    if (parts.Length < 2) return "原值损坏";
                    var hive = parts[0].Equals("HKLM", StringComparison.OrdinalIgnoreCase)
                        ? RegistryHive.LocalMachine
                        : RegistryHive.CurrentUser;
                    var valueName = parts[1];

                    using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64)
                        .OpenSubKey(ApprovedPath, writable: true);
                    if (key is null) return "打不开 StartupApproved 键";

                    if (rec.Old.Length == 0) key.DeleteValue(valueName, throwOnMissingValue: false);
                    else key.SetValue(valueName, Convert.FromBase64String(rec.Old), RegistryValueKind.Binary);
                    return null;
                }
                case "task":
                {
                    using var ts = new TaskService();
                    var task = ts.GetTask(rec.Key);
                    if (task is null) return "找不到该任务";
                    task.Enabled = !bool.TryParse(rec.Old, out var v) || v;
                    return null;
                }
                case "folder":
                {
                    if (!File.Exists(rec.Old)) return "隔离文件不存在";
                    var dir = Path.GetDirectoryName(rec.Key);
                    if (dir is not null) Directory.CreateDirectory(dir);
                    if (File.Exists(rec.Key)) File.Delete(rec.Key);
                    File.Move(rec.Old, rec.Key);
                    return null;
                }
                default:
                    return "未知类型";
            }
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // ---------------------------------------------------------------- 工具

    private static void Add(Record rec)
    {
        var list = Load();
        list.Add(rec);
        Save(list);
    }

    /// <summary>"HKCU\SOFTWARE\...\Run|Steam" → (hive, runPath, "Steam")</summary>
    private static (RegistryHive Hive, string RunPath, string ValueName) ParseRunSource(string source)
    {
        var parts = source.Split('|');
        var left = parts[0];
        var valueName = parts.Length > 1 ? parts[1] : "";

        var sep = left.IndexOf('\\');
        var hiveText = sep > 0 ? left[..sep] : "HKCU";
        var runPath = sep > 0 ? left[(sep + 1)..] : left;

        var hive = hiveText.Equals("HKLM", StringComparison.OrdinalIgnoreCase)
            ? RegistryHive.LocalMachine
            : RegistryHive.CurrentUser;

        return (hive, runPath, valueName);
    }

    private static int RunSc(params string[] args)
    {
        var psi = new ProcessStartInfo("sc.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi);
        if (p is null) return -1;
        p.WaitForExit(5000);
        return p.HasExited ? p.ExitCode : -1;
    }
}

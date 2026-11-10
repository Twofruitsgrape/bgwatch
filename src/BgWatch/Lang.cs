namespace BgWatch;

/// <summary>关掉一项会怎样。抽成枚举，让两份语言表都能覆盖到，不会漏。</summary>
public enum Imp
{
    ServiceCritical, ServiceDriver, ServiceSecurity, ServicePackaged, ServiceMicrosoft, ServiceManual,
    RunResidue, RunSecurity, RunMicrosoft, RunGeneric,
    TaskTelemetry, TaskMicrosoft, TaskGeneric,
    FolderMove,
}

/// <summary>为什么被归类成"系统项"。</summary>
public enum SReason { CoreService, Driver, Security, WindowsComponent, WindowsTask }

/// <summary>
/// 全部界面文案。两份实例（中文 / 英文），运行时可切。
/// 扫描结果里的句子也在这里 —— 因为 Scanner 生成的文本会直接显示在表格里。
/// </summary>
public sealed class Lang
{
    public string Code = "zh";

    // ---- 窗口 ----
    public string WindowTitle = "";
    public string Heading = "";
    public string SearchLabel = "";
    public string SearchHint = "";
    public string UndoAll = "";
    public string Rescan = "";
    public string SafeMode = "";
    public string NotAdmin = "";
    public string Elevate = "";
    public string SwitchTo = "";
    public string Scanning = "";
    public string ProgSampling = "", ProgServices = "", ProgRun = "", ProgTasks = "", ProgFolder = "", ProgMerging = "";

    // ---- 表头 ----
    public string ColOff = "", ColUsage = "", ColName = "", ColStatus = "", ColWhat = "", ColImpact = "";

    // ---- 状态 ----
    public string StateOn = "", StateOff = "", StateOffRunning = "";
    public string SharedHost = "", RunningShort = "", NoneShort = "";

    // ---- 参数化 ----
    public Func<Imp, string> ImpactOf = _ => "";
    public Func<SReason, string> ReasonOf = _ => "";
    public Func<Kind, string> KindOf = _ => "";

    /// <summary>服务：发布者 + 宿主 exe + 官方描述</summary>
    public Func<string, string?, string, string> ServiceWhat = (_, _, _) => "";
    public Func<string?, string> RunWhat = _ => "";
    public Func<bool, string?, string> TaskWhat = (_, _) => "";
    public Func<string?, string> FolderWhat = _ => "";
    public Func<string, string> SharedWith = _ => "";

    public Func<int, int, double, string> StatusSafe = (_, _, _) => "";
    public Func<int, int, double, string> StatusFull = (_, _, _) => "";
    public Func<int, string> SearchHits = _ => "";
    public Func<int, string> CanUndo = _ => "";
    public Func<int, long, string> DumpTotal = (_, _) => "";
    public Func<int, long, string> DumpApps = (_, _) => "";
    public Func<int, long, string> DumpSystem = (_, _) => "";

    // ---- 对话框 ----
    public string NeedAdminMsg = "";
    public string ConfirmTitle = "";
    public Func<string, string, string> ConfirmText = (_, _) => "";
    public Func<string, string> OpFailed = _ => "";
    public Func<string, string> ScanFailed = _ => "";
    public Func<string, string> ElevateFailed = _ => "";
    public Func<string, string> RestoredOne = _ => "";
    public Func<string, string> DisabledOne = _ => "";
    public Func<string, string> DisabledRunning = _ => "";
    public string NothingToUndo = "";
    public Func<int, string> UndoConfirm = _ => "";

    // ---- 杂项 ----
    public string UnknownPublisher = "";
    public string PackagedApp = "";
    public string NoSignature = "";
    public string FileMissing = "";

    public static readonly Lang Zh = new()
    {
        Code = "zh",
        WindowTitle = "bgwatch — 谁在后台自己跑起来",
        Heading = "谁在后台自己跑起来",
        SearchLabel = "搜索",
        SearchHint = "名称 / 发布者 / 说明",
        UndoAll = "还原全部改动",
        Rescan = "重新扫描",
        SafeMode = "只看应用（隐藏 Windows / 驱动 / 安全项）",
        NotAdmin = "当前不是管理员：能看，但不能改。",
        Elevate = "以管理员身份重启",
        SwitchTo = "English",
        Scanning = "扫描中…",
        ProgSampling = "采样进程占用…",
        ProgServices = "读取服务…",
        ProgRun = "读取登录启动项…",
        ProgTasks = "读取计划任务…",
        ProgFolder = "读取启动文件夹…",
        ProgMerging = "整理…",

        ColOff = "关掉", ColUsage = "占用", ColName = "名称",
        ColStatus = "状态", ColWhat = "是什么", ColImpact = "关掉会怎样",

        StateOn = "启用",
        StateOff = "已关闭",
        StateOffRunning = "已关闭 · 仍在运行",
        SharedHost = "共享宿主",
        RunningShort = "运行中",
        NoneShort = "—",

        KindOf = k => k switch
        {
            Kind.Service => "服务",
            Kind.Run => "登录启动",
            Kind.Task => "计划任务",
            _ => "启动文件夹",
        },

        ReasonOf = r => r switch
        {
            SReason.CoreService => "Windows 核心服务",
            SReason.Driver => "硬件驱动配套",
            SReason.Security => "安全防护",
            SReason.WindowsComponent => "Windows 组件",
            _ => "Windows 内置任务",
        },

        ImpactOf = i => i switch
        {
            Imp.ServiceCritical => "系统核心服务，关掉可能导致无法开机 / 无网络 / 无声音",
            Imp.ServiceDriver => "硬件驱动配套服务，关掉会导致对应硬件功能或热键失效",
            Imp.ServiceSecurity => "安全软件组件：关掉会降低防护能力（杀毒/主动防御），建议保留",
            Imp.ServicePackaged => "改为手动：开机不启动。⚠ 该服务来自 MSIX 应用，应用更新后会重置",
            Imp.ServiceMicrosoft => "微软组件，关掉可能影响某个 Windows 功能",
            Imp.ServiceManual => "改为手动：开机不启动，需要时程序仍可自己拉起它",
            Imp.RunResidue => "目标文件已不存在 —— 卸载残留，可以放心关掉",
            Imp.RunSecurity => "安全软件组件：关掉会降低防护能力，建议保留",
            Imp.RunMicrosoft => "微软组件，关掉可能少个系统功能",
            Imp.RunGeneric => "不再随登录自动启动，随时可以手动打开",
            Imp.TaskTelemetry => "遥测 / 客户体验改善任务，关掉不影响系统功能",
            Imp.TaskMicrosoft => "微软维护任务，关掉前请确认具体用途",
            Imp.TaskGeneric => "不再按计划自动触发",
            _ => "移到隔离目录，不删除；可以一键还原",
        },

        ServiceWhat = (who, exe, desc) =>
        {
            var host = exe is null ? "" : $"（{Path.GetFileName(exe)}）";
            var tail = desc.Length > 0 && desc.Length <= 80 ? $"：{desc}" : "";
            return $"{who} 的服务{host}{tail}";
        },
        RunWhat = pub => pub is { Length: > 0 } ? $"{pub} 的登录启动项" : "登录启动项（无签名信息）",
        TaskWhat = (ms, exe) =>
        {
            var t = ms ? "微软内置计划任务" : "第三方计划任务";
            return exe is null ? t : $"{t}（{Path.GetFileName(exe)}）";
        },
        FolderWhat = exe => exe is null
            ? "启动文件夹里的快捷方式（目标无法解析，可能是 UWP 应用）"
            : $"启动文件夹快捷方式 → {Path.GetFileName(exe)}",
        SharedWith = name => $"同「{name}」",

        StatusSafe = (shown, hidden, gb) => $"安全模式：显示 {shown} 项应用（已隐藏 {hidden} 项系统 / 驱动 / 安全项） · 应用占用合计 {gb:0.0} GB",
        StatusFull = (shown, total, gb) => $"完整模式：显示 {shown} / {total} 项 · 占用合计 {gb:0.0} GB",
        SearchHits = n => $"　·　搜索命中 {n} 项",
        CanUndo = n => $"　·　可还原 {n} 项",
        DumpTotal = (n, mb) => $"共 {n} 项   合计 {mb} MB",
        DumpApps = (n, mb) => $"  应用类（安全模式可见）: {n} 项，合计 {mb} MB",
        DumpSystem = (n, mb) => $"  系统/驱动/安全（默认隐藏）: {n} 项，合计 {mb} MB",

        NeedAdminMsg = "需要管理员权限：请点上面的「以管理员身份重启」。",
        ConfirmTitle = "确认",
        ConfirmText = (name, impact) => $"关掉「{name}」？\n\n{impact}\n\n（随时可以点「还原全部改动」恢复）",
        OpFailed = e => "操作失败：" + e,
        ScanFailed = e => "扫描失败：" + e,
        ElevateFailed = e => "提权失败：" + e,
        RestoredOne = n => $"已恢复「{n}」",
        DisabledOne = n => $"已关掉「{n}」",
        DisabledRunning = n => $"已关掉「{n}」—— 进程还在跑，重启后才会消失",
        NothingToUndo = "没有需要还原的改动。",
        UndoConfirm = n => $"把 {n} 项改动全部还原为原来的设置？",

        UnknownPublisher = "未知发布者",
        PackagedApp = "MSIX 打包应用",
        NoSignature = "无签名信息",
        FileMissing = "文件不存在",
    };

    public static readonly Lang En = new()
    {
        Code = "en",
        WindowTitle = "bgwatch — what runs itself in the background",
        Heading = "What starts itself",
        SearchLabel = "Search",
        SearchHint = "name / publisher / description",
        UndoAll = "Undo all changes",
        Rescan = "Rescan",
        SafeMode = "Apps only (hide Windows / driver / security)",
        NotAdmin = "Not running as administrator — view only.",
        Elevate = "Restart as administrator",
        SwitchTo = "中文",
        Scanning = "Scanning…",
        ProgSampling = "Sampling processes…",
        ProgServices = "Reading services…",
        ProgRun = "Reading logon entries…",
        ProgTasks = "Reading scheduled tasks…",
        ProgFolder = "Reading startup folder…",
        ProgMerging = "Merging…",

        ColOff = "Off", ColUsage = "Usage", ColName = "Name",
        ColStatus = "Status", ColWhat = "What it is", ColImpact = "If turned off",

        StateOn = "On",
        StateOff = "Off",
        StateOffRunning = "Off · still running",
        SharedHost = "shared host",
        RunningShort = "running",
        NoneShort = "—",

        KindOf = k => k switch
        {
            Kind.Service => "Service",
            Kind.Run => "Logon entry",
            Kind.Task => "Scheduled task",
            _ => "Startup folder",
        },

        ReasonOf = r => r switch
        {
            SReason.CoreService => "Windows core service",
            SReason.Driver => "Hardware driver",
            SReason.Security => "Security",
            SReason.WindowsComponent => "Windows component",
            _ => "Windows built-in task",
        },

        ImpactOf = i => i switch
        {
            Imp.ServiceCritical => "Core system service — disabling it can break boot, network or audio",
            Imp.ServiceDriver => "Hardware driver service — the device or hotkey will stop working",
            Imp.ServiceSecurity => "Security component — turning it off weakens protection",
            Imp.ServicePackaged => "Set to manual. ⚠ Ships with an MSIX app; resets when the app updates",
            Imp.ServiceMicrosoft => "Windows component — may disable a Windows feature",
            Imp.ServiceManual => "Set to manual: won't start at boot, programs can still start it on demand",
            Imp.RunResidue => "Target file no longer exists — leftover, safe to turn off",
            Imp.RunSecurity => "Security component — turning it off weakens protection",
            Imp.RunMicrosoft => "Windows component — may lose a minor Windows feature",
            Imp.RunGeneric => "Won't start at login; you can still open it manually",
            Imp.TaskTelemetry => "Telemetry / customer experience task — no effect on Windows",
            Imp.TaskMicrosoft => "Microsoft maintenance task — check what it does first",
            Imp.TaskGeneric => "Won't run on schedule any more",
            _ => "Moved to quarantine (not deleted); one click to put it back",
        },

        ServiceWhat = (who, exe, desc) =>
        {
            var host = exe is null ? "" : $" ({Path.GetFileName(exe)})";
            var tail = desc.Length > 0 && desc.Length <= 80 ? $": {desc}" : "";
            return $"{who} service{host}{tail}";
        },
        RunWhat = pub => pub is { Length: > 0 } ? $"Logon entry by {pub}" : "Logon entry (no signature)",
        TaskWhat = (ms, exe) =>
        {
            var t = ms ? "Microsoft built-in task" : "Third-party task";
            return exe is null ? t : $"{t} ({Path.GetFileName(exe)})";
        },
        FolderWhat = exe => exe is null
            ? "Shortcut in the startup folder (target unresolved, possibly a UWP app)"
            : $"Startup folder shortcut → {Path.GetFileName(exe)}",
        SharedWith = name => $"same as {name}",

        StatusSafe = (shown, hidden, gb) => $"Safe mode: {shown} app items shown ({hidden} Windows / driver / security items hidden) · {gb:0.0} GB in use",
        StatusFull = (shown, total, gb) => $"Full mode: {shown} of {total} items · {gb:0.0} GB in use",
        SearchHits = n => $"　·　{n} matches",
        CanUndo = n => $"　·　{n} change(s) to undo",
        DumpTotal = (n, mb) => $"{n} items, {mb} MB total",
        DumpApps = (n, mb) => $"  App items (visible in safe mode): {n}, {mb} MB",
        DumpSystem = (n, mb) => $"  System/driver/security (hidden): {n}, {mb} MB",

        NeedAdminMsg = "Administrator required — use the button at the top.",
        ConfirmTitle = "Confirm",
        ConfirmText = (name, impact) => $"Turn off \"{name}\"?\n\n{impact}\n\n(You can always use \"Undo all changes\")",
        OpFailed = e => "Failed: " + e,
        ScanFailed = e => "Scan failed: " + e,
        ElevateFailed = e => "Elevation failed: " + e,
        RestoredOne = n => $"Restored \"{n}\"",
        DisabledOne = n => $"Turned off \"{n}\"",
        DisabledRunning = n => $"Turned off \"{n}\" — the process is still running; it goes away after a reboot",
        NothingToUndo = "Nothing to undo.",
        UndoConfirm = n => $"Restore all {n} change(s) to their original settings?",

        UnknownPublisher = "Unknown publisher",
        PackagedApp = "MSIX packaged app",
        NoSignature = "no signature",
        FileMissing = "file missing",
    };

    public static string SwitchText => L.T.SwitchTo;
}

/// <summary>当前语言 + 持久化。</summary>
public static class L
{
    private static readonly string Path =
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "bgwatch", "lang.txt");

    public static Lang T { get; private set; } = Load();

    public static void Set(string code)
    {
        T = code == "en" ? Lang.En : Lang.Zh;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, T.Code);
        }
        catch { }
    }

    public static void Toggle() => Set(T.Code == "en" ? "zh" : "en");

    private static Lang Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var code = File.ReadAllText(Path).Trim();
                if (code == "en") return Lang.En;
                if (code == "zh") return Lang.Zh;
            }
        }
        catch { }

        var ui = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return ui == "zh" ? Lang.Zh : Lang.En;
    }
}

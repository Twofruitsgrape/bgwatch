namespace BgWatch;

public enum Kind { Service, Run, Task, Folder }

public enum Safety
{
    /// <summary>关掉只会让某个软件不自动启动，随时能手动打开。</summary>
    Safe = 0,
    /// <summary>微软组件 / 硬件配套 / 需要人工判断。</summary>
    Caution = 1,
    /// <summary>动了会出系统级问题，不允许操作。</summary>
    Critical = 2,
}

public sealed class Item
{
    public string Key { get; init; } = "";
    public Kind Kind { get; init; }
    public string Name { get; set; } = "";
    public string Source { get; init; } = "";
    public string? Exe { get; set; }
    public string Publisher { get; set; } = "";

    /// <summary>这是什么（一句话）。</summary>
    public string What { get; set; } = "";

    /// <summary>关掉会怎样（一句话）。</summary>
    public string Impact { get; set; } = "";

    public Safety Safety { get; set; } = Safety.Safe;
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 属于 Windows 本身 / 硬件驱动 / 安全防护的项。
    /// 安全模式默认隐藏它们 —— 这些关掉不会让电脑变快，只会让它出问题。
    /// </summary>
    public bool SystemManaged { get; set; }

    /// <summary>系统项的归类，用来在界面上一句话说明为什么隐藏。</summary>
    public string SystemReason { get; set; } = "";

    /// <summary>MSIX 打包服务：应用更新后会重新注册，改动会被重置。</summary>
    public bool Packaged { get; set; }

    /// <summary>svchost 之类的共享宿主：内存不可单独归因，不虚报数字。</summary>
    public bool SharedHost { get; set; }

    /// <summary>同一个进程被多条自启动项引用时，只让第一条报数字，其余指向它 —— 不重复计数。</summary>
    public string SharedWith { get; set; } = "";

    public long MemMb { get; set; }
    public double CpuPct { get; set; }
    public int Pids { get; set; }

    public string KindLabel => L.T.KindOf(Kind);

    public string UsageLabel => SharedWith.Length > 0
        ? L.T.SharedWith(SharedWith)
        : MemMb > 0
        ? (CpuPct >= 0.1 ? $"{MemMb} MB · {CpuPct:0.0}%" : $"{MemMb} MB")
        : SharedHost ? L.T.SharedHost
        : Pids > 0 ? L.T.RunningShort
        : L.T.NoneShort;

    /// <summary>界面上禁止操作：系统关键项一律不给开关。</summary>
    public bool Actionable => Safety != Safety.Critical;
}

public static class ServiceRules
{
    /// <summary>禁用会导致无法开机 / 无法联网 / 没有声音的服务。宁多勿少。</summary>
    public static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM", "Power", "PlugPlay", "BrokerInfrastructure",
        "gpsvc", "Winmgmt", "EventLog", "Schedule", "ProfSvc", "UserManager", "SamSs", "KeyIso",
        "CryptSvc", "WinDefend", "SecurityHealthService", "wscsvc", "MsMpSvc", "Sense",
        "MpsSvc", "BFE", "Dhcp", "Dnscache", "nsi", "NlaSvc", "netprofm",
        "AudioSrv", "AudioEndpointBuilder", "Themes", "TokenBroker", "StateRepository", "camsvc",
        "LanmanWorkstation", "LanmanServer", "WlanSvc", "Wcmsvc", "NcbService",
        "CoreMessagingRegistrar", "SystemEventsBroker", "TextInputManagementService",
    };

    /// <summary>安全软件：关掉会降低防护，必须标成"需确认"，不能给成"可安全禁用"。</summary>
    public static readonly string[] SecurityHints =
    {
        "360", "qihoo", "奇虎", "huorong", "火绒", "kingsoft", "金山", "tencent", "腾讯",
        "kaspersky", "eset", "avast", "avg", "bitdefender", "mcafee", "norton", "symantec",
        "trend micro", "sophos", "malwarebytes", "avira", "defender", "zhudongfangyu",
    };

    /// <summary>程序是不是微软自己的东西（发布者是微软，或落在 Windows 目录里）。</summary>
    public static bool IsWindowsOwned(string publisher, string? exe)
    {
        if (publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) return true;
        var e = exe ?? "";
        return e.StartsWith(Util.Windows + "\\", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksLikeSecurity(string publisher, string? exe)
    {
        var hay = (publisher + " " + (exe ?? "")).ToLowerInvariant();
        return SecurityHints.Any(h => hay.Contains(h, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>更新/安装类辅助程序：关掉不影响主程序。</summary>
    public static readonly string[] HelperHints =
    {
        "update", "updater", "updateservice", "crashhandler", "crashpad",
        "telemetry", "diagtrack", "uninstall", "setup", "installer",
    };
}

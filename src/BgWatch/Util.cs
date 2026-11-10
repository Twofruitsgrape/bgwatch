using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BgWatch;

/// <summary>路径 / 命令行 / 版本信息 / 快捷方式。四个扫描器都靠它。</summary>
public static class Util
{
    private static readonly string[] ExeExts = { ".exe", ".com", ".bat", ".cmd", ".scr", ".ps1", ".vbs", ".js" };

    public static string Windows => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public static string Expand(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.Trim()
            .Replace("\\SystemRoot\\", Windows + "\\", StringComparison.OrdinalIgnoreCase)
            .Replace("\\??\\", "", StringComparison.Ordinal)
            .Replace("\\\\?\\", "", StringComparison.Ordinal)
            .Replace('/', '\\');
        try { s = Environment.ExpandEnvironmentVariables(s); } catch { }
        return s;
    }

    /// <summary>从命令行里抠出可执行文件。难点是无引号的 "C:\Program Files\x.exe"。</summary>
    public static string? ExeFromCommandLine(string? cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return null;
        var s = Expand(cmd).Trim();

        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            return end > 1 ? Clean(s[1..end]) : null;
        }

        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string? longest = null, existing = null;
        var sb = new StringBuilder();
        for (var i = 0; i < parts.Length && i <= 10; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(parts[i]);
            var cand = sb.ToString();
            if (!ExeExts.Any(e => cand.EndsWith(e, StringComparison.OrdinalIgnoreCase))) continue;
            longest = cand;
            if (File.Exists(cand)) existing = cand;
        }

        if (existing is not null) return Clean(existing);
        if (longest is not null) return Clean(longest);
        return parts.Length > 0 ? Clean(parts[0].TrimEnd(',')) : null;
    }

    private static string Clean(string p) => p.Trim().Trim('"').TrimEnd(' ', ',');

    /// <summary>用于比较的归一化路径。</summary>
    public static string Norm(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var p = Expand(path).Trim('"');
        try { p = Path.GetFullPath(p); } catch { }
        return p.TrimEnd(' ', '.').ToLowerInvariant();
    }

    private static readonly Dictionary<string, string> VendorCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>把 exe 翻译成"这是谁家的软件"。没有版本信息就返回空串。</summary>
    public static string Vendor(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "";
        lock (VendorCache)
        {
            if (VendorCache.TryGetValue(path, out var cached)) return cached;
        }

        var result = "";
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(path);
            result = (vi.CompanyName ?? "").Trim();
            if (result.Length == 0) result = (vi.ProductName ?? "").Trim();
        }
        catch { }

        lock (VendorCache) VendorCache[path] = result;
        return result;
    }

    public static bool IsElevated()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    // ---------------- 快捷方式 ----------------

    /// <summary>解析 .lnk 的目标。启动文件夹里全是快捷方式，不解析就拿不到真实 exe。</summary>
    public static string? ResolveShortcut(string lnk)
    {
        if (!File.Exists(lnk)) return null;
        object? o = null;
        try
        {
            o = new ShellLinkCo();
            var link = (IShellLinkW)o;
            var persist = (IPersistFile)o;

            persist.Load(lnk, 0);              // STGM_READ

            var sb = new StringBuilder(1024);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            var path = sb.ToString();

            return string.IsNullOrWhiteSpace(path) ? null : Expand(path);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (o is not null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046"), ClassInterface(ClassInterfaceType.None)]
    private class ShellLinkCo { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}

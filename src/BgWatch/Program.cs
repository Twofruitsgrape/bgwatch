namespace BgWatch;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 无界面自检：bgwatch.exe --dump out.txt
        // 用来在不打开窗口的情况下核对扫描结果（也方便排查"界面上一片空白"这类问题）。
        if (args.Length > 0 && args[0] == "--dump")
        {
            var path = args.Length > 1 ? args[1] : "bgwatch-dump.txt";
            var items = Scanner.Scan();
            var sys = items.Where(i => i.SystemManaged).ToList();
            var app = items.Where(i => !i.SystemManaged).ToList();
            var lines = new List<string>
            {
                $"共 {items.Count} 项   合计 {items.Sum(i => i.MemMb)} MB",
                $"  安全模式可见（应用类）: {app.Count} 项，合计 {app.Sum(i => i.MemMb)} MB",
                $"  系统/驱动/安全（默认隐藏）: {sys.Count} 项，合计 {sys.Sum(i => i.MemMb)} MB",
                "",
            };
            foreach (var i in items)
            {
                var tag = i.SystemManaged ? $"系统/{i.SystemReason}" : "应用";
                lines.Add($"{i.MemMb,6} MB  [{i.KindLabel}] {i.Name}   (启用={i.Enabled}, 安全={i.Safety}, 归类={tag})");
                lines.Add($"              是什么: {i.What}");
                lines.Add($"              关掉:   {i.Impact}");
            }
            File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(true));
            return;
        }

        // 脚本模式：bgwatch.exe --off "名称"  /  --on "名称"
        if (args.Length >= 2 && (args[0] == "--off" || args[0] == "--on"))
        {
            var target = args[1];
            var items = Scanner.Scan();
            var item = items.FirstOrDefault(i => i.Name.Equals(target, StringComparison.OrdinalIgnoreCase))
                       ?? items.FirstOrDefault(i => i.Name.Contains(target, StringComparison.OrdinalIgnoreCase));

            if (item is null) { File.WriteAllText("bgwatch-result.txt", $"找不到: {target}"); return; }

            var error = args[0] == "--off" ? Changer.Disable(item) : Changer.Restore(item);
            var text = error is null
                ? $"{args[0]} 成功: [{item.KindLabel}] {item.Name}  ::  {item.Impact}"
                : $"{args[0]} 失败: {error}";
            File.WriteAllText("bgwatch-result.txt", text);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

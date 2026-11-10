using System.Diagnostics;

namespace BgWatch;

public sealed class MainForm : Form
{
    private readonly DataGridView _grid = new();
    private readonly TextBox _search = new();
    private readonly Label _status = new();
    private readonly Button _restore = new();
    private readonly Button _refresh = new();
    private readonly CheckBox _safeMode = new();
    private readonly Panel _banner = new();

    private List<Item> _items = new();
    private bool _suppress;

    public MainForm()
    {
        Text = "bgwatch — 谁在后台自己跑起来";
        Width = 1240;
        Height = 780;
        MinimumSize = new Size(900, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.White;

        BuildBanner();
        BuildTopBar();
        BuildGrid();
        BuildStatusBar();

        Load += async (_, _) => await RescanAsync();
    }

    // ---------------------------------------------------------------- 界面

    private void BuildBanner()
    {
        if (Util.IsElevated()) return;

        _banner.Dock = DockStyle.Top;
        _banner.Height = 38;
        _banner.BackColor = Color.FromArgb(255, 248, 226);
        _banner.Padding = new Padding(12, 8, 12, 8);

        var text = new Label
        {
            Text = "当前不是管理员：能看，但不能改。",
            AutoSize = true,
            ForeColor = Color.FromArgb(120, 90, 0),
            Location = new Point(12, 10),
        };

        var elevate = new Button
        {
            Text = "以管理员身份重启",
            AutoSize = true,
            Location = new Point(250, 5),
            FlatStyle = FlatStyle.System,
        };
        elevate.Click += (_, _) => RelaunchElevated();

        _banner.Controls.Add(text);
        _banner.Controls.Add(elevate);
        Controls.Add(_banner);
    }

    private void BuildTopBar()
    {
        var top = new Panel { Dock = DockStyle.Top, Height = 58, Padding = new Padding(12, 14, 12, 8) };

        var title = new Label
        {
            Text = "谁在后台自己跑起来",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
            Location = new Point(12, 16),
        };

        var searchLabel = new Label { Text = "搜索", AutoSize = true, Location = new Point(260, 20) };

        _search.Location = new Point(296, 17);
        _search.Width = 260;
        _search.PlaceholderText = "名称 / 发布者 / 说明";
        _search.TextChanged += (_, _) => Rebuild();

        _restore.Text = "还原全部改动";
        _restore.AutoSize = true;
        _restore.Location = new Point(574, 15);
        _restore.Click += (_, _) => RestoreAll();

        _refresh.Text = "重新扫描";
        _refresh.AutoSize = true;
        _refresh.Location = new Point(690, 15);
        _refresh.Click += async (_, _) => await RescanAsync();

        _safeMode.Text = "只看应用（隐藏 Windows / 驱动 / 安全项）";
        _safeMode.AutoSize = true;
        _safeMode.Checked = true;
        _safeMode.Location = new Point(800, 17);
        _safeMode.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
        _safeMode.CheckedChanged += (_, _) => Rebuild();

        top.Controls.AddRange(new Control[] { title, searchLabel, _search, _restore, _refresh, _safeMode });
        Controls.Add(top);
    }

    private void BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.BorderStyle = BorderStyle.None;
        _grid.BackgroundColor = Color.White;
        _grid.GridColor = Color.FromArgb(235, 235, 235);
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.RowTemplate.Height = 32;
        _grid.ColumnHeadersHeight = 34;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 248, 248);
        // 表头也要设 Selection 色，否则当前单元格所在列的表格头会变成系统高亮蓝
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(248, 248, 248);
        _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(60, 60, 60);
        // 选中行：浅蓝底 + 深色字。不设 SelectionForeColor 的话默认是白色，压在浅底上根本看不清。
        _grid.DefaultCellStyle.ForeColor = Color.FromArgb(30, 30, 30);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(226, 236, 250);
        _grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(30, 30, 30);

        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "关掉",
            Width = 46,
            ReadOnly = false,
        });
        _grid.Columns.Add(Col("占用", 92));
        _grid.Columns.Add(Col("名称", 210));
        _grid.Columns.Add(Col("状态", 116));
        _grid.Columns.Add(Col("是什么", 330));
        _grid.Columns.Add(Col("关掉会怎样", 300));

        _grid.Columns[4].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += OnCellValueChanged;
        // 只让复选框列可编辑
        _grid.CellBeginEdit += (_, e) => { if (e.ColumnIndex != 0) e.Cancel = true; };

        Controls.Add(_grid);
        _grid.BringToFront();
    }

    private static DataGridViewTextBoxColumn Col(string header, int width) => new()
    {
        HeaderText = header,
        Width = width,
        ReadOnly = true,
        SortMode = DataGridViewColumnSortMode.NotSortable,
    };

    private void BuildStatusBar()
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Color.FromArgb(248, 248, 248) };
        _status.AutoSize = false;
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Padding = new Padding(12, 0, 0, 0);
        _status.ForeColor = Color.FromArgb(90, 90, 90);
        bar.Controls.Add(_status);
        Controls.Add(bar);
    }

    // ---------------------------------------------------------------- 扫描

    private async Task RescanAsync()
    {
        SetBusy(true);
        try
        {
            var progress = new Progress<string>(m => _status.Text = m);
            var items = await Task.Run(() => Scanner.Scan(m => ((IProgress<string>)progress).Report(m)));

            _items = items;
            Rebuild();

            if (Changer.Count > 0)
                _status.Text += $"　·　可还原 {Changer.Count} 项";
        }
        catch (Exception ex)
        {
            _status.Text = "扫描失败：" + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _refresh.Enabled = !busy;
        _restore.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        if (busy) _status.Text = "扫描中…";
    }

    // ---------------------------------------------------------------- 列表

    private void Rebuild()
    {
        _suppress = true;
        _grid.Rows.Clear();

        var query = _search.Text.Trim();
        var safe = _safeMode.Checked;
        var shown = 0;
        var hidden = 0;

        foreach (var item in _items)
        {
            // 安全模式：隐藏 Windows 自身 / 硬件驱动 / 安全防护 —— 这些关掉只会让电脑出问题
            if (safe && item.SystemManaged) { hidden++; continue; }

            if (query.Length > 0
                && !item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !item.What.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !item.Impact.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !item.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;

            var index = _grid.Rows.Add(
                !item.Enabled,
                item.UsageLabel,
                item.Name,
                StateOf(item),
                item.What,
                item.Impact);

            var row = _grid.Rows[index];
            row.Tag = item;
            shown++;

            if (!item.Actionable)
            {
                row.Cells[0].ReadOnly = true;
                row.DefaultCellStyle.ForeColor = Color.FromArgb(150, 150, 150);
                row.DefaultCellStyle.SelectionForeColor = Color.FromArgb(150, 150, 150);
            }

            PaintStatus(row.Cells[3], item);
        }

        _suppress = false;

        // 载入后不要自动高亮第一行，看起来像被选中了
        _grid.ClearSelection();
        _grid.CurrentCell = null;

        var totalMb = _items.Where(i => !safe || !i.SystemManaged).Sum(i => i.MemMb);
        _status.Text = safe
            ? $"安全模式：显示 {shown} 项应用（已隐藏 {hidden} 项系统 / 驱动 / 安全项） · 应用占用合计 {totalMb / 1024.0:0.0} GB"
            : $"完整模式：显示 {shown} / {_items.Count} 项 · 占用合计 {totalMb / 1024.0:0.0} GB";
        if (query.Length > 0) _status.Text += $"　·　搜索命中 {shown} 项";
    }

    /// <summary>状态列上色，普通态和选中态都设，否则一选中颜色就丢了。</summary>
    private static void PaintStatus(DataGridViewCell cell, Item item)
    {
        var color = item.Enabled
            ? Color.FromArgb(40, 140, 60)
            : item.Pids > 0 ? Color.FromArgb(200, 120, 0) : Color.FromArgb(120, 120, 120);

        cell.Style.ForeColor = color;
        cell.Style.SelectionForeColor = color;
    }

    private static string StateOf(Item item)
    {
        if (item.Enabled) return "启用";
        return item.Pids > 0 ? "已关闭 · 仍在运行" : "已关闭";
    }

    private async void OnCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_suppress || e.ColumnIndex != 0 || e.RowIndex < 0) return;
        if (_grid.Rows[e.RowIndex].Tag is not Item item) return;

        var wantDisabled = (bool)(_grid.Rows[e.RowIndex].Cells[0].Value ?? false);
        if (wantDisabled == !item.Enabled) return;      // 没有实际变化

        if (!Util.IsElevated() && item.Kind != Kind.Folder)
        {
            _suppress = true;
            _grid.Rows[e.RowIndex].Cells[0].Value = !item.Enabled;
            _suppress = false;
            _status.Text = "需要管理员权限：请点上面的「以管理员身份重启」。";
            return;
        }

        if (wantDisabled && item.Safety == Safety.Caution)
        {
            var answer = MessageBox.Show(
                $"关掉「{item.Name}」？\n\n{item.Impact}\n\n（随时可以点「还原全部改动」恢复）",
                "确认", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK)
            {
                _suppress = true;
                _grid.Rows[e.RowIndex].Cells[0].Value = !item.Enabled;
                _suppress = false;
                return;
            }
        }

        var error = wantDisabled ? Changer.Disable(item) : Changer.Restore(item);
        if (error is not null)
        {
            _status.Text = "操作失败：" + error;
            _suppress = true;
            _grid.Rows[e.RowIndex].Cells[0].Value = !item.Enabled;
            _suppress = false;
            return;
        }

        item.Enabled = !wantDisabled;

        _suppress = true;
        var row = _grid.Rows[e.RowIndex];
        row.Cells[1].Value = item.UsageLabel;
        row.Cells[3].Value = StateOf(item);
        PaintStatus(row.Cells[3], item);
        _suppress = false;

        _status.Text = item.Enabled
            ? $"已恢复「{item.Name}」"
            : item.Pids > 0
                ? $"已关掉「{item.Name}」—— 进程还在跑，重启后才会消失"
                : $"已关掉「{item.Name}」";

        await Task.CompletedTask;
    }

    private async void RestoreAll()
    {
        var count = Changer.Count;
        if (count == 0)
        {
            MessageBox.Show("没有需要还原的改动。", "bgwatch", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show($"把 {count} 项改动全部还原为原来的设置？", "确认还原",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

        var result = Changer.RestoreAll();
        await RescanAsync();
        _status.Text = result;
        MessageBox.Show(result, "bgwatch", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ---------------------------------------------------------------- 提权

    private void RelaunchElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Application.ExecutablePath)
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("提权失败：" + ex.Message, "bgwatch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

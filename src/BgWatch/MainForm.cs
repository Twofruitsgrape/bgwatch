using System.Diagnostics;

namespace BgWatch;

public sealed class MainForm : Form
{
    private readonly DataGridView _grid = new();
    private readonly TextBox _search = new();
    private readonly Label _status = new();
    private readonly Button _restore = new();
    private readonly Button _refresh = new();
    private readonly Button _lang = new();
    private readonly Label _heading = new();
    private readonly Label _searchLabel = new();
    private readonly CheckBox _safeMode = new();
    private readonly Panel _banner = new();
    private Label? _bannerText;
    private Button? _elevateButton;

    private List<Item> _items = new();
    private bool _suppress;

    public MainForm()
    {
        Width = 1260;
        Height = 780;
        MinimumSize = new Size(900, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.White;

        BuildBanner();
        BuildTopBar();
        BuildGrid();
        BuildStatusBar();

        ApplyLanguage();
        Load += async (_, _) => await RescanAsync();
    }

    // ---------------------------------------------------------------- 界面搭建

    private void BuildBanner()
    {
        if (Util.IsElevated()) return;

        _banner.Dock = DockStyle.Top;
        _banner.Height = 38;
        _banner.BackColor = Color.FromArgb(255, 248, 226);

        _bannerText = new Label { AutoSize = true, Location = new Point(12, 11), ForeColor = Color.FromArgb(120, 90, 0) };

        _elevateButton = new Button { AutoSize = true, Location = new Point(12, 5), FlatStyle = FlatStyle.System };
        _elevateButton.Click += (_, _) => RelaunchElevated();

        _banner.Controls.Add(_bannerText);
        _banner.Controls.Add(_elevateButton);
        Controls.Add(_banner);
    }

    private void BuildTopBar()
    {
        // FlowLayoutPanel：中英文文案长短不同，绝对坐标会挤成一团
        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            Padding = new Padding(12, 16, 12, 8),
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.White,
        };

        _heading.AutoSize = true;
        _heading.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold);
        _heading.Margin = new Padding(0, 4, 24, 0);

        _searchLabel.AutoSize = true;
        _searchLabel.Margin = new Padding(0, 8, 6, 0);

        _search.Width = 250;
        _search.Margin = new Padding(0, 4, 14, 0);
        _search.TextChanged += (_, _) => Rebuild();

        _restore.AutoSize = true;
        _restore.Margin = new Padding(0, 3, 8, 0);
        _restore.FlatStyle = FlatStyle.System;
        _restore.Click += (_, _) => RestoreAll();

        _refresh.AutoSize = true;
        _refresh.Margin = new Padding(0, 3, 18, 0);
        _refresh.FlatStyle = FlatStyle.System;
        _refresh.Click += async (_, _) => await RescanAsync();

        _safeMode.AutoSize = true;
        _safeMode.Checked = true;
        _safeMode.Margin = new Padding(0, 7, 0, 0);
        _safeMode.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
        _safeMode.CheckedChanged += (_, _) => Rebuild();

        top.Controls.AddRange(new Control[] { _heading, _searchLabel, _search, _restore, _refresh, _safeMode });
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
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(248, 248, 248);
        _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(60, 60, 60);

        // 选中行：浅蓝底 + 深色字。不设 SelectionForeColor 默认是白色，压在浅底上看不清。
        _grid.DefaultCellStyle.ForeColor = Color.FromArgb(30, 30, 30);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(226, 236, 250);
        _grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(30, 30, 30);

        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Width = 46, ReadOnly = false });
        _grid.Columns.Add(Col(130));   // 英文 "same as X" 比中文「同「X」」长
        _grid.Columns.Add(Col(210));
        _grid.Columns.Add(Col(116));
        _grid.Columns.Add(Col(330));
        _grid.Columns.Add(Col(300));
        _grid.Columns[4].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += OnCellValueChanged;
        _grid.CellBeginEdit += (_, e) => { if (e.ColumnIndex != 0) e.Cancel = true; };

        Controls.Add(_grid);
        _grid.BringToFront();
    }

    private static DataGridViewTextBoxColumn Col(int width) => new()
    {
        Width = width,
        ReadOnly = true,
        SortMode = DataGridViewColumnSortMode.NotSortable,
    };

    private void BuildStatusBar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 32,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.FromArgb(248, 248, 248),
            Padding = new Padding(0, 0, 8, 0),
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _status.Dock = DockStyle.Fill;
        _status.AutoSize = false;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Padding = new Padding(12, 0, 0, 0);
        _status.ForeColor = Color.FromArgb(90, 90, 90);

        _lang.AutoSize = true;
        _lang.FlatStyle = FlatStyle.System;
        _lang.Margin = new Padding(0, 3, 0, 3);
        _lang.Click += async (_, _) => await SwitchLanguageAsync();

        bar.Controls.Add(_status, 0, 0);
        bar.Controls.Add(_lang, 1, 0);
        Controls.Add(bar);
    }

    /// <summary>所有静态文案。切语言时重新调一次即可。</summary>
    private void ApplyLanguage()
    {
        var t = L.T;
        Text = t.WindowTitle;
        _heading.Text = t.Heading;
        _searchLabel.Text = t.SearchLabel;
        _search.PlaceholderText = t.SearchHint;
        _restore.Text = t.UndoAll;
        _refresh.Text = t.Rescan;
        _safeMode.Text = t.SafeMode;
        _lang.Text = t.SwitchTo;

        _grid.Columns[0].HeaderText = t.ColOff;
        _grid.Columns[1].HeaderText = t.ColUsage;
        _grid.Columns[2].HeaderText = t.ColName;
        _grid.Columns[3].HeaderText = t.ColStatus;
        _grid.Columns[4].HeaderText = t.ColWhat;
        _grid.Columns[5].HeaderText = t.ColImpact;

        if (_bannerText is not null) _bannerText.Text = t.NotAdmin;
        if (_elevateButton is not null)
        {
            _elevateButton.Text = t.Elevate;
            _elevateButton.Location = new Point(_bannerText!.Right + 12, 5);
        }

        if (_items.Count > 0) Rebuild();
    }

    private async Task SwitchLanguageAsync()
    {
        L.Toggle();
        ApplyLanguage();
        await RescanAsync();       // 条目文案是扫描时生成的，必须重扫
    }

    // ---------------------------------------------------------------- 扫描

    private async Task RescanAsync()
    {
        SetBusy(true);
        try
        {
            var progress = new Progress<string>(m => _status.Text = m);
            _items = await Task.Run(() => Scanner.Scan(m => ((IProgress<string>)progress).Report(m)));
            Rebuild();
        }
        catch (Exception ex)
        {
            _status.Text = L.T.ScanFailed(ex.Message);
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
        _lang.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        if (busy) _status.Text = L.T.Scanning;
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
        var gb = 0.0;

        foreach (var item in _items)
        {
            if (safe && item.SystemManaged) { hidden++; continue; }

            if (query.Length > 0
                && !item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !item.What.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !item.Impact.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !item.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;

            var index = _grid.Rows.Add(!item.Enabled, item.UsageLabel, item.Name, StateOf(item), item.What, item.Impact);
            var row = _grid.Rows[index];
            row.Tag = item;
            shown++;
            gb += item.MemMb / 1024.0;

            if (!item.Actionable)
            {
                row.Cells[0].ReadOnly = true;
                row.DefaultCellStyle.ForeColor = Color.FromArgb(150, 150, 150);
                row.DefaultCellStyle.SelectionForeColor = Color.FromArgb(150, 150, 150);
            }

            PaintStatus(row.Cells[3], item);
        }

        _suppress = false;

        // 载入后不要自动高亮第一行
        _grid.ClearSelection();
        _grid.CurrentCell = null;

        var t = L.T;
        _status.Text = safe ? t.StatusSafe(shown, hidden, gb) : t.StatusFull(shown, _items.Count, gb);
        if (query.Length > 0) _status.Text += t.SearchHits(shown);
        if (Changer.Count > 0) _status.Text += t.CanUndo(Changer.Count);
    }

    private static string StateOf(Item item)
        => item.Enabled ? L.T.StateOn : item.Pids > 0 ? L.T.StateOffRunning : L.T.StateOff;

    /// <summary>状态列上色，普通态和选中态都设，否则一选中颜色就丢了。</summary>
    private static void PaintStatus(DataGridViewCell cell, Item item)
    {
        var color = item.Enabled
            ? Color.FromArgb(40, 140, 60)
            : item.Pids > 0 ? Color.FromArgb(200, 120, 0) : Color.FromArgb(120, 120, 120);

        cell.Style.ForeColor = color;
        cell.Style.SelectionForeColor = color;
    }

    private void OnCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_suppress || e.ColumnIndex != 0 || e.RowIndex < 0) return;
        if (_grid.Rows[e.RowIndex].Tag is not Item item) return;

        var wantDisabled = (bool)(_grid.Rows[e.RowIndex].Cells[0].Value ?? false);
        if (wantDisabled == !item.Enabled) return;

        if (!Util.IsElevated() && item.Kind != Kind.Folder)
        {
            Revert(e.RowIndex, item);
            _status.Text = L.T.NeedAdminMsg;
            return;
        }

        if (wantDisabled && item.Safety == Safety.Caution)
        {
            var answer = MessageBox.Show(
                L.T.ConfirmText(item.Name, item.Impact),
                L.T.ConfirmTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK) { Revert(e.RowIndex, item); return; }
        }

        var error = wantDisabled ? Changer.Disable(item) : Changer.Restore(item);
        if (error is not null)
        {
            _status.Text = L.T.OpFailed(error);
            Revert(e.RowIndex, item);
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
            ? L.T.RestoredOne(item.Name)
            : item.Pids > 0 ? L.T.DisabledRunning(item.Name) : L.T.DisabledOne(item.Name);
    }

    private void Revert(int rowIndex, Item item)
    {
        _suppress = true;
        _grid.Rows[rowIndex].Cells[0].Value = !item.Enabled;
        _suppress = false;
    }

    private async void RestoreAll()
    {
        var count = Changer.Count;
        if (count == 0)
        {
            MessageBox.Show(L.T.NothingToUndo, "bgwatch", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(L.T.UndoConfirm(count), L.T.ConfirmTitle,
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
            MessageBox.Show(L.T.ElevateFailed(ex.Message), "bgwatch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

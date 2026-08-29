namespace TfcViewerGui;

/// <summary>图例项（色块 + 群系名，可多选）</summary>
public sealed class LegendEntry : Control
{
    public int BiomeId;
    public string BiomeName = "";
    public Color BiomeColor;
    public bool Checked;

    public event Action<int>? Clicked;

    private readonly Font _font = new("Microsoft YaHei UI", 9f);

    public LegendEntry(int id, string name, Color color)
    {
        BiomeId = id;
        BiomeName = name;
        BiomeColor = color;
        Cursor = Cursors.Hand;
        // 固定列宽，名称过长自动换行（高度自适应）
        using (var g = CreateGraphics())
        {
            var sz = g.MeasureString(name, _font, 216);
            Height = Math.Max(26, (int)sz.Height + 10);
        }
        Width = 234;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (Checked)
        {
            using var bg = new SolidBrush(Color.FromArgb(180, 210, 255));
            g.FillRectangle(bg, 0, 0, Width - 1, Height - 1);
            using var bar = new SolidBrush(Color.FromArgb(30, 90, 200));
            g.FillRectangle(bar, 0, 0, 4, Height);
        }
        using var c = new SolidBrush(BiomeColor);
        g.FillRectangle(c, 10, 5, 16, 16);
        using var pen = new Pen(Color.FromArgb(80, 80, 80));
        g.DrawRectangle(pen, 10, 5, 16, 16);
        using var tb = new SolidBrush(ForeColor);
        // 名称多行自动换行，垂直居中
        using var fmt = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        var rect = new RectangleF(32, 2, Width - 38, Height - 4);
        g.DrawString(BiomeName, _font, tb, rect, fmt);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Clicked?.Invoke(BiomeId);
    }
}

/// <summary>左侧图例面板：标题 + 取消选择按钮 + 群系列表（多选）</summary>
public sealed class LegendPanel : UserControl
{
    public event Action? SelectionChanged;

    /// <summary>true = 英文界面（标题/按钮英文）</summary>
    public bool English;

    private readonly Dictionary<int, LegendEntry> _entries = new();
    private readonly FlowLayoutPanel _list;
    private readonly Button _clearBtn;
    private readonly Label _titleLabel;

    public HashSet<int> Selected { get; } = new();

    public LegendPanel()
    {
        BackColor = Color.FromArgb(245, 245, 245);
        MinimumSize = new Size(260, 100);

        var title = new Panel { Dock = DockStyle.Top, Height = 36 };
        _titleLabel = new Label
        {
            Text = "图例（点击多选）",
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(8, 10),
        };
        _clearBtn = new Button
        {
            Text = "✕ 取消选择",
            Font = new Font("Microsoft YaHei UI", 9f),
            AutoSize = true,
            Visible = false,
            Location = new Point(150, 6),
            BackColor = Color.FromArgb(255, 200, 200),
        };
        _clearBtn.Click += (_, _) => ClearSelection();
        title.Controls.Add(_titleLabel);
        title.Controls.Add(_clearBtn);

        _list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };

        Controls.Add(_list);
        Controls.Add(title);
    }

    /// <summary>切换界面语言，刷新标题/按钮文字</summary>
    public void ApplyLanguage()
    {
        _titleLabel.Text = English ? "Legend (click to multi-select)" : "图例（点击多选）";
        _clearBtn.Text = English ? "✕ Clear selection" : "✕ 取消选择";
    }

    /// <summary>刷新图例内容（只列当前视图出现的群系，保持已有选中）</summary>
    public void SetLegend(IEnumerable<(int Id, string Name, Color Color)> items)
    {
        _entries.Clear();
        _list.Controls.Clear();
        foreach (var it in items)
        {
            var entry = new LegendEntry(it.Id, it.Name, it.Color) { Checked = Selected.Contains(it.Id) };
            entry.Clicked += OnEntryClicked;
            _entries[it.Id] = entry;
            _list.Controls.Add(entry);
        }
        UpdateClearButton();
    }

    private void OnEntryClicked(int biomeId)
    {
        if (Selected.Contains(biomeId))
            Selected.Remove(biomeId);
        else
            Selected.Add(biomeId);
        if (_entries.TryGetValue(biomeId, out var entry))
            entry.Checked = Selected.Contains(biomeId);
        UpdateClearButton();
        SelectionChanged?.Invoke();
    }

    public void ClearSelection()
    {
        if (Selected.Count == 0) return;
        Selected.Clear();
        foreach (var e in _entries.Values) e.Checked = false;
        UpdateClearButton();
        SelectionChanged?.Invoke();
    }

    private void UpdateClearButton() => _clearBtn.Visible = Selected.Count > 0;
}
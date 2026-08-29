using TfcViewer;
using TfcViewer.Core;
using TfcViewer.World;
using TfcViewer.World.Region;

namespace TfcViewerGui;

/// <summary>主窗体：左图例 + 主画布(含气泡) + 右下小地图/拖动按钮 + 顶部工具条</summary>
public sealed class MainForm : Form
{
    private readonly Dictionary<string, int[]> _colors = BiomeColors.Load();

    private RegionGenerator _rg = null!;
    private double _centerBX = 0, _centerBZ = 0;
    private double _bpp = 16;
    private string _layer = "biome";
    private bool _rendering;
    private bool _pendingRender;

    private readonly CanvasView _canvas = new();
    private readonly LegendPanel _legend = new();
    private readonly MinimapView _minimap = new();
    private readonly CheckBox _dragBtn = new();
    private readonly Button _layerBtn = new();
    private readonly TextBox _seedBox = new();
    private readonly TextBox _xBox = new();
    private readonly TextBox _zBox = new();
    private readonly TextBox _scaleBox = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusText = new();

    private Point? _dragStart;
    private double _dragCenterBX, _dragCenterBZ;
    private int _viewW = 900, _viewH = 700;

    public MainForm(long seed)
    {
        Text = "TFC 群系查看器";
        Size = new Size(1280, 860);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9f);

        LoadEngine(seed);

        // ---- 顶部工具条 ----
        var top = new Panel { Dock = DockStyle.Top, Height = 40 };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(6, 6, 0, 0) };
        _seedBox.Text = seed.ToString();
        _seedBox.Width = 150;
        _xBox.Text = "0";
        _xBox.Width = 70;
        _zBox.Text = "0";
        _zBox.Width = 70;
        _scaleBox.Text = "16";
        _scaleBox.Width = 55;
        var loadBtn = new Button { Text = "加载", AutoSize = true };
        flow.Controls.Add(new Label { Text = "种子:", AutoSize = true, Anchor = AnchorStyles.Left });
        flow.Controls.Add(_seedBox);
        flow.Controls.Add(loadBtn);
        flow.Controls.Add(new Label { Text = "中心 X:", AutoSize = true });
        flow.Controls.Add(_xBox);
        flow.Controls.Add(new Label { Text = "Z:", AutoSize = true });
        flow.Controls.Add(_zBox);
        flow.Controls.Add(new Label { Text = "块/像素:", AutoSize = true });
        flow.Controls.Add(_scaleBox);
        var goBtn = new Button { Text = "跳转", AutoSize = true };
        flow.Controls.Add(goBtn);
        _layerBtn.Text = "图层: 群系";
        flow.Controls.Add(_layerBtn);
        top.Controls.Add(flow);

        // ---- 左图例 ----
        _legend.Dock = DockStyle.Left;
        _legend.SelectionChanged += () => _canvas.SetSelected(_legend.Selected);

        // ---- 主画布 ----
        _canvas.Dock = DockStyle.Fill;
        _canvas.ViewClick += OnCanvasClick;
        _canvas.CanvasMouseDown += e => { _dragStart = e.Location; _dragCenterBX = _centerBX; _dragCenterBZ = _centerBZ; };
        _canvas.CanvasMouseMove += e =>
        {
            if (_dragStart == null) return;
            _centerBX = _dragCenterBX + (e.X - _dragStart.Value.X) * _bpp;
            _centerBZ = _dragCenterBZ + (_dragStart.Value.Y - e.Y) * _bpp;
            UpdateMinimapFrame();
            UpdateStatus();
        };
        _canvas.CanvasMouseUp += _ =>
        {
            _dragStart = null;
            RenderAsync();
        };
        _canvas.MouseWheel += OnWheel;

        // ---- 右下：小地图 + 拖动按钮 ----
        var overlay = new Panel { BackColor = Color.Transparent, Size = new Size(260, 208), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        _dragBtn.Text = "拖动模式";
        _dragBtn.AutoSize = true;
        _dragBtn.CheckedChanged += (_, _) =>
        {
            _canvas.DragMode = _dragBtn.Checked;
            _canvas.ClearBubble();
            _canvas.Invalidate();
        };
        _minimap.Location = new Point(10, 36);
        _minimap.Jump += (cx, cz) => { _centerBX = cx; _centerBZ = cz; RenderAsync(); };
        overlay.Controls.Add(_dragBtn);
        overlay.Controls.Add(_minimap);

        // ---- 底部状态 ----
        _status.Items.Add(_statusText);

        Controls.Add(_canvas);
        Controls.Add(overlay);
        Controls.Add(_legend);
        Controls.Add(top);
        Controls.Add(_status);
        overlay.BringToFront();

        loadBtn.Click += (_, _) => TryLoadSeed();
        goBtn.Click += (_, _) => { ParseCenterFields(); RenderAsync(); };
        _layerBtn.Click += (_, _) =>
        {
            _layer = _layer switch { "biome" => "temperature", "temperature" => "rainfall", _ => "biome" };
            _layerBtn.Text = "图层: " + (_layer == "biome" ? "群系" : _layer == "temperature" ? "温度" : "降雨");
            _legend.Visible = _layer == "biome";
            _canvas.SetSelected(new HashSet<int>());
            _legend.ClearSelection();
            RenderAsync();
        };
        _canvas.Resize += (_, _) => RenderAsync();

        RenderAsync();
    }

    private void LoadEngine(long seed)
    {
        _rg = new RegionGenerator(Defaults.DefaultSettings(), Seed.Of(seed));
    }

    private void TryLoadSeed()
    {
        var ok = long.TryParse(_seedBox.Text.Trim(), out var seed);
        if (!ok) seed = Program.ParseSeed(_seedBox.Text.Trim());
        LoadEngine(seed);
        _centerBX = 0;
        _centerBZ = 0;
        RenderAsync();
    }

    private void ParseCenterFields()
    {
        if (double.TryParse(_xBox.Text, out var x)) _centerBX = x;
        if (double.TryParse(_zBox.Text, out var z)) _centerBZ = z;
        if (double.TryParse(_scaleBox.Text, out var s) && s > 0) _bpp = s;
    }

    private void OnCanvasClick(Point p)
    {
        _canvas.ClearBubble();
        int w = _canvas.Width, h = _canvas.Height;
        double minBX = _centerBX - w * _bpp / 2.0;
        double minBZ = _centerBZ - h * _bpp / 2.0;
        int bx = (int)Math.Floor(minBX + p.X * _bpp);
        int bz = (int)Math.Floor(minBZ + (h - 1 - p.Y) * _bpp);
        var anchor = p;

        Task.Run(() =>
        {
            var info = ViewerQuery.GetInfoAt(_rg, bx, bz);
            BeginInvoke(() =>
            {
                if (_dragBtn.Checked) return;
                _canvas.Bubble = new BubbleData
                {
                    Anchor = anchor,
                    Lines = new[]
                    {
                        $"区块坐标: ({info.ChunkX}, {info.ChunkZ})",
                        $"方块坐标: ({info.BlockX}, {info.BlockZ})",
                        $"预测群系: {info.BiomeId}",
                        $"温度: {info.Temperature:F1} °C",
                        $"降水: {info.Rainfall:F1} mm",
                        $"预测气候: {info.Climate}",
                        $"置信度: {info.ConfidencePercent}%",
                    },
                };
                _canvas.Invalidate();
            });
        });
    }

    private void OnWheel(object? sender, MouseEventArgs e)
    {
        if (_dragBtn.Checked) return;
        double factor = e.Delta > 0 ? 0.5 : 2.0;
        double newBpp = Math.Clamp(_bpp * factor, 0.25, 4096);
        if (Math.Abs(newBpp - _bpp) < 1e-9) return;
        int w = _canvas.Width, h = _canvas.Height;
        double bx = _centerBX + (e.X - w / 2.0) * _bpp;
        double bz = _centerBZ + (h / 2.0 - e.Y) * _bpp;
        _bpp = newBpp;
        _centerBX = bx - (e.X - w / 2.0) * _bpp;
        _centerBZ = bz - (h / 2.0 - e.Y) * _bpp;
        _canvas.ClearBubble();
        RenderAsync();
    }

    private void RenderAsync()
    {
        if (_rendering)
        {
            _pendingRender = true;
            return;
        }
        _rendering = true;
        int w = Math.Max(320, _canvas.Width), h = Math.Max(240, _canvas.Height);
        _viewW = w;
        _viewH = h;
        double cx = _centerBX, cz = _centerBZ, bpp = _bpp;
        string layer = _layer;

        Task.Run(() =>
        {
            try
            {
                var result = ViewRenderer.Render(_rg, w, h, cx, cz, bpp, layer, _colors);
                double mmSpan = 8 * Math.Max(w, h) * bpp;
                int mmw = _minimap.Width, mmh = _minimap.Height;
                var mm = ViewRenderer.Render(_rg, Math.Max(60, mmw), Math.Max(40, mmh), cx, cz, mmSpan / Math.Max(60, mmw), "biome", _colors);

                BeginInvoke(() =>
                {
                    _canvas.SetSource(result.Image, result.BiomeIds, w, h);
                    _minimap.Image = mm.Image;
                    _minimap.ViewCx = cx;
                    _minimap.ViewCz = cz;
                    _minimap.ViewSpan = Math.Max(w, h) * bpp;
                    _minimap.WorldSpan = mmSpan;
                    if (layer == "biome" && result.Present.Count > 0)
                    {
                        _legend.SetLegend(result.Present.OrderBy(p => p.Name, StringComparer.Ordinal));
                    }
                    UpdateStatus();
                    _minimap.Invalidate();
                });
            }
            catch (Exception ex)
            {
                BeginInvoke(() => _statusText.Text = "渲染错误: " + ex.Message);
            }
            finally
            {
                _rendering = false;
            }
            if (_pendingRender)
            {
                _pendingRender = false;
                BeginInvoke(RenderAsync);
            }
        });
    }

    private void UpdateMinimapFrame()
    {
        _minimap.ViewCx = _centerBX;
        _minimap.ViewCz = _centerBZ;
        _minimap.Invalidate();
    }

    private void UpdateStatus()
    {
        double span = Math.Max(_viewW, _viewH) * _bpp;
        _statusText.Text = string.Format("中心 ({0:F0}, {1:F0}) 块 — 视图约 {2:F1} km × {3:F1} km — {4:F2} 块/像素 — 图层: {5}",
            _centerBX, _centerBZ, span * 0.0625, span * 0.0625, _bpp,
            _layer == "biome" ? "群系" : _layer == "temperature" ? "温度" : "降雨");
    }
}
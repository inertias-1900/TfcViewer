using TfcViewer;
using TfcViewer.Core;
using TfcViewer.World;
using TfcViewer.World.Region;

namespace TfcViewerGui;

/// <summary>
/// 主窗体：窗口固定大小；左图例 + 512×512 主画布；右侧栏（上部状态显示区 + 下部整体预览/拖动按钮）。
/// 右上角语言切换（中文 ⇄ English UI）。
/// </summary>
public sealed class MainForm : Form
{
    private readonly Dictionary<string, int[]> _colors = BiomeColors.Load();
    private readonly Dictionary<string, string> _zh = BiomeZh.Load();

    private RegionGenerator _rg = null!;
    private double _centerBX = 0, _centerBZ = 0;
    private double _bpp = 16;                    // 默认 16 = 每像素 1 chunk → 512×512 chunk 视图
    private string _layer = "biome";
    private bool _rendering;
    private bool _pendingRender;
    private string _renderState = "就绪";
    private bool _en;                            // true = 英文界面

    private readonly CanvasView _canvas = new() { Size = new Size(512, 512) };
    private readonly LegendPanel _legend = new();
    private readonly MinimapView _minimap = new();
    private readonly ToggleButton _dragBtn = new("拖动模式");
    private readonly Button _layerBtn = new();
    private readonly Button _langBtn = new();
    private readonly TextBox _seedBox = new();
    private readonly TextBox _xBox = new();
    private readonly TextBox _zBox = new();
    private readonly TextBox _scaleBox = new();
    private readonly Label _statusText = new();
    private readonly Label _statusTitle = new();
    private readonly List<(int Id, string Name, Color Color)> _lastPresent = new();

    // 工具条文本控件（语言切换时更新）
    private readonly Label _seedLbl = new(), _xLbl = new(), _zLbl = new(), _scaleLbl = new();
    private readonly Button _loadBtn = new(), _goBtn = new();

    private Point? _dragStart;
    private double _dragCenterBX, _dragCenterBZ;

    public MainForm(long seed)
    {
        Text = "TFC 群系查看器";
        FormBorderStyle = FormBorderStyle.FixedSingle;   // 固定窗口，不允许拉伸
        MaximizeBox = false;
        ClientSize = new Size(1072, 576);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9f);
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath); } catch { /* 任务栏图标尽力而为 */ }

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
        flow.Controls.Add(_seedLbl);
        flow.Controls.Add(_seedBox);
        flow.Controls.Add(_loadBtn);
        flow.Controls.Add(_xLbl);
        flow.Controls.Add(_xBox);
        flow.Controls.Add(_zLbl);
        flow.Controls.Add(_zBox);
        flow.Controls.Add(_scaleLbl);
        flow.Controls.Add(_scaleBox);
        flow.Controls.Add(_goBtn);
        flow.Controls.Add(_layerBtn);
        top.Controls.Add(flow);

        // 右上角语言切换：中文界面显示 "English UI"，英文界面显示 "中文界面"
        _langBtn.Dock = DockStyle.Right;
        _langBtn.Width = 110;
        _langBtn.FlatStyle = FlatStyle.System;
        _langBtn.Click += (_, _) => { _en = !_en; ApplyLanguage(); };
        top.Controls.Add(_langBtn);

        // ---- 左图例 ----
        _legend.Dock = DockStyle.Left;
        _legend.Width = 250;
        _legend.SelectionChanged += () => _canvas.SetSelected(_legend.Selected);

        // ---- 右侧栏：上部状态显示区 + 下部整体预览/拖动按钮 ----
        var right = new Panel { Dock = DockStyle.Right, Width = 290, BackColor = Color.FromArgb(238, 240, 244) };

        var statusPanel = new Panel { Dock = DockStyle.Top, Height = 128, BackColor = Color.FromArgb(238, 240, 244), Padding = new Padding(10, 8, 10, 4) };
        _statusTitle.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
        _statusTitle.AutoSize = true;
        _statusTitle.Location = new Point(10, 8);
        _statusText.Location = new Point(10, 32);
        _statusText.AutoSize = false;
        _statusText.Size = new Size(270, 92);
        _statusText.Font = new Font("Microsoft YaHei UI", 9f);
        statusPanel.Controls.Add(_statusText);
        statusPanel.Controls.Add(_statusTitle);

        _minimap.Dock = DockStyle.Bottom;
        _minimap.Height = 172;
        _minimap.Jump += (cx, cz) => { _centerBX = cx; _centerBZ = cz; RenderAsync(); };
        _dragBtn.Dock = DockStyle.Bottom;
        _dragBtn.Height = 30;
        _dragBtn.CheckedChanged += on =>
        {
            _canvas.DragMode = on;
            _canvas.ClearBubble();
            _canvas.Invalidate();
        };
        right.Controls.Add(_minimap);
        right.Controls.Add(_dragBtn);
        right.Controls.Add(statusPanel);

        // ---- 主画布（固定 512×512，位于图例与右侧栏之间）----
        _canvas.Location = new Point(260, 54);
        _canvas.ViewClick += OnCanvasClick;
        _canvas.CanvasMouseDown += e => { _dragStart = e.Location; _dragCenterBX = _centerBX; _dragCenterBZ = _centerBZ; };
        _canvas.CanvasMouseMove += e =>
        {
            if (_dragStart == null) return;
            // 拖动期间：只移动视图中心 + 更新小地图范围框（主画面松手后才渲染）
            _centerBX = _dragCenterBX + (e.X - _dragStart.Value.X) * _bpp;
            _centerBZ = _dragCenterBZ + (_dragStart.Value.Y - e.Y) * _bpp;
            _minimap.ViewCx = _centerBX;
            _minimap.ViewCz = _centerBZ;
            _minimap.Invalidate();
            UpdateStatus();
        };
        _canvas.CanvasMouseUp += _ =>
        {
            _dragStart = null;
            RenderAsync();
        };
        _canvas.MouseWheel += OnWheel;

        Controls.Add(_canvas);
        Controls.Add(_legend);
        Controls.Add(right);
        Controls.Add(top);

        _loadBtn.Click += (_, _) => TryLoadSeed();
        _goBtn.Click += (_, _) => { ParseCenterFields(); RenderAsync(); };
        _layerBtn.Click += (_, _) =>
        {
            _layer = _layer switch { "biome" => "temperature", "temperature" => "rainfall", _ => "biome" };
            _layerBtn.Text = _en ? "Layer: " + LayerNameEn : "图层: " + LayerNameZh;
            _legend.Visible = _layer == "biome";
            _canvas.SetSelected(new HashSet<int>());
            _legend.ClearSelection();
            RenderAsync();
        };

        _langBtn.Text = "English UI";
        ApplyLanguage();
        RenderAsync();
    }

    private string LayerNameZh => _layer == "biome" ? "群系" : _layer == "temperature" ? "温度" : "降雨";
    private string LayerNameEn => _layer == "biome" ? "Biome" : _layer == "temperature" ? "Temperature" : "Rainfall";

    /// <summary>按 _en 刷新所有界面文本</summary>
    private void ApplyLanguage()
    {
        _langBtn.Text = _en ? "中文界面" : "English UI";
        Text = _en ? "TFC Biome Viewer" : "TFC 群系查看器";
        _seedLbl.Text = _en ? "Seed:" : "种子:";
        _loadBtn.Text = _en ? "Load" : "加载";
        _xLbl.Text = _en ? "Center X:" : "中心 X:";
        _zLbl.Text = _en ? "Z:" : "Z:";
        _scaleLbl.Text = _en ? "Blocks/px:" : "块/像素:";
        _goBtn.Text = _en ? "Go" : "跳转";
        _layerBtn.Text = _en ? "Layer: " + LayerNameEn : "图层: " + LayerNameZh;
        _dragBtn.Text = _en ? "Drag mode" : "拖动模式";
        _statusTitle.Text = _en ? "Status" : "状态";
        _legend.English = _en;
        _legend.ApplyLanguage();
        _canvas.English = _en;
        _minimap.English = _en;
        if (_layer == "biome" && _lastPresent.Count > 0)
        {
            _legend.SetLegend(_lastPresent.Select(p => (p.Id, DisplayName(p.Name), p.Color)));
        }
        _canvas.ClearBubble();
        _canvas.Invalidate();
        _minimap.Invalidate();
        _renderState = _en ? "Ready" : "就绪";
        UpdateStatus();
    }

    private string DisplayName(string biomeId)
        => !_en && _zh.TryGetValue(biomeId, out var cn) ? $"{cn}({biomeId})" : biomeId;

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
        if (_dragBtn.Checked) return;
        // 点击气泡内部 → 只关闭气泡，不弹新气泡
        if (_canvas.HitTestBubble(p))
        {
            _canvas.ClearBubble();
            return;
        }
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
                    Lines = _en
                        ? new[]
                        {
                            $"Chunk: ({info.ChunkX}, {info.ChunkZ})",
                            $"Block: ({info.BlockX}, {info.BlockZ})",
                            $"Biome: {info.BiomeId}",
                            $"Temperature: {info.Temperature:F1} °C",
                            $"Rainfall: {info.Rainfall:F1} mm",
                            $"Climate: {ViewerQuery.ClimateLocalized(info.Climate, true)}",
                            $"Confidence: {info.ConfidencePercent}%",
                        }
                        : new[]
                        {
                            $"区块坐标: ({info.ChunkX}, {info.ChunkZ})",
                            $"方块坐标: ({info.BlockX}, {info.BlockZ})",
                            $"预测群系: {DisplayName(info.BiomeId)}",
                            $"温度: {info.Temperature:F1} °C",
                            $"降水: {info.Rainfall:F1} mm",
                            $"预测气候: {ViewerQuery.ClimateLocalized(info.Climate, false)}",
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
        _scaleBox.Text = _bpp.ToString("0.##");
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
        _renderState = _en ? "Rendering..." : "正在重新计算...";
        _canvas.Rendering = true;
        _canvas.Invalidate();
        UpdateStatus();
        int w = _canvas.Width, h = _canvas.Height;
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
                        _lastPresent.Clear();
                        _lastPresent.AddRange(result.Present.Select(p => (p.Id, p.Name, p.Color)));
                        _legend.SetLegend(_lastPresent.Select(p => (p.Id, DisplayName(p.Name), p.Color)));
                    }
                    _renderState = _en ? "Ready" : "就绪";
                    UpdateStatus();
                    _minimap.Invalidate();
                    _canvas.Rendering = false;
                    _canvas.Invalidate();
                });
            }
            catch (Exception ex)
            {
                BeginInvoke(() =>
                {
                    _renderState = (_en ? "Error: " : "错误: ") + ex.Message;
                    UpdateStatus();
                    _canvas.Rendering = false;
                    _canvas.Invalidate();
                });
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

    private void UpdateStatus()
    {
        double span = Math.Max(_canvas.Width, _canvas.Height) * _bpp;
        if (_en)
        {
            _statusText.Text = string.Format(
                "Center: ({0:F0}, {1:F0})\nView: {2:F0}×{3:F0} chunks ({4:F0}×{5:F0} blocks / {6:F1}×{7:F1} km)\nBlocks/px: {8:F2}\nLayer: {9}\nRender: {10}",
                _centerBX, _centerBZ, span * 0.0625, span * 0.0625, span, span, span * 0.0625, span * 0.0625,
                _bpp, LayerNameEn, _renderState);
        }
        else
        {
            _statusText.Text = string.Format(
                "中心: ({0:F0}, {1:F0}) 块\n视图: {2:F0}×{3:F0} chunk ({4:F0}×{5:F0} 块 / {6:F1}×{7:F1} km)\n块/像素: {8:F2}\n图层: {9}\n渲染: {10}",
                _centerBX, _centerBZ, span * 0.0625, span * 0.0625, span, span, span * 0.0625, span * 0.0625,
                _bpp, LayerNameZh, _renderState);
        }
    }
}
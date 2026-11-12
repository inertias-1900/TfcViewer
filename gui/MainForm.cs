using TfcViewer;
using TfcViewer.Climate;
using TfcViewer.Core;
using TfcViewer.Storage;
using TfcViewer.World;
using TfcViewer.World.Region;

namespace TfcViewerGui;

/// <summary>
/// 主窗体：左图例 + 中央画布 + 右侧栏（状态 / 筛选模式 / 小地图）。
/// 筛选模式同时决定画面配色、图例分类与点击高亮；右键按住主画面可实时平移。
/// </summary>
public sealed class MainForm : Form
{
    private const int ViewSize = 512;       // 主画布可见尺寸

    private readonly Dictionary<string, int[]> _colors = BiomeColors.Load();
    private readonly Dictionary<string, string> _zh = BiomeZh.Load();

    private RegionGenerator _rg = null!;
    private ClimateProfile _profile = null!;
    private readonly int _layerY = ClimateModel.SeaLevelY;

    private double _centerBX, _centerBZ;
    private double _bpp = 16;
    private long _seed;
    private FilterMode _filter = FilterMode.Biome;
    private RenderResult? _lastResult;
    private readonly Button[] _filterBtns = new Button[4];
    private readonly Label _filterLbl = new();

    private bool _rendering, _pendingRender;
    private int _renderGeneration;
    private string _renderState = "就绪";
    private bool _en;

    private readonly CanvasView _canvas = new() { Size = new Size(ViewSize, ViewSize) };
    private readonly LegendPanel _legend = new();
    private readonly MinimapView _minimap = new();
    private readonly Button _langBtn = new();
    private readonly TextBox _seedBox = new();
    private readonly TextBox _xBox = new();
    private readonly TextBox _zBox = new();
    private readonly TextBox _scaleBox = new();
    private readonly Label _statusText = new();
    private readonly Label _statusTitle = new();
    private readonly Label _seedLbl = new(), _xLbl = new(), _zLbl = new(), _scaleLbl = new();
    private readonly Button _loadBtn = new(), _goBtn = new();

    public MainForm(long seed)
    {
        Text = "TFC 群系查看器 v1.1.0";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(1072, 576);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9f);
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath); } catch { /* 尽力而为 */ }

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
        top.Controls.Add(flow);

        _langBtn.Dock = DockStyle.Right;
        _langBtn.Width = 110;
        _langBtn.FlatStyle = FlatStyle.System;
        _langBtn.Click += (_, _) => { _en = !_en; ApplyLanguage(); };
        top.Controls.Add(_langBtn);

        // ---- 左图例 ----
        _legend.Dock = DockStyle.Left;
        _legend.Width = 250;
        _legend.SelectionChanged += () => _canvas.SetSelected(_legend.Selected);

        // ---- 右侧栏 ----
        var right = new Panel { Dock = DockStyle.Right, Width = 290, BackColor = Color.FromArgb(238, 240, 244) };

        var statusPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(238, 240, 244), Padding = new Padding(10, 8, 10, 4) };
        _statusTitle.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
        _statusTitle.AutoSize = true;
        _statusTitle.Location = new Point(10, 8);
        _statusText.Location = new Point(10, 32);
        _statusText.AutoSize = false;
        _statusText.Size = new Size(270, 110);
        _statusText.Font = new Font("Microsoft YaHei UI", 9f);
        statusPanel.Controls.Add(_statusText);
        statusPanel.Controls.Add(_statusTitle);

        // 筛选模式（决定配色 + 图例 + 高亮）
        var filterPanel = new Panel { Dock = DockStyle.Bottom, Height = 78, BackColor = Color.FromArgb(238, 240, 244), Padding = new Padding(10, 6, 10, 4) };
        _filterLbl.AutoSize = true;
        _filterLbl.Location = new Point(10, 4);
        _filterLbl.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        filterPanel.Controls.Add(_filterLbl);
        var modes = new[] { FilterMode.Biome, FilterMode.Temperature, FilterMode.Rainfall, FilterMode.Climate };
        for (int i = 0; i < modes.Length; i++)
        {
            var btn = new Button
            {
                FlatStyle = FlatStyle.System,
                Size = new Size(63, 26),
                Location = new Point(10 + i * 67, 26),
                Tag = modes[i],
            };
            btn.Click += (s, _) => SetFilter((FilterMode)((Button)s!).Tag!);
            _filterBtns[i] = btn;
            filterPanel.Controls.Add(btn);
        }

        _minimap.Dock = DockStyle.Bottom;
        _minimap.Height = 172;
        _minimap.Jump += (cx, cz) =>
        {
            _centerBX = cx;
            _centerBZ = cz;
            _canvas.SetView(cx, cz);
            SyncCenterFields();
            RenderAsync();
        };

        right.Controls.Add(_minimap);
        right.Controls.Add(filterPanel);
        right.Controls.Add(statusPanel);

        // ---- 主画布 ----
        _canvas.Location = new Point(260, 54);
        _canvas.ViewClick += OnCanvasClick;
        _canvas.MouseWheel += OnWheel;

        Controls.Add(_canvas);
        Controls.Add(_legend);
        Controls.Add(right);
        Controls.Add(top);

        _loadBtn.Click += (_, _) => TryLoadSeed();
        _goBtn.Click += (_, _) => { ParseCenterFields(); _canvas.ViewBpp = _bpp; _canvas.SetView(_centerBX, _centerBZ); RenderAsync(); };

        _langBtn.Text = "English UI";
        UpdateFilterButtons();
        ApplyLanguage();
        RenderAsync();
    }

    /// <summary>切换筛选模式：配色、图例、高亮分类全部随之改变</summary>
    private void SetFilter(FilterMode mode)
    {
        if (_filter == mode) return;
        _filter = mode;
        _legend.ClearSelection();
        _canvas.SetSelected(new HashSet<int>());
        _canvas.ClearBubble();
        UpdateFilterButtons();
        RenderAsync();
    }

    private void UpdateFilterButtons()
    {
        _filterLbl.Text = _en ? "Filter (recolours map + legend)" : "筛选模式（重涂地图 + 换图例）";
        for (int i = 0; i < _filterBtns.Length; i++)
        {
            var btn = _filterBtns[i];
            if (btn == null) continue;
            var mode = (FilterMode)btn.Tag!;
            btn.Text = Filters.ModeName(mode, _en);
            bool active = mode == _filter;
            btn.Font = new Font("Microsoft YaHei UI", 9f, active ? FontStyle.Bold : FontStyle.Regular);
            btn.BackColor = active ? Color.FromArgb(205, 225, 250) : SystemColors.Control;
        }
        _legend.SetTitle(Filters.Title(_filter, _en));
    }

    private void ApplyLanguage()
    {
        _langBtn.Text = _en ? "中文界面" : "English UI";
        Text = _en ? "TFC Biome Viewer v1.1.0" : "TFC 群系查看器 v1.1.0";
        _seedLbl.Text = _en ? "Seed:" : "种子:";
        _loadBtn.Text = _en ? "Load" : "加载";
        _xLbl.Text = _en ? "Center X:" : "中心 X:";
        _zLbl.Text = _en ? "Z:" : "Z:";
        _scaleLbl.Text = _en ? "Blocks/px:" : "块/像素:";
        _goBtn.Text = _en ? "Go" : "跳转";
        _statusTitle.Text = _en ? "Status" : "状态";
        _legend.English = _en;
        _legend.ApplyLanguage();
        _canvas.English = _en;
        _minimap.English = _en;
        UpdateFilterButtons();
        if (_lastResult != null) UpdateLegend(_lastResult);
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
        _seed = seed;
        _rg = new RegionGenerator(Defaults.DefaultSettings(), Seed.Of(seed));
        _profile = new ClimateProfile(new ClimateModel(seed, _rg.Settings.TemperatureScale), TfcSave.DefaultDaysInMonth);
        _canvas.TemperatureScale = _rg.Settings.TemperatureScale;
    }

    private void TryLoadSeed()
    {
        if (!long.TryParse(_seedBox.Text.Trim(), out var seed))
            seed = Program.ParseSeed(_seedBox.Text.Trim());
        LoadEngine(seed);
        _centerBX = 0;
        _centerBZ = 0;
        _canvas.SetView(0, 0);
        RenderAsync();
    }

    private void ParseCenterFields()
    {
        if (double.TryParse(_xBox.Text, out var x)) _centerBX = x;
        if (double.TryParse(_zBox.Text, out var z)) _centerBZ = z;
        if (double.TryParse(_scaleBox.Text, out var s) && s > 0) _bpp = s;
    }

    private void SyncCenterFields()
    {
        _xBox.Text = ((long)_centerBX).ToString();
        _zBox.Text = ((long)_centerBZ).ToString();
        UpdateStatus();
    }

    private void OnCanvasClick(Point p)
    {
        if (_canvas.HitTestBubble(p))
        {
            _canvas.ClearBubble();
            return;
        }
        _canvas.ClearBubble();

        var (bx, bz) = _canvas.ToWorld(p);
        var anchor = p;

        Task.Run(() =>
        {
            var info = ViewerQuery.GetInfoAt(_rg, bx, bz);
            float avgTemp = ClimateQuery.AverageTempAt(_rg, bx, bz);
            float avgRain = ClimateQuery.RainfallAt(_rg, bx, bz);
            var range = _profile.Range(bz, _layerY, avgTemp);
            string winter = range.Min < ClimateModel.IceFreezeTemperature
                ? (_en ? "freezes over" : "会结冰")
                : range.Min < ClimateModel.SnowFreezeTemperature
                    ? (_en ? "snow settles" : "会积雪")
                    : (_en ? "no frost" : "不会结冰");
            int koppen = KoppenClimate.Classify(avgTemp, avgRain);

            BeginInvoke(() =>
            {
                _canvas.Bubble = new BubbleData
                {
                    Anchor = anchor,
                    Lines = _en
                        ? new[]
                        {
                            $"Chunk: ({info.ChunkX}, {info.ChunkZ})",
                            $"Block: ({info.BlockX}, {info.BlockZ})",
                            $"Biome: {info.BiomeId}",
                            $"Base temp: {avgTemp:F1} °C",
                            $"Year range: {range.Min:F1} ~ {range.Max:F1} °C",
                            $"Year mean: {range.Mean:F1} °C (swing {range.Span:F1})",
                            $"Rainfall: {avgRain:F0} mm",
                            $"Worldgen zone: {ViewerQuery.ClimateLocalized(info.Climate, true)}",
                            $"Köppen: {KoppenClimate.Name(koppen, true)}",
                            $"Winter: {winter} (y={_layerY})",
                            $"Confidence: {info.ConfidencePercent}%",
                        }
                        : new[]
                        {
                            $"区块坐标: ({info.ChunkX}, {info.ChunkZ})",
                            $"方块坐标: ({info.BlockX}, {info.BlockZ})",
                            $"预测群系: {DisplayName(info.BiomeId)}",
                            $"基础温度: {avgTemp:F1} °C",
                            $"年温范围: {range.Min:F1} ~ {range.Max:F1} °C",
                            $"年均温: {range.Mean:F1} °C（年温差 {range.Span:F1}）",
                            $"降水: {avgRain:F0} mm",
                            $"预测群系类型: {ViewerQuery.ClimateLocalized(info.Climate, false)}",
                            $"气候分类: {KoppenClimate.Name(koppen, false)}",
                            $"冬季: {winter}（海拔 y={_layerY}）",
                            $"置信度: {info.ConfidencePercent}%",
                        },
                };
                _canvas.Invalidate();
            });
        });
    }

    private void OnWheel(object? sender, MouseEventArgs e)
    {
        double factor = e.Delta > 0 ? 0.5 : 2.0;
        double newBpp = Math.Clamp(_bpp * factor, 0.25, 4096);
        if (Math.Abs(newBpp - _bpp) < 1e-9) return;

        int w = _canvas.Width, h = _canvas.Height;
        double bx = _centerBX + (e.X - w / 2.0) * _bpp;
        double bz = _centerBZ + (h / 2.0 - e.Y) * _bpp;
        _bpp = newBpp;
        _centerBX = bx - (e.X - w / 2.0) * _bpp;
        _centerBZ = bz - (h / 2.0 - e.Y) * _bpp;

        _scaleBox.Text = _bpp.ToString("0.##");
        _canvas.ViewBpp = _bpp;
        _canvas.SetView(_centerBX, _centerBZ);
        SyncCenterFields();
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
        UpdateStatus();

        int vw = _canvas.Width, vh = _canvas.Height;
        int mmw = Math.Max(60, _minimap.Width);
        int mmh = Math.Max(40, _minimap.Height);
        double cx = _centerBX, cz = _centerBZ, bpp = _bpp;
        var filter = _filter;
        int gen = ++_renderGeneration;
        var settings = _rg.Settings;
        var profile = _profile;
        var colors = _colors;
        long seed = _seed;
        int threads = Math.Clamp(Environment.ProcessorCount, 1, 16);
        double mmBpp = 8.0 * Math.Max(vw, vh) * bpp / mmw;

        Task.Run(() =>
        {
            try
            {
                // 主视图：并行分带渲染（每带独立 RegionGenerator，构造仅 ~15 ms）
                var result = ViewRenderer.RenderParallel(settings, seed, vw, vh, cx, cz, bpp, colors, profile, _layerY, filter, threads);

                // 小地图（范围大、像素少，线程数减半即可）
                var mm = ViewRenderer.RenderParallel(settings, seed, mmw, mmh, cx, cz, mmBpp, colors, profile, _layerY, filter,
                    Math.Clamp(threads / 2, 1, 8));

                BeginInvoke(() =>
                {
                    if (gen != _renderGeneration) return;

                    _canvas.SetBuffer(result.Image, result.ClassIds, vw, vh,
                        cx - vw * bpp / 2.0, cz - vh * bpp / 2.0, bpp);
                    _canvas.ViewBpp = bpp;
                    _canvas.SetView(cx, cz);

                    _minimap.Image = mm.Image;
                    _minimap.Bpp = mmBpp;
                    _minimap.ViewCx = cx;
                    _minimap.ViewCz = cz;
                    _minimap.ViewSpan = Math.Max(vw, vh) * bpp;

                    _lastResult = result;
                    UpdateLegend(result);
                    _renderState = _en ? "Ready" : "就绪";
                    _canvas.Rendering = false;
                    UpdateStatus();
                    _minimap.Invalidate();
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
    private void UpdateLegend(RenderResult result)
    {
        var items = new List<(int Id, string Name, Color Color)>();
        int total = Math.Max(1, result.W * result.H);

        if (_filter == FilterMode.Biome)
        {
            foreach (var kv in result.Counts.OrderByDescending(k => k.Value))
            {
                string name = TfcViewer.World.Layer.TFCLayers.GetFromLayerId(kv.Key);
                var color = result.BiomePalette.TryGetValue(kv.Key, out var c) ? c : Color.Gray;
                items.Add((kv.Key, DisplayName(name), color));
            }
        }
        else
        {
            foreach (var kv in result.Counts.OrderBy(k => k.Key))
            {
                items.Add((kv.Key, $"{Filters.Name(_filter, kv.Key, _en)}  {kv.Value * 100 / total}%", Filters.ColorOf(_filter, kv.Key)));
            }
        }

        _legend.SetTitle(Filters.Title(_filter, _en));
        _legend.SetLegend(items);
    }

    private void UpdateStatus()
    {
        double span = Math.Max(_canvas.Width, _canvas.Height) * _bpp;
        if (_en)
        {
            _statusText.Text = string.Format(
                "Center: ({0:F0}, {1:F0})\nView: {2:F0} blocks ({3:F1} km)\nBlocks/px: {4:F2}\nFilter: {5}\nRender: {6}\n\nRight-drag to pan · wheel to zoom",
                _centerBX, _centerBZ, span, span / 1000.0, _bpp,
                Filters.ModeName(_filter, true), _renderState);
        }
        else
        {
            _statusText.Text = string.Format(
                "中心: ({0:F0}, {1:F0}) 块\n视图: {2:F0} 块（{3:F1} km）\n块/像素: {4:F2}\n筛选: {5}\n渲染: {6}\n\n右键按住拖动 · 滚轮缩放",
                _centerBX, _centerBZ, span, span / 1000.0, _bpp,
                Filters.ModeName(_filter, false), _renderState);
        }
    }
}

namespace TfcViewerGui;

/// <summary>点击信息气泡数据</summary>
public sealed class BubbleData
{
    public Point Anchor;        // 画布坐标（锚点）
    public string[] Lines = Array.Empty<string>();
}

/// <summary>
/// 主画布：显示一块比视口大 3 倍的渲染缓冲区，按当前视口裁剪绘制。
/// 右键按住可实时平移（只移动视口，不重渲染）；松开后由外部重渲染新缓冲区。
/// 左键单击查询；选中分类后未选中项灰度化、选中项白描边。
/// </summary>
public sealed class CanvasView : UserControl
{
    // ---- 缓冲区 ----
    private Bitmap? _source;                 // 原始渲染缓冲
    private Bitmap? _display;                // 经过选中处理后的缓冲（无选中时与 _source 同一对象）
    private int[] _classIds = Array.Empty<int>();   // 缓冲像素的分类 id
    private int _bufW, _bufH;
    private double _bufMinBX, _bufMinBZ, _bufBpp;

    // ---- 视口 ----
    public double ViewCenterBX, ViewCenterBZ;
    public double ViewBpp = 16;

    public HashSet<int> Selected { get; private set; } = new();
    public bool Rendering;
    public bool English;

    // 纬度刻度
    public int TemperatureScale = 20000;
    public bool ShowLatitude = true;

    public BubbleData? Bubble;

    /// <summary>左键单击（画布坐标）</summary>
    public event Action<Point>? ViewClick;

    public CanvasView()
    {
        BackColor = Color.FromArgb(12, 16, 30);
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Cross;
    }

    /// <summary>设置缓冲区：位图 + 分类矩阵 + 缓冲区覆盖的世界范围</summary>
    public void SetBuffer(Bitmap bmp, int[] classIds, int w, int h, double minBX, double minBZ, double bpp)
    {
        DisposeBuffers();
        _source = bmp;
        _classIds = classIds ?? Array.Empty<int>();
        _bufW = w;
        _bufH = h;
        _bufMinBX = minBX;
        _bufMinBZ = minBZ;
        _bufBpp = bpp;
        RebuildDisplay();
        Invalidate();
    }

    public void SetSelected(HashSet<int> selected)
    {
        Selected = new HashSet<int>(selected);
        RebuildDisplay();
        Invalidate();
    }

    public void SetView(double centerBX, double centerBZ)
    {
        ViewCenterBX = centerBX;
        ViewCenterBZ = centerBZ;
        Invalidate();
    }

    public void ClearBubble()
    {
        if (Bubble == null) return;
        Bubble = null;
        Invalidate();
    }

    private void DisposeBuffers()
    {
        if (!ReferenceEquals(_display, _source)) _display?.Dispose();
        _display = null;
        _source?.Dispose();
        _source = null;
        _classIds = Array.Empty<int>();
        _bufW = _bufH = 0;
    }

    /// <summary>后处理：无选中→原图；有选中→未选中灰度化、选中原色+8邻域描白边</summary>
    private void RebuildDisplay()
    {
        if (!ReferenceEquals(_display, _source)) _display?.Dispose();
        _display = null;

        var src = _source;
        if (src == null)
        {
            Invalidate();
            return;
        }

        if (Selected.Count == 0 || _classIds.Length != _bufW * _bufH)
        {
            _display = src;
            Invalidate();
            return;
        }

        int w = _bufW, h = _bufH;
        var outp = new Bitmap(w, h);
        var rect = new Rectangle(0, 0, w, h);
        var srcData = src.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var outData = outp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        unsafe
        {
            byte* s0 = (byte*)srcData.Scan0;
            byte* d0 = (byte*)outData.Scan0;
            int stride = srcData.Stride;
            bool[] isSel = new bool[w * h];
            for (int i = 0; i < _classIds.Length; i++) isSel[i] = Selected.Contains(_classIds[i]);

            for (int y = 0; y < h; y++)
            {
                byte* sRow = s0 + y * stride;
                byte* dRow = d0 + y * stride;
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    byte* s = sRow + x * 4;
                    byte* d = dRow + x * 4;
                    if (isSel[i])
                    {
                        bool edge = false;
                        for (int dy = -1; dy <= 1 && !edge; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = x + dx, ny = y + dy;
                                if (nx < 0 || ny < 0 || nx >= w || ny >= h || !isSel[ny * w + nx])
                                {
                                    edge = true;
                                    break;
                                }
                            }
                        }
                        if (edge)
                        {
                            d[0] = d[1] = d[2] = 255;
                        }
                        else
                        {
                            d[2] = s[2];
                            d[1] = s[1];
                            d[0] = s[0];
                        }
                        d[3] = 255;
                    }
                    else
                    {
                        int gray = (s[2] * 299 + s[1] * 587 + s[0] * 114) / 1000;
                        d[2] = d[1] = d[0] = (byte)gray;
                        d[3] = 255;
                    }
                }
            }
        }
        src.UnlockBits(srcData);
        outp.UnlockBits(outData);
        _display = outp;
        Invalidate();
    }

    // ---- 坐标换算 ----

    /// <summary>画布坐标 → 世界方块坐标（顶部 = 较大 Z，与渲染行序一致）</summary>
    public (int Bx, int Bz) ToWorld(Point p)
    {
        double bx = ViewCenterBX + (p.X - Width / 2.0) * ViewBpp;
        double bz = ViewCenterBZ + (Height / 2.0 - p.Y) * ViewBpp;
        return ((int)Math.Floor(bx), (int)Math.Floor(bz));
    }

    /// <summary>画布第 y 行对应的世界 Z</summary>
    public double WorldZAtRow(int y) => ViewCenterBZ + (Height / 2.0 - y) * ViewBpp;

    /// <summary>TFC 的纬度：温度纬度轴 z=0 为赤道，|z| = temperatureScale/2 为极点（90°）</summary>
    public static double LatitudeAt(double z, int temperatureScale)
    {
        double pole = Math.Max(1.0, temperatureScale / 2.0);
        double period = pole * 2;
        double d = z % period;
        if (d < 0) d += period;
        double t = d <= pole ? d : period - d;
        return 90.0 * t / pole;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        var img = _display;
        if (img != null && _bufW > 0)
        {
            // 视口左上角与顶部对应的世界坐标
            double viewLeft = ViewCenterBX - Width * ViewBpp / 2.0;
            double viewTopZ = ViewCenterBZ + Height * ViewBpp / 2.0;

            double srcX = (viewLeft - _bufMinBX) / _bufBpp;
            double srcTop = (_bufMinBZ + (_bufH - 1) * _bufBpp - viewTopZ) / _bufBpp;
            double srcW = Math.Min(_bufW, Width * ViewBpp / _bufBpp);
            double srcH = Math.Min(_bufH, Height * ViewBpp / _bufBpp);

            // 只绘制视口对应的子区域（clamp 在缓冲内），避免 GDI 每次处理整张缓冲
            srcX = Math.Clamp(srcX, 0, Math.Max(0, _bufW - srcW));
            srcTop = Math.Clamp(srcTop, 0, Math.Max(0, _bufH - srcH));

            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(img,
                new RectangleF(0, 0, Width, Height),
                new RectangleF((float)srcX, (float)srcTop, (float)srcW, (float)srcH),
                GraphicsUnit.Pixel);
        }
        else
        {
            using var f = new Font("Microsoft YaHei UI", 12f);
            using var b = new SolidBrush(Color.White);
            g.DrawString(English ? "Rendering..." : "渲染中...", f, b, 10, 10);
        }

        DrawLatitude(g);
        DrawBubble(g);

        if (Rendering && _display == null)
        {
            using var cover = new SolidBrush(Color.FromArgb(190, 238, 238, 238));
            g.FillRectangle(cover, 0, 0, Width, Height);
            using var f = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold);
            using var tb = new SolidBrush(Color.FromArgb(70, 70, 70));
            var msg = English ? "Rendering..." : "正在重新计算";
            var sz = g.MeasureString(msg, f);
            g.DrawString(msg, f, tb, (Width - sz.Width) / 2f, (Height - sz.Height) / 2f);
        }
    }

    private RectangleF _bubbleRect = RectangleF.Empty;

    /// <summary>左右两侧的纬度刻度小字</summary>
    private void DrawLatitude(Graphics g)
    {
        if (!ShowLatitude || _display == null) return;

        using var f = new Font("Microsoft YaHei UI", 8f);
        using var bg = new SolidBrush(Color.FromArgb(155, 255, 255, 255));
        using var tb = new SolidBrush(Color.FromArgb(35, 35, 35));

        const int step = 64;
        for (int y = step / 2; y < Height; y += step)
        {
            double lat = LatitudeAt(WorldZAtRow(y), TemperatureScale);
            string text = English ? $"Lat {lat:F0}°" : $"纬度 {lat:F0}°";
            var sz = g.MeasureString(text, f);

            g.FillRectangle(bg, 2, y - sz.Height / 2, sz.Width + 5, sz.Height);
            g.DrawString(text, f, tb, 4, y - sz.Height / 2);

            float rightX = Width - sz.Width - 8;
            g.FillRectangle(bg, rightX - 2, y - sz.Height / 2, sz.Width + 5, sz.Height);
            g.DrawString(text, f, tb, rightX, y - sz.Height / 2);
        }
    }

    private void DrawBubble(Graphics g)
    {
        var bub = Bubble;
        if (bub == null)
        {
            _bubbleRect = RectangleF.Empty;
            return;
        }
        using var f = new Font("Microsoft YaHei UI", 9f);
        float lineH = f.GetHeight(g) + 2;
        float w = 0, h = lineH * bub.Lines.Length + 10;
        foreach (var line in bub.Lines)
        {
            var sz = g.MeasureString(line, f);
            w = Math.Max(w, sz.Width);
        }
        w += 14;
        float x = bub.Anchor.X + 12, y = bub.Anchor.Y - 8;
        if (x + w > Width - 4) x = bub.Anchor.X - w - 12;
        if (y + h > Height - 4) y = Height - h - 4;
        if (x < 4) x = 4;
        if (y < 4) y = 4;
        _bubbleRect = new RectangleF(x, y, w, h);

        using var bg = new SolidBrush(Color.FromArgb(235, 245, 255));
        using var border = new Pen(Color.FromArgb(40, 80, 150), 1.5f);
        g.FillRectangle(bg, x, y, w, h);
        g.DrawRectangle(border, x, y, w, h);
        using var tb = new SolidBrush(Color.FromArgb(20, 30, 50));
        float ty = y + 5;
        foreach (var line in bub.Lines)
        {
            g.DrawString(line, f, tb, x + 7, ty);
            ty += lineH;
        }
    }

    /// <summary>点击是否落在气泡内部（用于"点气泡关闭气泡"）</summary>
    public bool HitTestBubble(Point p) => Bubble != null && _bubbleRect.Contains(p);

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
            ViewClick?.Invoke(e.Location);
    }
}

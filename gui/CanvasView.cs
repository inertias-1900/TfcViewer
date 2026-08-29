namespace TfcViewerGui;

/// <summary>点击信息气泡数据</summary>
public sealed class BubbleData
{
    public Point Anchor;        // 画布坐标（锚点）
    public string[] Lines = Array.Empty<string>();
}

/// <summary>主画布：渲染图显示 + 选中群系灰显/描边 + 信息气泡 + 拖动/查看交互</summary>
public sealed class CanvasView : UserControl
{
    public Bitmap? Source;
    private Bitmap? _display;
    private int[] _biomeIds = Array.Empty<int>();   // 渲染像素对应的 biomeId（w*h）
    private int _srcW, _srcH;

    public HashSet<int> Selected { get; private set; } = new();
    public bool DragMode;
    public bool Rendering;   // 渲染中：灰白覆盖层 + "正在重新计算"

    public BubbleData? Bubble;

    /// <summary>查看模式点击（画布坐标）</summary>
    public event Action<Point>? ViewClick;

    /// <summary>拖动交互（画布坐标、按钮状态）</summary>
    public event Action<MouseEventArgs>? CanvasMouseDown;
    public event Action<MouseEventArgs>? CanvasMouseMove;
    public event Action<MouseEventArgs>? CanvasMouseUp;

    public CanvasView()
    {
        BackColor = Color.FromArgb(12, 16, 30);
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    /// <summary>设置渲染结果：位图 + 每像素 biomeId（仅 biome 层有意义，其他层传 null 或不启用选中）</summary>
    public void SetSource(Bitmap bmp, int[] biomeIds, int w, int h)
    {
        Source?.Dispose();
        _display?.Dispose();
        Source = bmp;
        _biomeIds = biomeIds ?? Array.Empty<int>();
        _srcW = w;
        _srcH = h;
        RebuildDisplay();
        Invalidate();
    }

    public void SetSelected(HashSet<int> selected)
    {
        Selected = new HashSet<int>(selected);
        RebuildDisplay();
        Invalidate();
    }

    public void ClearBubble()
    {
        if (Bubble == null) return;
        Bubble = null;
        Invalidate();
    }

    /// <summary>后处理：无选中→原图；有选中→未选中灰度化、选中原色+8邻域描白边</summary>
    private void RebuildDisplay()
    {
        _display?.Dispose();
        _display = null;
        var src = Source;
        if (src == null || _biomeIds.Length != _srcW * _srcH || Selected.Count == 0)
        {
            var copy = new Bitmap(src?.Width ?? 1, src?.Height ?? 1);
            if (src != null)
            {
                using var g = Graphics.FromImage(copy);
                g.DrawImageUnscaled(src, 0, 0);
            }
            _display = copy;
            return;
        }

        var w = _srcW;
        var h = _srcH;
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
            for (int i = 0; i < _biomeIds.Length; i++)
                isSel[i] = Selected.Contains(_biomeIds[i]);

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
                        // 描边：8 邻域存在非选中
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
                            d[3] = 255;
                        }
                        else
                        {
                            d[2] = s[2];
                            d[1] = s[1];
                            d[0] = s[0];
                            d[3] = 255;
                        }
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
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var img = _display ?? Source;
        if (img != null)
        {
            g.DrawImage(img, 0, 0, Width, Height);
        }
        else
        {
            using var f = new Font("Microsoft YaHei UI", 12f);
            using var b = new SolidBrush(Color.White);
            g.DrawString("渲染中...", f, b, 10, 10);
        }
        if (DragMode)
        {
            // 拖动模式边框
            using var pen = new Pen(Color.FromArgb(255, 200, 60), 2f);
            g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
            // 左上角说明：白底 + 加粗黑字
            using var bg = new SolidBrush(Color.White);
            g.FillRectangle(bg, 8, 8, 250, 30);
            using var f = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
            using var tb = new SolidBrush(Color.Black);
            g.DrawString("拖动模式：按住左键拖动画面", f, tb, 14, 13);
        }
        DrawBubble(g);

        if (Rendering)
        {
            // 渲染中覆盖层：灰白半透明 + 居中提示
            using var cover = new SolidBrush(Color.FromArgb(190, 238, 238, 238));
            g.FillRectangle(cover, 0, 0, Width, Height);
            using var f = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold);
            using var tb = new SolidBrush(Color.FromArgb(70, 70, 70));
            var sz = g.MeasureString("正在重新计算", f);
            g.DrawString("正在重新计算", f, tb, (Width - sz.Width) / 2f, (Height - sz.Height) / 2f);
        }
    }

    private RectangleF _bubbleRect = RectangleF.Empty;

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

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (DragMode) CanvasMouseDown?.Invoke(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (DragMode && e.Button == MouseButtons.Left) CanvasMouseMove?.Invoke(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (DragMode) CanvasMouseUp?.Invoke(e);
        else if (e.Button == MouseButtons.Left) ViewClick?.Invoke(e.Location);
    }
}
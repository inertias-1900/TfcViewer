namespace TfcViewerGui;

/// <summary>
/// 右下角整体预览（小地图）：显示大范围图 + 白色边框标记当前视图范围。
/// 坐标换算严格按「每像素块数 Bpp」做（X/Y 同一个比例），左键点击/拖动即把该点设为主画面中心。
/// </summary>
public sealed class MinimapView : UserControl
{
    private Bitmap? _image;
    public Bitmap? Image
    {
        get => _image;
        set
        {
            if (ReferenceEquals(_image, value)) return;
            var old = _image;
            _image = value;
            old?.Dispose();
            Invalidate();
        }
    }

    /// <summary>主视图中心与小地图中心一致；ViewSpan = 主视图覆盖的方块数</summary>
    public double ViewCx, ViewCz, ViewSpan;
    /// <summary>小地图每像素代表的方块数（X、Y 相同）</summary>
    public double Bpp = 1;
    public bool English;

    public event Action<double, double>? Jump;

    public MinimapView()
    {
        Size = new Size(240, 160);
        BackColor = Color.FromArgb(20, 24, 40);
        Cursor = Cursors.Hand;
    }

    /// <summary>小地图覆盖的世界范围（块）</summary>
    private double WorldWidth => Width * Bpp;

    private double WorldHeight => Height * Bpp;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var img = _image;
        if (img != null)
        {
            g.DrawImage(img, new Rectangle(0, 0, Width, Height));
        }
        else
        {
            using var f = new Font("Microsoft YaHei UI", 9f);
            using var b = new SolidBrush(Color.White);
            g.DrawString(English ? "Rendering..." : "渲染中...", f, b, 8, 8);
        }

        if (Bpp > 0 && WorldWidth > 0)
        {
            double left = ViewCx - WorldWidth / 2.0;
            double top = ViewCz + WorldHeight / 2.0;        // 北（大 Z）在上
            double x0 = (ViewCx - ViewSpan / 2.0 - left) / Bpp;
            double y0 = (top - (ViewCz + ViewSpan / 2.0)) / Bpp;
            double w = ViewSpan / Bpp;
            double h = ViewSpan / Bpp;
            using var pen = new Pen(Color.White, 2f);
            g.DrawRectangle(pen, (float)x0, (float)y0, (float)w, (float)h);
        }

        using var border = new Pen(Color.Gray);
        g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) JumpTo(e.Location);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.Button == MouseButtons.Left) JumpTo(e.Location);
    }

    private void JumpTo(Point p)
    {
        if (Bpp <= 0) return;
        double cx = ViewCx - WorldWidth / 2.0 + p.X * Bpp;
        double cz = ViewCz + WorldHeight / 2.0 - p.Y * Bpp;
        Jump?.Invoke(cx, cz);
    }
}

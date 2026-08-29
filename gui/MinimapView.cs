namespace TfcViewerGui;

/// <summary>右下角整体预览（小地图）：显示大范围图 + 白色边框标记当前视图范围，点击/拖动跳转</summary>
public sealed class MinimapView : UserControl
{
    public Bitmap? Image;
    public double ViewCx, ViewCz, ViewSpan; // 主视图跨度（块）
    public double WorldSpan;                 // 小地图覆盖跨度（块）
    public bool English;                     // 英文界面（提示文字）

    public event Action<double, double>? Jump; // 跳转到目标中心（块坐标）

    public MinimapView()
    {
        Size = new Size(240, 160);
        BackColor = Color.FromArgb(20, 24, 40);
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var img = Image;
        if (img != null)
        {
            g.DrawImage(img, 0, 0, Width, Height);
        }
        else
        {
            using var f = new Font("Microsoft YaHei UI", 9f);
            using var b = new SolidBrush(Color.White);
            g.DrawString(English ? "Rendering..." : "渲染中...", f, b, 8, 8);
        }

        if (WorldSpan > 0)
        {
            double worldLeft = ViewCx - ViewSpan / 2.0;
            double worldTop = ViewCz + ViewSpan / 2.0;   // 北在上
            double x0 = (worldLeft - (ViewCx - WorldSpan / 2.0)) / WorldSpan * Width;
            double x1 = (worldLeft + ViewSpan - (ViewCx - WorldSpan / 2.0)) / WorldSpan * Width;
            double y0 = ((ViewCz + WorldSpan / 2.0) - worldTop) / WorldSpan * Height;
            double y1 = ((ViewCz + WorldSpan / 2.0) - worldTop + ViewSpan) / WorldSpan * Height;
            using var pen = new Pen(Color.White, 2f);
            g.DrawRectangle(pen, (float)x0, (float)y0, (float)(x1 - x0), (float)(y1 - y0));
        }

        using var border = new Pen(Color.Gray);
        g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        JumpTo(e.Location);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.Button == MouseButtons.Left) JumpTo(e.Location);
    }

    private void JumpTo(Point p)
    {
        if (WorldSpan <= 0) return;
        double cx = (ViewCx - WorldSpan / 2.0) + (double)p.X / Width * WorldSpan;
        double cz = (ViewCz + WorldSpan / 2.0) - (double)p.Y / Height * WorldSpan;
        Jump?.Invoke(cx, cz);
    }
}
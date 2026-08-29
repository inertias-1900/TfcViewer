namespace TfcViewerGui;

/// <summary>自绘开关按钮：点击切换，保持按下（凹陷）状态动画</summary>
public sealed class ToggleButton : Control
{
    public bool Checked;
    public event Action<bool>? CheckedChanged;

    public ToggleButton(string text)
    {
        Text = text;
        Size = new Size(120, 28);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        if (Checked)
        {
            // 按下/凹陷：深灰渐变 + 内阴影 + 文字下沉
            using var bg = new SolidBrush(Color.FromArgb(120, 130, 150));
            g.FillRectangle(bg, rect);
            using var topShadow = new Pen(Color.FromArgb(70, 78, 96));
            g.DrawLine(topShadow, rect.Left, rect.Top, rect.Right, rect.Top);
            g.DrawLine(topShadow, rect.Left, rect.Top, rect.Left, rect.Bottom);
            using var bottomLight = new Pen(Color.FromArgb(200, 208, 224));
            g.DrawLine(bottomLight, rect.Left, rect.Bottom, rect.Right, rect.Bottom);
            g.DrawLine(bottomLight, rect.Right, rect.Top, rect.Right, rect.Bottom);
        }
        else
        {
            // 抬起/凸起：浅灰渐变 + 亮顶边
            using var bg = new SolidBrush(Color.FromArgb(225, 228, 235));
            g.FillRectangle(bg, rect);
            using var topLight = new Pen(Color.White);
            g.DrawLine(topLight, rect.Left + 1, rect.Top + 1, rect.Right - 1, rect.Top + 1);
            using var bottomShadow = new Pen(Color.FromArgb(110, 118, 134));
            g.DrawLine(bottomShadow, rect.Left, rect.Bottom, rect.Right, rect.Bottom);
        }

        using var f = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        var sz = g.MeasureString(Text, f);
        float tx = (Width - sz.Width) / 2f;
        float ty = (Height - sz.Height) / 2f + (Checked ? 1f : 0f);
        using var tb = new SolidBrush(Checked ? Color.White : Color.FromArgb(30, 32, 40));
        g.DrawString(Text, f, tb, tx, ty);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Checked = !Checked;
        Invalidate();
        CheckedChanged?.Invoke(Checked);
    }
}
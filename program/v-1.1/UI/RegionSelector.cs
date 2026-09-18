namespace AutoTyper.UI;

public class RegionSelector : Form
{
    private Point startPoint;
    private Point currentPoint;
    private bool selecting = false;
    private Rectangle selectedRegion;

    public Rectangle SelectedRegion => selectedRegion;

    public RegionSelector()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.WindowState = FormWindowState.Maximized;
        this.BackColor = Color.Black;
        this.Opacity = 0.3;
        this.TopMost = true;
        this.Cursor = Cursors.Cross;
        this.DoubleBuffered = true;

        this.MouseDown += (s, e) => { startPoint = e.Location; currentPoint = e.Location; selecting = true; this.Invalidate(); };
        this.MouseMove += (s, e) => { if (selecting) { currentPoint = e.Location; this.Invalidate(); } };
        this.MouseUp += (s, e) =>
        {
            selecting = false;
            selectedRegion = NormalizeRect(startPoint, currentPoint);
            this.DialogResult = DialogResult.OK;
            this.Close();
        };
        this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { this.DialogResult = DialogResult.Cancel; this.Close(); } };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!selecting) return;
        var r = NormalizeRect(startPoint, currentPoint);
        using var pen = new Pen(Color.Red, 2);
        e.Graphics.DrawRectangle(pen, r);
        using var br = new SolidBrush(Color.FromArgb(60, Color.Red));
        e.Graphics.FillRectangle(br, r);
    }

    private static Rectangle NormalizeRect(Point a, Point b)
    {
        int x = Math.Min(a.X, b.X);
        int y = Math.Min(a.Y, b.Y);
        int w = Math.Abs(a.X - b.X);
        int h = Math.Abs(a.Y - b.Y);
        return new Rectangle(x, y, w, h);
    }
}

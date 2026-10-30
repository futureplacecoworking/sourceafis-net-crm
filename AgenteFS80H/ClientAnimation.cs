using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace Futureplace.AgenteFS80H.Desktop;

/// <summary>Local, decorative motion around the approved mark; never renders an actual biometric image.</summary>
internal sealed class ClientAnimation : Control
{
    private readonly Image mark = CentralTheme.ReaderImage();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 33 };
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private ClientMotion motion = ClientMotion.Waiting;
    private Color accent = CentralTheme.Primary;

    public ClientAnimation()
    {
        Name = "clientAnimation";
        AccessibleName = "Marca do leitor de digitais";
        AccessibleRole = AccessibleRole.Graphic;
        TabStop = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        timer.Tick += (_, _) => Invalidate();
    }

    public void ShowPresentation(ClientPresentation state)
    {
        if (motion != state.Motion || accent != state.Accent)
        {
            motion = state.Motion;
            accent = state.Accent;
            elapsed.Restart();
        }
        UpdatePlayback();
        Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdatePlayback();
    }

    private void UpdatePlayback() => timer.Enabled = Visible && !IsDisposed && motion is not (ClientMotion.Attention or ClientMotion.Unavailable);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var size = Math.Min(ClientSize.Width, ClientSize.Height);
        if (size < 24) return;
        var center = new PointF(ClientSize.Width / 2F, ClientSize.Height / 2F);
        var phase = (float)(elapsed.Elapsed.TotalSeconds / 2.4 % 1);
        bool animated = motion is not (ClientMotion.Attention or ClientMotion.Unavailable);
        for (var i = 0; i < 3; i++)
        {
            var wave = animated ? (phase + i / 3F) % 1 : i / 3F;
            var radius = size * (0.34F + wave * 0.14F);
            var alpha = animated ? (int)(42 * (1 - wave)) : 18;
            using var pen = new Pen(Color.FromArgb(alpha, accent), Math.Max(1F, size / 150F));
            g.DrawEllipse(pen, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        }
        var orbit = size * 0.37F;
        var orbitBounds = new RectangleF(center.X - orbit, center.Y - orbit, orbit * 2, orbit * 2);
        using (var trail = new Pen(Color.FromArgb(110, accent), Math.Max(2F, size / 90F)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            var angle = animated ? phase * 360 - 90 : -90;
            g.DrawArc(trail, orbitBounds, angle, motion == ClientMotion.Confirmed ? 285 : 95);
        }

        var markSize = size * 0.57F;
        g.DrawImage(mark, new RectangleF(center.X - markSize / 2, center.Y - markSize / 2, markSize, markSize));

        if (motion is ClientMotion.Confirmed or ClientMotion.Attention or ClientMotion.Unavailable)
        {
            var diameter = size * 0.23F;
            var badge = new RectangleF(center.X + markSize * 0.23F, center.Y + markSize * 0.18F, diameter, diameter);
            using var brush = new SolidBrush(accent);
            using var outline = new Pen(Color.White, Math.Max(3F, size / 65F));
            g.FillEllipse(brush, badge);
            g.DrawEllipse(outline, badge);
            using var ink = new Pen(Color.White, Math.Max(2F, size / 65F)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            if (motion == ClientMotion.Confirmed)
            {
                var progress = Math.Clamp((float)elapsed.Elapsed.TotalSeconds / 0.45F, 0, 1);
                var a = new PointF(badge.X + diameter * 0.24F, badge.Y + diameter * 0.52F);
                var b = new PointF(badge.X + diameter * 0.44F, badge.Y + diameter * 0.7F);
                var c = new PointF(badge.X + diameter * 0.77F, badge.Y + diameter * 0.32F);
                if (progress < 0.4F) g.DrawLine(ink, a, Between(a, b, progress / 0.4F));
                else { g.DrawLine(ink, a, b); g.DrawLine(ink, b, Between(b, c, (progress - 0.4F) / 0.6F)); }
            }
            else
            {
                g.DrawLine(ink, badge.X + diameter / 2, badge.Y + diameter * 0.26F, badge.X + diameter / 2, badge.Y + diameter * 0.56F);
                g.DrawLine(ink, badge.X + diameter / 2, badge.Y + diameter * 0.73F, badge.X + diameter / 2, badge.Y + diameter * 0.75F);
            }
        }
    }

    private static PointF Between(PointF a, PointF b, float amount) => new(a.X + (b.X - a.X) * amount, a.Y + (b.Y - a.Y) * amount);

    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); elapsed.Stop(); mark.Dispose(); }
        base.Dispose(disposing);
    }
}

using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Futureplace.AgenteFS80H.Desktop;

internal enum ButtonTone { Primary, Secondary, Danger }

/// <summary>Identity shared with the Central UI. Assets are local: rendering never needs a network request.</summary>
internal static class CentralTheme
{
    public static readonly Color Header = Color.FromArgb(0x00, 0x1a, 0x30);
    public static readonly Color Primary = Color.FromArgb(0x00, 0x3a, 0x6c);
    public static readonly Color Background = Color.FromArgb(0xf4, 0xf5, 0xf7);
    public static readonly Color Surface = Color.White;
    public static readonly Color Text = Color.FromArgb(0x17, 0x2b, 0x3a);
    public static readonly Color Muted = Color.FromArgb(0x64, 0x74, 0x8b);
    public static readonly Color Border = Color.FromArgb(0xd9, 0xde, 0xe7);
    public static readonly Color Success = Color.FromArgb(0x15, 0x80, 0x3d);
    public static readonly Color Warning = Color.FromArgb(0xb4, 0x53, 0x09);
    public static readonly Color Danger = Color.FromArgb(0xae, 0x00, 0x01);

    private const string ResourcePrefix = "Futureplace.AgenteFS80H.Desktop.Assets.";
    private static readonly PrivateFontCollection Fonts = new();
    // GDI+ and the Win32 controls both retain these font buffers. They live with the process.
    private static readonly List<IntPtr> FontBuffers = [];
    private static readonly List<IntPtr> GdiFonts = [];
    private static readonly FontFamily? DisplayFamily;
    private static readonly FontFamily? BodyFamily;

    static CentralTheme()
    {
        LoadFont("Nunito.ttf");
        LoadFont("Nunito-Regular.ttf");
        LoadFont("Raleway.ttf");
        LoadFont("Raleway-Bold.ttf");
        DisplayFamily = Fonts.Families.FirstOrDefault(f => f.Name == "Nunito");
        BodyFamily = Fonts.Families.FirstOrDefault(f => f.Name == "Central Text");
    }

    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr AddFontMemResourceEx(IntPtr font, uint length, IntPtr reserved, ref uint count);

    public static Font Heading(float size, FontStyle style = FontStyle.Bold) => CreateFont(DisplayFamily, size, style);
    public static Font Body(float size, FontStyle style = FontStyle.Regular) => CreateFont(BodyFamily, size, style);

    private static Font CreateFont(FontFamily? family, float size, FontStyle style)
    {
        if (family is not null)
        {
            // Unsupported optional styles fall back to the system font, never blocking startup.
            try { if (family.IsStyleAvailable(style)) return new Font(family, size, style, GraphicsUnit.Point); }
            catch (ArgumentException) { }
        }
        return new Font("Segoe UI", size, style, GraphicsUnit.Point);
    }

    private static void LoadFont(string file)
    {
        IntPtr buffer = IntPtr.Zero;
        bool retained = false;
        try
        {
            using var resource = Resource(file);
            if (resource is null || resource.Length is <= 0 or > 4 * 1024 * 1024)
                return;
            var bytes = new byte[checked((int)resource.Length)];
            resource.ReadExactly(bytes);
            buffer = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            Fonts.AddMemoryFont(buffer, bytes.Length);
            FontBuffers.Add(buffer);
            retained = true;

            // PrivateFontCollection is GDI+ only. Registering the same memory privately also serves TextBox/GDI.
            uint count = 0;
            var handle = AddFontMemResourceEx(buffer, (uint)bytes.Length, IntPtr.Zero, ref count);
            if (handle != IntPtr.Zero)
                GdiFonts.Add(handle);
        }
        catch (Exception e) when (e is ArgumentException or ExternalException or IOException or InvalidOperationException)
        {
            // Segoe UI remains available if a font resource is absent or cannot be registered.
        }
        finally
        {
            if (!retained && buffer != IntPtr.Zero)
                Marshal.FreeCoTaskMem(buffer);
        }
    }

    public static Image Logo()
    {
        try
        {
            using var stream = Resource("Central.png");
            if (stream is not null)
            {
                using var image = Image.FromStream(stream);
                return new Bitmap(image);
            }
        }
        catch (Exception e) when (e is ArgumentException or ExternalException or IOException) { }

        // The official F is never redrawn. A text-only fallback keeps a missing optional asset harmless.
        var fallback = new Bitmap(240, 60);
        using var graphics = Graphics.FromImage(fallback);
        using var font = new Font("Segoe UI", 23F, FontStyle.Bold);
        using var brush = new SolidBrush(Color.White);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.DrawString("CENTRAL", font, brush, new PointF(0, 8));
        return fallback;
    }

    public static Icon AppIcon()
    {
        try
        {
            using var stream = Resource("Central.ico");
            if (stream is not null)
            {
                using var icon = new Icon(stream);
                return (Icon)icon.Clone();
            }
        }
        catch (Exception e) when (e is ArgumentException or ExternalException or IOException) { }
        return (Icon)SystemIcons.Application.Clone();
    }

    private static Stream? Resource(string name) => Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + name);

    internal static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0)
            return path;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static Color ParentBackground(Control control)
    {
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
            if (parent.BackColor.A == 255)
                return parent.BackColor;
        return Background;
    }

    internal static Color Mix(Color first, Color second, float amount) => Color.FromArgb(
        (int)(first.R + (second.R - first.R) * amount),
        (int)(first.G + (second.G - first.G) * amount),
        (int)(first.B + (second.B - first.B) * amount));
}

/// <summary>A normal accessible WinForms button, painted with the Central's rounded visual language.</summary>
internal sealed class CentralButton : Button
{
    private readonly ButtonTone tone;
    private bool hovered;
    private bool pressed;
    private bool selected;
    private string? measuredCaption;
    private Font? measuredFont;
    private int measuredDpi;
    private SizeF captionSize;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Navigation { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected { get => selected; set { selected = value; Invalidate(); } }

    public CentralButton(string text, ButtonTone tone = ButtonTone.Secondary)
    {
        this.tone = tone;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor, true);
        Text = text;
        AccessibleName = text;
        AccessibleRole = AccessibleRole.PushButton;
        Font = CentralTheme.Heading(10.5F);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        UseCompatibleTextRendering = true;
        BackColor = Color.Transparent;
        ForeColor = tone == ButtonTone.Primary ? Color.White : CentralTheme.Text;
        Cursor = Cursors.Hand;
        Padding = new Padding(16, 9, 16, 9);
        Margin = new Padding(0, 0, 8, 8);
        MinimumSize = new Size(44, 44);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        // Match the GDI+ painter. GDI/TextRenderer underestimates these embedded fonts and clips captions.
        if (measuredCaption != Text || !ReferenceEquals(measuredFont, Font) || measuredDpi != DeviceDpi)
        {
            using var bitmap = new Bitmap(1, 1);
            bitmap.SetResolution(DeviceDpi, DeviceDpi);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using var format = new StringFormat { FormatFlags = StringFormatFlags.NoWrap };
            captionSize = graphics.MeasureString(Text, Font, 10000, format);
            measuredCaption = Text;
            measuredFont = Font;
            measuredDpi = DeviceDpi;
        }
        var imageWidth = Image is null ? 0 : Image.Width + 8;
        var imageHeight = Image?.Height ?? 0;
        var dpi = DeviceDpi / 96F;
        return new Size(Math.Max((int)(44 * dpi), (int)Math.Ceiling(captionSize.Width) + imageWidth + Padding.Horizontal + (int)(8 * dpi)),
            Math.Max((int)(44 * dpi), Math.Max((int)Math.Ceiling(captionSize.Height), imageHeight) + Padding.Vertical));
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(CentralTheme.ParentBackground(this));

    protected override void OnPaint(PaintEventArgs e)
    {
        if (ClientSize.Width < 2 || ClientSize.Height < 2)
            return;
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        float scale = DeviceDpi / 96F;
        var bounds = new RectangleF(.75F, .75F, ClientSize.Width - 1.5F, ClientSize.Height - 1.5F);
        var fill = CentralTheme.Surface;
        var border = CentralTheme.Border;
        var text = CentralTheme.Text;

        if (!Enabled)
        {
            fill = Color.FromArgb(235, 238, 242);
            border = CentralTheme.Border;
            text = CentralTheme.Muted;
        }
        else if (Navigation)
        {
            fill = Selected ? CentralTheme.Surface : hovered ? Color.FromArgb(235, 239, 244) : CentralTheme.ParentBackground(this);
            border = fill;
            text = Selected ? CentralTheme.Primary : CentralTheme.Muted;
        }
        else if (tone == ButtonTone.Primary)
        {
            fill = pressed ? CentralTheme.Mix(CentralTheme.Primary, Color.Black, .2F)
                : hovered ? CentralTheme.Mix(CentralTheme.Primary, Color.Black, .1F) : CentralTheme.Primary;
            border = fill;
            text = Color.White;
        }
        else if (tone == ButtonTone.Danger)
        {
            fill = pressed ? CentralTheme.Mix(CentralTheme.Surface, CentralTheme.Danger, .14F)
                : hovered ? CentralTheme.Mix(CentralTheme.Surface, CentralTheme.Danger, .07F) : CentralTheme.Surface;
            border = CentralTheme.Danger;
            text = CentralTheme.Danger;
        }
        else if (pressed || hovered)
        {
            fill = CentralTheme.Mix(CentralTheme.Surface, CentralTheme.Primary, pressed ? .12F : .06F);
            border = CentralTheme.Primary;
            text = CentralTheme.Primary;
        }

        using (var path = CentralTheme.Rounded(bounds, (Navigation ? 12 : 6) * scale))
        using (var brush = new SolidBrush(fill))
        using (var pen = new Pen(border, Math.Max(1F, scale)))
        {
            graphics.FillPath(brush, path);
            graphics.DrawPath(pen, path);
        }

        var content = RectangleF.FromLTRB(Padding.Left, Padding.Top, Width - Padding.Right, Height - Padding.Bottom);
        if (Image is not null)
        {
            var textWidth = graphics.MeasureString(Text, Font).Width;
            var start = content.Left + Math.Max(0, (content.Width - Image.Width - 8 * scale - textWidth) / 2);
            graphics.DrawImage(Image, start, (Height - Image.Height) / 2F, Image.Width, Image.Height);
            content = RectangleF.FromLTRB(start + Image.Width + 8 * scale, content.Top, content.Right, content.Bottom);
        }
        using (var brush = new SolidBrush(text))
        using (var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
            HotkeyPrefix = ShowKeyboardCues ? HotkeyPrefix.Show : HotkeyPrefix.Hide
        })
        {
            if (RightToLeft == RightToLeft.Yes)
                format.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
            graphics.DrawString(Text, Font, brush, content, format);
        }
        if (Enabled && Focused && ShowFocusCues)
        {
            var inset = 3.5F * scale;
            using var focus = CentralTheme.Rounded(new RectangleF(inset, inset, Width - inset * 2, Height - inset * 2), 3 * scale);
            using var pen = new Pen(tone == ButtonTone.Primary ? Color.White : Navigation ? CentralTheme.Primary : border, Math.Max(1F, scale)) { DashStyle = DashStyle.Dot };
            graphics.DrawPath(pen, focus);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; base.OnMouseLeave(e); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; base.OnMouseDown(e); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; base.OnMouseUp(e); Invalidate(); }
    protected override void OnMouseCaptureChanged(EventArgs e) { pressed = false; base.OnMouseCaptureChanged(e); Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Space or Keys.Enter) pressed = true; base.OnKeyDown(e); Invalidate(); }
    protected override void OnKeyUp(KeyEventArgs e) { pressed = false; base.OnKeyUp(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { pressed = false; base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
}

internal sealed class CentralCard : Panel
{
    public CentralCard()
    {
        BackColor = CentralTheme.Surface;
        Padding = new Padding(24);
        Margin = new Padding(0, 0, 0, 16);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SuspendLayout();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(CentralTheme.ParentBackground(this));
        var scale = DeviceDpi / 96F;
        if (Width < 4 || Height <= 6 * scale)
            return;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shadow = CentralTheme.Rounded(new RectangleF(1, 3 * scale, Width - 2, Height - 3 * scale - 1), 12 * scale);
        using var shadowBrush = new SolidBrush(Color.FromArgb(15, CentralTheme.Header));
        graphics.FillPath(shadowBrush, shadow);
        using var card = CentralTheme.Rounded(new RectangleF(1, 0, Width - 2, Height - 5 * scale), 12 * scale);
        using var surface = new SolidBrush(BackColor);
        graphics.FillPath(surface, card);
    }
}

/// <summary>Decorative generic fingerprint; never displays or stores an actual captured fingerprint.</summary>
internal sealed class FingerprintGlyph : Control
{
    private Color accent = CentralTheme.Primary;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Accent { get => accent; set { if (accent != value) { accent = value; Invalidate(); } } }

    public FingerprintGlyph()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(100, 100);
        BackColor = Color.Transparent;
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "Leitor de digital";
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2)
            return;
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = Math.Min(Width, Height) / 100F;
        var state = graphics.Save();
        try
        {
            graphics.TranslateTransform((Width - scale * 100) / 2, (Height - scale * 100) / 2);
            graphics.ScaleTransform(scale, scale);
            using var background = new SolidBrush(Color.FromArgb(18, accent));
            graphics.FillEllipse(background, 1, 1, 98, 98);
            using var pen = new Pen(accent, 2.7F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

            using var outerLeft = new GraphicsPath();
            outerLeft.AddBezier(19, 52, 16, 34, 30, 20, 48, 19);
            outerLeft.AddBezier(48, 19, 63, 18, 77, 27, 81, 39);
            graphics.DrawPath(pen, outerLeft);

            using var outerRight = new GraphicsPath();
            outerRight.AddBezier(82, 49, 82, 62, 79, 74, 74, 82);
            graphics.DrawPath(pen, outerRight);

            using var ridge = new GraphicsPath();
            ridge.AddBezier(22, 68, 28, 59, 27, 52, 27, 46);
            ridge.AddBezier(27, 46, 27, 33, 38, 27, 50, 27);
            ridge.AddBezier(50, 27, 63, 27, 73, 36, 73, 48);
            ridge.AddBezier(73, 48, 74, 62, 70, 75, 65, 84);
            graphics.DrawPath(pen, ridge);

            using var middle = new GraphicsPath();
            middle.AddBezier(28, 80, 37, 67, 36, 57, 36, 47);
            middle.AddBezier(36, 47, 36, 39, 42, 35, 50, 35);
            middle.AddBezier(50, 35, 59, 35, 65, 41, 65, 49);
            middle.AddBezier(65, 49, 65, 65, 62, 75, 55, 85);
            graphics.DrawPath(pen, middle);

            using var core = new GraphicsPath();
            core.AddBezier(38, 85, 44, 75, 45, 62, 45, 49);
            core.AddBezier(45, 49, 45, 42, 56, 42, 56, 49);
            core.AddBezier(56, 49, 56, 64, 53, 75, 48, 82);
            graphics.DrawPath(pen, core);
        }
        finally { graphics.Restore(state); }
    }
}

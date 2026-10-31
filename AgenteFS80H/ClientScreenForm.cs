namespace Futureplace.AgenteFS80H.Desktop;

/// <summary>Por que a tela nova (WebView2) não abriu e a janela ficou na tela simples.</summary>
internal enum ClientWebFailure { RuntimeMissing, Failed }

/// <summary>
/// Tela do cliente no monitor em pé. Mostra a página da opção A pelo WebView2; se ele faltar ou falhar,
/// continua com a tela simples desenhada em WinForms, que nunca depende de componente externo.
/// </summary>
internal sealed class ClientScreenForm : Form
{
    private readonly Image logo = CentralTheme.Logo();
    private readonly TableLayoutPanel nativeShell;
    private IClientWebHost? web;
    private AgentSnapshot? lastSnapshot;
    private string? lastJson;
    private readonly ClientAnimation animation = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Label caption = Label("clientCaption", 10, true);
    private readonly Label title = Label("clientTitle", 32, true);
    private readonly Label welcome = Label("clientWelcome", 18, true);
    private readonly Label instruction = Label("clientInstruction", 15);
    private readonly TableLayoutPanel content;
    private Rectangle windowBounds;
    private float titleSize;

    public ClientScreenForm(Func<IClientWebHost>? webHostFactory = null)
    {
        SuspendLayout();
        Name = "clientScreen";
        Text = "Central Biométrica — Cliente";
        Icon = CentralTheme.AppIcon();
        BackColor = CentralTheme.Background;
        Font = CentralTheme.Body(11);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(960, 720);
        MinimumSize = new Size(720, 540);
        StartPosition = FormStartPosition.Manual;

        var shell = nativeShell = new TableLayoutPanel { Name = "clientNativeScreen", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        shell.SuspendLayout();
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = CentralTheme.Header, ColumnCount = 2, Padding = new Padding(32, 16, 32, 16), Margin = Padding.Empty };
        header.SuspendLayout();
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        header.Controls.Add(new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(196, 52), Anchor = AnchorStyles.Left, AccessibleName = "Central — Future Place Coworking" }, 0, 0);
        var brand = Label("", 12, true);
        brand.Text = "FUTURE PLACE COWORKING";
        brand.ForeColor = Color.White;
        brand.TextAlign = ContentAlignment.MiddleRight;
        brand.AutoSize = false;
        brand.Dock = DockStyle.Fill;
        header.Controls.Add(brand, 1, 0);
        shell.Controls.Add(header, 0, 0);

        var card = new CentralCard { Dock = DockStyle.Fill, Margin = new Padding(28, 24, 28, 0), Padding = new Padding(24, 8, 24, 8) };
        content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Margin = Padding.Empty };
        content.SuspendLayout();
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 166));
        for (var i = 0; i < 4; i++) content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        content.Controls.Add(animation, 0, 1);
        content.Controls.Add(caption, 0, 2);
        content.Controls.Add(title, 0, 3);
        content.Controls.Add(welcome, 0, 4);
        content.Controls.Add(instruction, 0, 5);
        title.AccessibleRole = AccessibleRole.Alert;
        title.Margin = new Padding(0, 10, 0, 12);
        welcome.Margin = new Padding(0, 0, 0, 14);
        instruction.Margin = new Padding(0, 0, 0, 12);
        instruction.ForeColor = CentralTheme.Muted;
        card.Controls.Add(content);
        shell.Controls.Add(card, 0, 1);
        var help = Label("", 11);
        help.Text = "Precisa de ajuda? Fale com a recepcionista.";
        help.AutoSize = false;
        help.Dock = DockStyle.Fill;
        help.ForeColor = CentralTheme.Muted;
        help.Padding = new Padding(20, 8, 20, 8);
        shell.Controls.Add(help, 0, 2);
        Controls.Add(shell);
        SizeChanged += (_, _) => ResizeContent();
        content.SizeChanged += (_, _) => ResizeContent();
        ResumeTree(shell);
        if (webHostFactory is not null) AttachWeb(webHostFactory);
        ResumeLayout(true);
        ResizeContent();
    }

    /// <summary>A tela nova não abriu; a janela segue na tela simples. A recepção é avisada de como resolver.</summary>
    public event Action<ClientWebFailure>? WebUnavailable;

    /// <summary>True enquanto a página nova (WebView2) é a que está na tela.</summary>
    public bool UsingWebPage => web is not null;

    private void AttachWeb(Func<IClientWebHost> factory)
    {
        try { web = factory(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            web = null;
            Load += (_, _) => WebUnavailable?.Invoke(ClientWebFailure.Failed);
            return;
        }
        BackColor = Color.Black;
        web.View.Dock = DockStyle.Fill;
        web.Failed += () => { if (!IsDisposed) BeginInvoke(() => FallBack(ClientWebFailure.Failed)); };
        Controls.Add(web.View);
        web.View.BringToFront();
        nativeShell.Visible = false;
        Load += async (_, _) => await StartWebAsync();
    }

    private async Task StartWebAsync()
    {
        var host = web;
        if (host is null) return;
        try
        {
            await host.InitializeAsync();
            if (IsDisposed || !ReferenceEquals(host, web)) return;
            if (lastSnapshot is not null) ShowWeb(lastSnapshot);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!IsDisposed && ReferenceEquals(host, web))
                FallBack(ex is ClientWebUnavailableException { RuntimeMissing: true } ? ClientWebFailure.RuntimeMissing : ClientWebFailure.Failed);
        }
    }

    private void FallBack(ClientWebFailure reason)
    {
        var host = web;
        if (host is null || IsDisposed) return;
        web = null;
        lastJson = null;
        Controls.Remove(host.View);
        host.Dispose();
        BackColor = CentralTheme.Background;
        nativeShell.Visible = true;
        if (lastSnapshot is not null) ShowNative(lastSnapshot);
        WebUnavailable?.Invoke(reason);
    }

    public void ShowSnapshot(AgentSnapshot snapshot)
    {
        if (IsDisposed) return;
        lastSnapshot = snapshot;
        if (web is not null) ShowWeb(snapshot);
        ShowNative(snapshot);
    }

    private void ShowWeb(AgentSnapshot snapshot)
    {
        var json = ClientPage.Serialize(ClientPage.From(snapshot, DateTimeOffset.UtcNow));
        if (json == lastJson) return;
        lastJson = json;
        web!.Show(json);
    }

    private void ShowNative(AgentSnapshot snapshot)
    {
        var state = ClientPresentation.From(snapshot, DateTimeOffset.UtcNow);
        content.SuspendLayout();
        caption.Text = state.Caption;
        title.Text = state.Title;
        welcome.Text = state.Welcome ?? "";
        welcome.Visible = state.Welcome is not null;
        instruction.Text = state.Instruction;
        caption.ForeColor = title.ForeColor = welcome.ForeColor = state.Accent;
        animation.ShowPresentation(state);
        content.ResumeLayout(true);
    }

    public void Present(Screen destination)
    {
        if (!Visible)
        {
            var area = destination.WorkingArea;
            var size = new Size(Math.Min(960, area.Width), Math.Min(720, area.Height));
            windowBounds = new Rectangle(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2, size.Width, size.Height);
            Bounds = windowBounds;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = destination.Bounds;
            WindowState = FormWindowState.Maximized;
            Show();
        }
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormBorderStyle == FormBorderStyle.None ? FormWindowState.Maximized : FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        if (keyData == Keys.F11)
        {
            if (FormBorderStyle == FormBorderStyle.None)
            {
                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.Sizable;
                Bounds = windowBounds;
            }
            else
            {
                windowBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                var screen = Screen.FromControl(this);
                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.None;
                Bounds = screen.Bounds;
                WindowState = FormWindowState.Maximized;
            }
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ResizeContent()
    {
        var scale = DeviceDpi / 96F;
        var height = ClientSize.Height / scale;
        content.RowStyles[1].Height = height < 600 ? 110 * scale : height < 800 ? 166 * scale : 224 * scale;
        var width = Math.Max(200, content.ClientSize.Width - (int)(12 * scale));
        foreach (var label in new[] { caption, title, welcome, instruction }) label.MaximumSize = new Size(width, 0);
        var desiredSize = height < 600 ? 25F : height < 800 ? 32F : 40F;
        if (titleSize != desiredSize)
        {
            var next = CentralTheme.Heading(desiredSize);
            // Control.Font keeps its existing instance when an equal font is assigned.
            // Disposing that still-selected instance would break GDI+ text measurement.
            if (title.Font.Equals(next)) next.Dispose();
            else
            {
                var old = title.Font;
                title.Font = next;
                old.Dispose();
            }
            titleSize = desiredSize;
        }
    }

    private static Label Label(string name, float size, bool heading = false) => new()
    {
        Name = name, AutoSize = true, UseCompatibleTextRendering = true, Dock = DockStyle.Fill,
        Font = heading ? CentralTheme.Heading(size) : CentralTheme.Body(size), TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = CentralTheme.Text, BackColor = Color.Transparent, Margin = new Padding(0, 4, 0, 4)
    };

    private static void ResumeTree(Control root)
    {
        foreach (Control child in root.Controls) ResumeTree(child);
        root.ResumeLayout(true);
    }

    protected override void Dispose(bool disposing)
    {
        var icon = disposing ? Icon : null;
        var host = disposing ? web : null;
        web = null;
        base.Dispose(disposing);
        if (disposing) { host?.Dispose(); logo.Dispose(); icon?.Dispose(); }
    }
}

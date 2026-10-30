using System.ComponentModel;
using System.Diagnostics;

namespace Futureplace.AgenteFS80H.Desktop;

internal sealed partial class MainForm : Form
{
    private readonly AgentRuntime runtime;
    private readonly CancellationToken lifetime;
    private readonly TextBox centralUrl = new() { Name = "centralUrl" };
    private readonly TextBox pairingCode = new() { Name = "pairingCode" };
    private readonly TextBox scannerPath = new() { Name = "scannerPath" };
    private readonly Label pairingState = TextLabel("");
    private readonly Label libraryState = TextLabel("");
    private readonly Label statusTitle = TextLabel("", 27, true);
    private readonly Label statusMessage = TextLabel("", 14);
    private readonly Label statusCaption = TextLabel("LEITOR DA RECEPÇÃO", 10, true);
    private readonly Label actionMessage = TextLabel("");
    private readonly CentralButton pairButton = new("Parear este computador", ButtonTone.Primary) { Name = "pairButton" };
    private readonly CentralButton clearButton = new("Limpar pareamento e digitais", ButtonTone.Danger) { Name = "clearButton" };
    private readonly CentralButton chooseScannerButton = new("Selecionar DLL de 64 bits…") { Name = "selectDllButton" };
    private readonly CentralButton downloadsButton = new("Site da Futronic");
    private readonly CentralButton navigationReading = new("Leitura") { Name = "navigationReading", Navigation = true };
    private readonly CentralButton navigationSettings = new("Configurações") { Name = "navigationSettings", Navigation = true };
    private readonly CentralButton openCentralButton = new("Abrir Central", ButtonTone.Primary) { Name = "openCentralButton" };
    private readonly CentralButton setupReader = new("Configurar leitor", ButtonTone.Primary) { Name = "setupReader" };
    private readonly CheckBox startup = new() { Name = "startup" };
    private readonly Panel readingPage = Page("readingPage");
    private readonly Panel settingsPage = Page("settingsPage");
    private readonly CentralCard setupGuide = new() { Name = "setupGuide", AutoSize = true, Dock = DockStyle.Top };
    private readonly FingerprintGlyph fingerprint = new() { Size = new Size(144, 144), Margin = new Padding(0, 16, 28, 16) };
    private readonly List<Label> wrappingLabels = [];
    private readonly Image logo = CentralTheme.Logo();
    private string? displayedCentralUrl;
    private bool busy;
    private bool changingStartup;
    private bool startupAvailable = true;
    private bool exiting;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AllowClose { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Task PendingOperation { get; private set; } = Task.CompletedTask;

    public MainForm(AgentRuntime runtime, CancellationToken lifetime)
    {
        this.runtime = runtime;
        this.lifetime = lifetime;
        SuspendLayout();
        Text = "Leitor da recepção — Central";
        Icon = CentralTheme.AppIcon();
        Font = CentralTheme.Body(10.5F);
        ForeColor = CentralTheme.Text;
        BackColor = CentralTheme.Background;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        MinimumSize = new Size(860, 680);
        ClientSize = new Size(1040, 760);

        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty, Padding = Padding.Empty };
        shell.SuspendLayout();
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        shell.Controls.Add(BuildHeader(), 0, 0);
        shell.Controls.Add(BuildNavigation(), 0, 1);
        actionMessage.Name = "actionMessage";
        actionMessage.AccessibleRole = AccessibleRole.Alert;
        actionMessage.ForeColor = CentralTheme.Danger;
        actionMessage.Margin = new Padding(32, 0, 32, 12);
        actionMessage.Visible = false;
        actionMessage.TextChanged += (_, _) => actionMessage.Visible = !string.IsNullOrEmpty(actionMessage.Text);
        shell.Controls.Add(actionMessage, 0, 2);
        var pages = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(28, 0, 28, 0) };
        pages.SuspendLayout();
        pages.Controls.Add(settingsPage);
        pages.Controls.Add(readingPage);
        shell.Controls.Add(pages, 0, 3);
        var footer = TextLabel("Fechar a janela mantém o leitor funcionando na bandeja do Windows. Esta etapa não aciona fechadura.", 10);
        footer.Dock = DockStyle.Fill;
        footer.AutoSize = false;
        footer.TextAlign = ContentAlignment.MiddleLeft;
        footer.Padding = new Padding(32, 8, 32, 8);
        footer.ForeColor = CentralTheme.Muted;
        shell.Controls.Add(footer, 0, 4);
        Controls.Add(shell);

        BuildReadingPage();
        BuildSettingsPage();
        navigationReading.Click += (_, _) => ShowPage(false);
        navigationSettings.Click += (_, _) => ShowPage(true);
        setupReader.Click += (_, _) => ShowPage(true);
        openCentralButton.Click += (_, _) => OpenCentral();
        FormClosing += (_, e) =>
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
        Shown += (_, _) => ReloadConfiguration();
        readingPage.SizeChanged += (_, _) => ResizeContent();
        settingsPage.SizeChanged += (_, _) => ResizeContent();
        ResumeTree(shell);
        ResumeLayout(true);
        ReloadConfiguration();
        ShowPage(false);
        ShowSnapshot(runtime.Snapshot);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = CentralTheme.Header, Padding = new Padding(32, 16, 32, 16), Margin = Padding.Empty, ColumnCount = 2 };
        header.SuspendLayout();
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        header.Controls.Add(new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(208, 52), Anchor = AnchorStyles.Left, AccessibleName = "Central de Operações — Future Place" }, 0, 0);
        var title = TextLabel("LEITOR DA RECEPÇÃO  /  FS80H", 10, true);
        title.ForeColor = Color.White;
        title.AutoSize = false;
        title.Dock = DockStyle.Fill;
        title.TextAlign = ContentAlignment.MiddleRight;
        header.Controls.Add(title, 1, 0);
        return header;
    }

    private Control BuildNavigation()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(32, 16, 32, 12), Margin = Padding.Empty, ColumnCount = 2 };
        row.SuspendLayout();
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        var tabs = Buttons(navigationReading, navigationSettings);
        tabs.Dock = DockStyle.Fill;
        row.Controls.Add(tabs, 0, 0);
        openCentralButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        openCentralButton.Margin = Padding.Empty;
        row.Controls.Add(openCentralButton, 1, 0);
        return row;
    }

    private void BuildReadingPage()
    {
        var stack = Stack();
        var card = new CentralCard { Dock = DockStyle.Fill, AutoSize = true, MinimumSize = new Size(0, 290), Padding = new Padding(28, 30, 28, 26) };
        var hero = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        hero.SuspendLayout();
        hero.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176));
        hero.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        hero.Controls.Add(fingerprint, 0, 0);
        var instructions = Stack();
        statusTitle.Name = "readingTitle";
        statusTitle.Margin = new Padding(0, 8, 0, 12);
        statusMessage.Name = "readingInstruction";
        statusMessage.AccessibleRole = AccessibleRole.Alert;
        statusMessage.Margin = new Padding(0, 0, 0, 16);
        Add(instructions, statusCaption);
        Add(instructions, statusTitle);
        Add(instructions, statusMessage);
        var automatic = Wrap("A leitura começa automaticamente no aparelho FS80H conectado por USB. Acompanhe as orientações acima.");
        automatic.ForeColor = CentralTheme.Muted;
        Add(instructions, automatic);
        hero.Controls.Add(instructions, 1, 0);
        card.Controls.Add(hero);
        Add(stack, card);

        var setup = Stack();
        Add(setup, Wrap("Vamos preparar este computador", 15, true));
        Add(setup, Wrap("Conecte à Central e selecione o arquivo do leitor em Configurações. O código de pareamento é gerado em Automação e sensores → Leitor da recepção."));
        Add(setup, Buttons(setupReader));
        setupGuide.Controls.Add(setup);
        Add(stack, setupGuide);

        var help = new CentralCard { Dock = DockStyle.Fill, AutoSize = true };
        var guide = Stack();
        Add(guide, Wrap("Como fazer a leitura", 15, true));
        var steps = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 3, Margin = new Padding(0, 12, 0, 0) };
        steps.SuspendLayout();
        for (var i = 0; i < 3; i++) steps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
        var titles = new[] { "1. Coloque o dedo", "2. Retire quando pedir", "3. Repita para confirmar" };
        var copy = new[] { "Use o dedo já cadastrado no aparelho físico.", "Aguarde a orientação e retire completamente.", "Apresente o mesmo dedo e aguarde a decisão." };
        for (var i = 0; i < 3; i++)
        {
            var step = Stack();
            step.Margin = new Padding(0, 0, i < 2 ? 20 : 0, 0);
            var title = TextLabel(titles[i], 11, true);
            title.MaximumSize = new Size(245, 0);
            var text = TextLabel(copy[i], 10);
            text.ForeColor = CentralTheme.Muted;
            text.MaximumSize = new Size(245, 0);
            Add(step, title);
            Add(step, text);
            steps.Controls.Add(step, i, 0);
        }
        Add(guide, steps);
        help.Controls.Add(guide);
        Add(stack, help);
        var central = Wrap("Cadastre pessoas e configure os dias e horários na Central: Automação e sensores → Leitor da recepção.");
        central.ForeColor = CentralTheme.Muted;
        Add(stack, central);
        readingPage.Controls.Add(stack);
    }

    private void BuildSettingsPage()
    {
        var stack = Stack();
        Add(stack, Wrap("Configurações do leitor", 21, true));
        var intro = Wrap("Prepare este computador uma vez. Depois, acompanhe a leitura na aba Leitura.");
        intro.ForeColor = CentralTheme.Muted;
        intro.Margin = new Padding(0, 0, 0, 20);
        Add(stack, intro);

        var connection = Stack();
        Add(connection, Wrap("1. Conectar à Central", 16, true));
        Add(connection, pairingState);
        Add(connection, Wrap("Endereço da Central", 10, true));
        ConfigureTextBox(centralUrl, "Endereço da Central");
        centralUrl.PlaceholderText = "https://central.futureplacecoworking.com.br";
        Add(connection, centralUrl);
        Add(connection, Wrap("Código de pareamento", 10, true));
        ConfigureTextBox(pairingCode, "Código de pareamento");
        pairingCode.MaxLength = 128;
        pairingCode.UseSystemPasswordChar = true;
        Add(connection, pairingCode);
        var pairingHint = Wrap("Gere o código em Automação e sensores → Leitor da recepção → Parear leitor. Cole o código aqui para conectar este PC.");
        pairingHint.ForeColor = CentralTheme.Muted;
        Add(connection, pairingHint);
        pairButton.Click += async (_, _) => await PairAsync();
        Add(connection, Buttons(pairButton));
        Add(stack, Card(connection));

        var scanner = Stack();
        Add(scanner, Wrap("2. Preparar o FS80H", 16, true));
        Add(scanner, libraryState);
        Add(scanner, Wrap("Selecione a ftrScanAPI.dll oficial de 64 bits do fabricante. O driver USB Futronic precisa estar instalado."));
        ConfigureTextBox(scannerPath, "Caminho da DLL oficial de captura");
        scannerPath.ReadOnly = true;
        scannerPath.PlaceholderText = "Arquivo de captura ainda não selecionado";
        Add(scanner, scannerPath);
        chooseScannerButton.Click += async (_, _) => await ChooseScannerAsync();
        downloadsButton.Click += (_, _) => OpenDownloads();
        Add(scanner, Buttons(chooseScannerButton, downloadsButton));
        Add(stack, Card(scanner));

        var preferences = Stack();
        Add(preferences, Wrap("Neste computador", 16, true));
        startup.Text = "Iniciar o leitor ao entrar no Windows";
        startup.AutoSize = true;
        startup.Font = CentralTheme.Body(11);
        startup.UseCompatibleTextRendering = true;
        startup.Margin = new Padding(0, 8, 0, 16);
        startup.CheckedChanged += StartupChanged;
        Add(preferences, startup);
        Add(preferences, Wrap("As digitais e o pareamento ficam protegidos nesta conta do Windows. Limpar o pareamento remove as digitais locais e exige uma nova configuração."));
        clearButton.Click += async (_, _) => await ClearPairingAsync();
        Add(preferences, Buttons(clearButton));
        Add(stack, Card(preferences));
        settingsPage.Controls.Add(stack);
    }

    private static Panel Page(string name) => new() { Name = name, Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty, BackColor = CentralTheme.Background };
    private static TableLayoutPanel Stack()
    {
        var stack = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        stack.SuspendLayout();
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return stack;
    }
    private static void ResumeTree(Control control)
    {
        foreach (Control child in control.Controls) ResumeTree(child);
        control.ResumeLayout(true);
    }
    private static void Add(TableLayoutPanel stack, Control control)
    {
        var row = stack.RowCount++;
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        stack.Controls.Add(control, 0, row);
    }
    private static CentralCard Card(Control content)
    {
        var card = new CentralCard { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
        card.Controls.Add(content);
        return card;
    }
    private static FlowLayoutPanel Buttons(params Button[] buttons)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0, 12, 0, 0) };
        panel.SuspendLayout();
        panel.Controls.AddRange(buttons);
        return panel;
    }
    private static Label TextLabel(string text, float size = 11, bool heading = false) => new()
    {
        Text = text, AutoSize = true, UseCompatibleTextRendering = true,
        Font = heading ? CentralTheme.Heading(size) : CentralTheme.Body(size),
        ForeColor = CentralTheme.Text, BackColor = Color.Transparent,
        Margin = new Padding(0, 0, 0, 8)
    };
    private Label Wrap(string text, float size = 11, bool heading = false)
    {
        var label = TextLabel(text, size, heading);
        wrappingLabels.Add(label);
        return label;
    }
    private static void ConfigureTextBox(TextBox box, string name)
    {
        box.AccessibleName = name;
        box.Font = CentralTheme.Body(12);
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Dock = DockStyle.Fill;
        box.Margin = new Padding(0, 0, 0, 14);
        box.MinimumSize = new Size(0, 32);
    }
    private void ResizeContent()
    {
        actionMessage.MaximumSize = new Size(Math.Max(240, ClientSize.Width - 64), 0);
        var full = Math.Max(320, settingsPage.ClientSize.Width - 68);
        foreach (var label in wrappingLabels) label.MaximumSize = new Size(full, 0);
        pairingState.MaximumSize = libraryState.MaximumSize = new Size(full, 0);
        var hero = Math.Max(280, readingPage.ClientSize.Width - 240);
        statusTitle.MaximumSize = statusMessage.MaximumSize = new Size(hero, 0);
    }

    internal void ShowSettings() => ShowPage(true);
    private void ShowPage(bool settings)
    {
        if (exiting) return;
        settingsPage.Visible = settings;
        readingPage.Visible = !settings;
        (settings ? settingsPage : readingPage).BringToFront();
        navigationReading.ForeColor = settings ? CentralTheme.Muted : CentralTheme.Primary;
        navigationSettings.ForeColor = settings ? CentralTheme.Primary : CentralTheme.Muted;
        navigationReading.Selected = !settings;
        navigationSettings.Selected = settings;
        navigationReading.AccessibleDescription = settings ? "Abrir a leitura automática" : "Aba atual";
        navigationSettings.AccessibleDescription = settings ? "Aba atual" : "Abrir configurações do computador";
        AcceptButton = settings ? pairButton : null;
        ResizeContent();
    }

    public void ReloadConfiguration()
    {
        var configuration = runtime.Configuration;
        if (displayedCentralUrl != configuration.CentralUrl)
        {
            centralUrl.Text = string.IsNullOrWhiteSpace(configuration.CentralUrl) ? "https://central.futureplacecoworking.com.br" : configuration.CentralUrl;
            displayedCentralUrl = configuration.CentralUrl;
        }
        scannerPath.Text = configuration.ScannerDllPath;
        pairingState.Text = configuration.IsPaired ? "Computador pareado. Cadastros e horários são administrados pela Central." : "Pareamento pendente. Gere um código na Central para conectar este computador.";
        libraryState.Text = string.IsNullOrWhiteSpace(configuration.ScannerDllPath) ? "Arquivo de captura pendente." : "Arquivo de captura configurado. Mantenha-o na pasta selecionada.";
        setupGuide.Visible = !configuration.IsPaired || string.IsNullOrWhiteSpace(configuration.ScannerDllPath);
        changingStartup = true;
        try { startup.Checked = StartupRegistration.IsEnabled(); }
        catch { startupAvailable = false; startup.Enabled = false; }
        finally { changingStartup = false; }
    }

    public void ShowSnapshot(AgentSnapshot snapshot)
    {
        if (exiting) return;
        statusTitle.Text = snapshot.Status == AgentStatus.Ready
            ? snapshot.Message.StartsWith("Retire o dedo", StringComparison.Ordinal) ? "Retire o dedo do leitor"
                : snapshot.Message.StartsWith("Digital cadastrada", StringComparison.Ordinal) ? "Digital cadastrada"
                : snapshot.Message.StartsWith("Cadastro confirmado", StringComparison.Ordinal) ? "Cadastro confirmado" : "Coloque o dedo no leitor"
            : StatusTitle(snapshot.Status);
        var accent = snapshot.Status switch
        {
            AgentStatus.Authorized => CentralTheme.Success,
            AgentStatus.Error or AgentStatus.Revoked => CentralTheme.Danger,
            AgentStatus.Denied or AgentStatus.Offline => CentralTheme.Warning,
            _ => CentralTheme.Primary
        };
        statusTitle.ForeColor = accent;
        statusCaption.ForeColor = accent;
        fingerprint.Accent = accent;
        statusMessage.Text = snapshot.Message;
        statusCaption.Text = snapshot.Status switch
        {
            AgentStatus.Capturing => "ACOMPANHE A LEITURA",
            AgentStatus.Authorized or AgentStatus.Denied => "DECISÃO DA CENTRAL",
            AgentStatus.Offline or AgentStatus.Revoked or AgentStatus.Error => "LEITOR PRECISA DE ATENÇÃO",
            AgentStatus.Ready => "LEITURA AUTOMÁTICA",
            _ => "LEITOR DA RECEPÇÃO"
        };
    }

    internal static string StatusTitle(AgentStatus status) => status switch
    {
        AgentStatus.Unconfigured => "Vamos preparar o leitor",
        AgentStatus.Connecting => "Conectando à Central",
        AgentStatus.Ready => "Pronto para ler",
        AgentStatus.Capturing => "Siga a orientação abaixo",
        AgentStatus.Authorized => "Autorizado pela Central",
        AgentStatus.Denied => "Acesso não autorizado",
        AgentStatus.Revoked => "Leitor revogado",
        AgentStatus.Offline => "Sem conexão com a Central",
        _ => "Verifique o leitor"
    };

    private void OpenCentral()
    {
        if (exiting) return;
        var address = runtime.Configuration.CentralUrl;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
        {
            ShowPage(true);
            actionMessage.Text = "Configure um endereço HTTPS válido da Central antes de abrir o navegador.";
            return;
        }
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { ShowPage(true); actionMessage.Text = "Abra o endereço da Central no navegador para cadastrar pessoas e definir os horários."; }
    }

    public void SetExiting()
    {
        exiting = true;
        SetInputsEnabled(false);
        foreach (var button in new[] { navigationReading, navigationSettings, openCentralButton, setupReader }) button.Enabled = false;
        startup.Enabled = false;
        statusTitle.Text = "Encerrando leitor…";
        statusMessage.Text = "Aguardando o leitor e a Central encerrarem as operações.";
    }

    protected override void Dispose(bool disposing)
    {
        var icon = disposing ? Icon : null;
        base.Dispose(disposing);
        if (disposing) { logo.Dispose(); icon?.Dispose(); }
    }
}

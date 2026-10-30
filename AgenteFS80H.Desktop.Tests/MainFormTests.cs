using System.Drawing.Imaging;
using System.Threading;
using Futureplace.AgenteFS80H;
using NUnit.Framework;

namespace AgenteFS80H.Desktop.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class MainFormTests
{
    [OneTimeSetUp]
    public void InitializeDesktop()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
    }

    [Test]
    public void StartupShowsReadingAndDoesNotInventAConnectionFromSavedPairing()
    {
        using var ui = new UiFixture();
        Assert.That(ui.Find<Control>("readingPage").Visible, Is.True);
        Assert.That(ui.Find<Control>("settingsPage").Visible, Is.False);
        Assert.That(ui.Find<Control>("setupGuide").Visible, Is.False);
        var title = ui.Find<Control>("readingTitle").Text;
        Assert.That(title, Does.Not.Contain("Coloque o dedo").And.Not.Contain("Autorizado"));
        Assert.That(title, Does.Not.Match(@"(?i)\bconectad[oa]\b"));
        Assert.That(ui.Store.Writes, Is.Zero);
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(false, false)]
    public void MissingPairingOrLibraryOffersVisibleSetup(bool paired, bool scanner)
    {
        using var ui = new UiFixture(paired, scanner);
        Assert.That(ui.Find<Control>("readingPage").Visible, Is.True);
        Assert.That(ui.Find<Control>("setupGuide").Visible, Is.True);
        Assert.That(ui.Find<Button>("setupReader").Visible, Is.True);
        ui.Click("setupReader");
        Assert.That(ui.Find<Control>("settingsPage").Visible, Is.True);
        Assert.That(ui.Find<Control>("readingPage").Visible, Is.False);
        Assert.That(ui.Store.Writes, Is.Zero);
    }

    [Test]
    public void StatusAndNavigationPreserveDraftAndDoNotChangePairingOrTemplates()
    {
        using var ui = new UiFixture();
        var state = ui.Store.LoadState();
        var templates = ui.Store.LoadTemplates().ToArray();
        ui.Click("navigationSettings");
        var url = ui.Find<TextBox>("centralUrl");
        var code = ui.Find<TextBox>("pairingCode");
        url.Text = "https://rascunho.example";
        code.Text = "codigo-sintetico";
        code.Focus();
        code.Select(3, 4);
        ui.Snapshot(AgentStatus.Capturing, "Retire o dedo completamente do leitor.");
        Assert.That(code.Focused, Is.True);
        Assert.That(code.SelectionStart, Is.EqualTo(3));
        Assert.That(code.SelectionLength, Is.EqualTo(4));
        Assert.That(code.UseSystemPasswordChar, Is.True);
        Assert.That(ui.Find<Control>("settingsPage").Visible, Is.True);
        ui.Click("navigationReading");
        ui.Form.ReloadConfiguration(); // Also called when the tray opens the window again.
        ui.Click("navigationSettings");
        Assert.That(ui.Find<TextBox>("centralUrl"), Is.SameAs(url));
        Assert.That(ui.Find<TextBox>("pairingCode"), Is.SameAs(code));
        Assert.That(url.Text, Is.EqualTo("https://rascunho.example"));
        Assert.That(code.Text, Is.EqualTo("codigo-sintetico"));
        Assert.That(ui.Store.LoadState(), Is.EqualTo(state));
        Assert.That(ui.Store.LoadTemplates(), Is.EqualTo(templates));
        Assert.That(ui.Store.Writes, Is.Zero);
    }

    [TestCase(AgentStatus.Unconfigured)]
    [TestCase(AgentStatus.Connecting)]
    [TestCase(AgentStatus.Ready)]
    [TestCase(AgentStatus.Capturing)]
    [TestCase(AgentStatus.Authorized)]
    [TestCase(AgentStatus.Denied)]
    [TestCase(AgentStatus.Revoked)]
    [TestCase(AgentStatus.Offline)]
    [TestCase(AgentStatus.Error)]
    public void RealRuntimeInstructionRemainsVisibleInEveryState(AgentStatus status)
    {
        using var ui = new UiFixture();
        const string message = "Retire o dedo para concluir a confirmação.";
        ui.Snapshot(status, message);
        Assert.That(ui.Find<Control>("readingInstruction").Visible, Is.True);
        Assert.That(ui.Find<Control>("readingInstruction").Text, Is.EqualTo(message));
        if (status == AgentStatus.Ready)
            Assert.That(ui.Find<Control>("readingTitle").Text, Is.EqualTo("Retire o dedo do leitor"));
        if (status == AgentStatus.Authorized)
            Assert.That(ui.Find<Control>("readingTitle").Text, Is.EqualTo("Autorizado pela Central"));
    }

    [Test]
    public void ReadyForANewReadingAsksThePersonToPlaceTheFinger()
    {
        using var ui = new UiFixture();
        const string message = "Coloque o dedo cadastrado no leitor. A decisão depende da Central conectada.";
        ui.Snapshot(AgentStatus.Ready, message);
        Assert.That(ui.Find<Control>("readingTitle").Text, Is.EqualTo("Coloque o dedo no leitor"));
        Assert.That(ui.Find<Control>("readingInstruction").Text, Is.EqualTo(message));
    }

    [Test]
    public void ReadyWaitingForFingerLiftNeverAsksThePersonToPlaceTheFinger()
    {
        using var ui = new UiFixture();
        const string message = "Retire o dedo do leitor antes de começar uma nova leitura.";
        ui.Snapshot(AgentStatus.Ready, message);
        Assert.That(ui.Find<Control>("readingTitle").Text, Is.EqualTo("Retire o dedo do leitor"));
        Assert.That(ui.Find<Control>("readingTitle").Text, Does.Not.Contain("Coloque"));
        Assert.That(ui.Find<Control>("readingInstruction").Text, Is.EqualTo(message));
    }

    [TestCase(AgentStatus.Offline, "Sem conexão. Acesso bloqueado.")]
    [TestCase(AgentStatus.Denied, "A Central não autorizou o acesso.")]
    [TestCase(AgentStatus.Revoked, "Leitor bloqueado pela Central.")]
    public void ABlockingStateImmediatelyReplacesTheAuthorizedPresentation(AgentStatus status, string message)
    {
        using var ui = new UiFixture();
        ui.Snapshot(AgentStatus.Authorized, "Autorizado pela Central. Nenhuma porta foi acionada.");
        var reading = ui.Find<Control>("readingPage");
        var approvedColors = Colors(reading);
        ui.Snapshot(status, message);
        Assert.That(ui.Find<Control>("readingTitle").Text, Does.Not.Contain("Autorizado pela Central"));
        Assert.That(ui.Find<Control>("readingInstruction").Text, Is.EqualTo(message));
        Assert.That(Colors(reading), Is.Not.EqualTo(approvedColors), "O sinal visual de autorização precisa desaparecer junto com o texto.");
    }

    [Test]
    public void SettingsNeverRenderStoredCredentialsAndKeepTheSelectedLibrary()
    {
        using var ui = new UiFixture();
        ui.Click("navigationSettings");
        var text = string.Join("\n", UiFixture.Tree(ui.Form).Select(c => c.Text + " " + c.AccessibleName + " " + c.AccessibleDescription));
        Assert.That(text, Does.Not.Contain(UiFixture.SyntheticToken).And.Not.Contain(UiFixture.SyntheticKey));
        Assert.That(ui.Find<TextBox>("scannerPath").Text, Is.EqualTo(@"C:\Futronic\ftrScanAPI.dll"));
        Assert.That(ui.Find<TextBox>("scannerPath").ReadOnly, Is.True);
    }

    [Test]
    public void ClosingTheWindowHidesItWithoutErasingLocalState()
    {
        using var ui = new UiFixture();
        var state = ui.Store.LoadState();
        ui.Form.Close();
        UiFixture.Pump();
        Assert.That(ui.Form.Visible, Is.False);
        Assert.That(ui.Form.IsDisposed, Is.False);
        Assert.That(ui.Store.LoadState(), Is.EqualTo(state));
        Assert.That(ui.Store.LoadTemplates(), Has.Count.EqualTo(1));
        Assert.That(ui.Store.Writes, Is.Zero);
    }

    [Test]
    public void ExitingDisablesNavigationAndAllActions()
    {
        using var ui = new UiFixture();
        ui.Click("navigationSettings");
        ui.Form.SetExiting();
        foreach (var name in new[] { "navigationReading", "navigationSettings", "openCentralButton", "pairButton", "clearButton", "selectDllButton", "setupReader", "centralUrl", "pairingCode", "startup" })
            Assert.That(ui.Find<Control>(name).Enabled, Is.False, $"{name} deve estar desabilitado durante o encerramento.");
        Assert.That(ui.Form.PendingOperation.IsCompleted, Is.True);
    }

    [Test]
    public void MinimumWindowKeepsReadingVisibleAndSettingsReachableByScrolling()
    {
        using var ui = new UiFixture();
        ui.Form.Size = new Size(860, 680);
        ui.Snapshot(AgentStatus.Ready, "Coloque o dedo cadastrado no leitor.");
        var reading = ui.Find<Control>("readingPage");
        AssertInViewport(ui.Find<Control>("readingTitle"), reading);
        AssertInViewport(ui.Find<Control>("readingInstruction"), reading);
        ui.Click("navigationSettings");
        var settings = ui.Find<ScrollableControl>("settingsPage");
        foreach (var name in new[] { "centralUrl", "pairingCode", "pairButton", "scannerPath", "selectDllButton", "startup" })
        {
            var control = ui.Find<Control>(name);
            settings.ScrollControlIntoView(control);
            UiFixture.Pump();
            AssertInViewport(control, settings);
        }
    }

    [TestCase("ready")]
    [TestCase("setup")]
    [TestCase("settings")]
    [TestCase("capture")]
    [TestCase("offline")]
    [TestCase("authorized")]
    [TestCase("denied")]
    public void NativePreviewUsesOnlySyntheticState(string scenario)
    {
        using var ui = new UiFixture(paired: scenario != "setup", scanner: scenario != "setup");
        var (status, message) = scenario switch
        {
            "setup" => (AgentStatus.Unconfigured, "Conecte este computador à Central para preparar o leitor."),
            "capture" => (AgentStatus.Capturing, "Retire o dedo completamente do leitor."),
            "offline" => (AgentStatus.Offline, "Sem conexão com a Central. Nenhum acesso será autorizado offline."),
            "authorized" => (AgentStatus.Authorized, "Autorizado pela Central. Nenhuma porta foi acionada."),
            "denied" => (AgentStatus.Denied, "A Central não autorizou o acesso. Confira o cadastro e o horário na Central."),
            _ => (AgentStatus.Ready, "Coloque o dedo cadastrado no leitor. A decisão depende da Central conectada.")
        };
        ui.Snapshot(status, message);
        if (scenario == "settings") ui.Click("navigationSettings");
        Assert.That(UiFixture.VisibleText(ui.Form), Does.Match(@"(?i)(não\s+(?:abre|aciona)\s+(?:a\s+)?(?:porta|fechadura)|nenhuma\s+porta.{0,20}acionada)"));
        var output = Environment.GetEnvironmentVariable("FS80H_UI_OUTPUT")
            ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "ui");
        Directory.CreateDirectory(output);
        var path = Path.Combine(output, scenario + ".png");
        ui.Form.Update();
        using var bitmap = new Bitmap(ui.Form.Width, ui.Form.Height);
        ui.Form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(path, ImageFormat.Png);
        TestContext.AddTestAttachment(path, $"Prévia nativa {scenario}; dados sintéticos, sem rede ou USB.");
    }

    private static string Colors(Control root) => string.Join("|", UiFixture.Tree(root).Select(c => $"{c.ForeColor.ToArgb()}:{c.BackColor.ToArgb()}"));

    private static void AssertInViewport(Control control, Control viewport)
    {
        Assert.That(control.Visible, Is.True, $"{control.Name} precisa estar visível.");
        var bounds = viewport.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
        var available = viewport.ClientRectangle;
        available.Inflate(2, 2);
        Assert.That(available.Contains(bounds), Is.True, $"{control.Name} fora da área visível: {bounds}, área {available}.");
    }
}

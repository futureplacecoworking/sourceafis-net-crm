using System.Drawing.Imaging;
using System.Reflection;
using System.Threading;
using Futureplace.AgenteFS80H;
using NUnit.Framework;

namespace AgenteFS80H.Desktop.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ClientScreenTests
{
    [Test]
    public void ReceptionistOpensOneReadOnlyPresentationWithoutChangingLocalData()
    {
        using var ui = new UiFixture();
        using var client = OpenClient(ui);
        Assert.That(client.Visible, Is.True);
        Assert.That(ui.Form.Visible, Is.True);
        ui.Click("clientScreenButton");
        Assert.That(ClientWindows(), Has.Count.EqualTo(1));
        Assert.That(ClientWindows().Single(), Is.SameAs(client));
        Assert.That(UiFixture.Tree(client).OfType<TextBox>(), Is.Empty);
        Assert.That(UiFixture.Tree(client).OfType<Button>(), Is.Empty);
        Assert.That(UiFixture.Tree(client).OfType<PictureBox>().Any(c => c.Image is not null), Is.True);
        Assert.That(Find(client, "clientAnimation").Visible, Is.True);
        Assert.That(UiFixture.VisibleText(client), Does.Not.Contain("Coloque o dedo"), "O estado inicial ainda não confirma que o leitor está pronto.");
        Assert.That(ui.Store.Writes, Is.Zero);
        Assert.That(ui.Store.LoadTemplates(), Has.Count.EqualTo(1));
    }

    [TestCase(AgentStatus.Ready, "Coloque o dedo cadastrado no leitor. A decisão depende da Central conectada.", "Coloque o dedo no leitor")]
    [TestCase(AgentStatus.Capturing, "Coloque o dedo no leitor para a primeira leitura.", "Coloque o dedo no leitor")]
    [TestCase(AgentStatus.Capturing, "Retire o dedo completamente do leitor.", "Retire o dedo do leitor")]
    [TestCase(AgentStatus.Capturing, "Retire o dedo para concluir a confirmação.", "Retire o dedo do leitor")]
    [TestCase(AgentStatus.Capturing, "Coloque o mesmo dedo novamente para confirmar.", "Repita com o mesmo dedo")]
    [TestCase(AgentStatus.Ready, "Retire o dedo do leitor antes de começar uma nova leitura.", "Retire o dedo do leitor")]
    public void ClientFollowsTheSameCaptureAndLiftInstructionsAsTheReceptionist(AgentStatus status, string message, string expected)
    {
        using var ui = new UiFixture();
        using var client = OpenClient(ui);
        ui.Snapshot(status, message);
        Assert.That(Find(client, "clientTitle").Text, Is.EqualTo(expected));
        Assert.That(ui.Find<Control>("readingInstruction").Text, Is.EqualTo(message));
        Assert.That(Find(client, "clientInstruction").Visible, Is.True);
        Assert.That(ui.Store.Writes, Is.Zero);
    }

    [Test]
    public void EveryPublicStateSuppressesPrivateRuntimeDetailsAndHasNoAdministrativeControls()
    {
        using var ui = new UiFixture();
        using var client = OpenClient(ui);
        var privateMessage = $"Thompson; C:\\Futronic\\ftrScanAPI.dll; https://central.example; pareamento; {UiFixture.SyntheticToken}; {UiFixture.SyntheticKey}";
        foreach (var status in Enum.GetValues<AgentStatus>())
        {
            ui.Snapshot(status, privateMessage);
            var publicText = string.Join("\n", UiFixture.Tree(client).Select(c => c.Text + c.AccessibleName + c.AccessibleDescription));
            foreach (var secret in new[] { "Thompson", "Futronic", "DLL", "central.example", "pareamento", "Configurações", UiFixture.SyntheticToken, UiFixture.SyntheticKey })
                Assert.That(publicText, Does.Not.Contain(secret), $"Informação interna na apresentação em {status}.");
            Assert.That(Find(client, "clientTitle").Text, Is.Not.Empty);
        }
        Assert.That(ui.Store.Writes, Is.Zero);
    }

    [Test]
    public void AuthorizedIdentificationDoesNotClaimTheDoorOpenedAndFailuresReplaceIt()
    {
        using var ui = new UiFixture();
        using var client = OpenClient(ui);
        foreach (var blocked in new[] { AgentStatus.Offline, AgentStatus.Error, AgentStatus.Revoked, AgentStatus.Denied, AgentStatus.Connecting, AgentStatus.Unconfigured })
        {
            ui.Snapshot(AgentStatus.Authorized, "Autorizado pela Central. Nenhuma porta foi acionada.");
            var title = Find(client, "clientTitle");
            Assert.That(title.Text, Does.Match(@"^(Bom dia|Boa tarde|Boa noite)!$"));
            Assert.That(Find(client, "clientCaption").Text, Is.EqualTo("IDENTIFICAÇÃO AUTORIZADA"));
            Assert.That(Find(client, "clientWelcome").Text, Is.EqualTo("Seja bem-vindo ao Future Place!"));
            Assert.That(Find(client, "clientInstruction").Text, Is.EqualTo("Aguarde a recepcionista para entrar."));
            Assert.That(UiFixture.VisibleText(client), Does.Not.Match(@"(?i)(porta (?:liberada|aberta)|pode entrar|acesso liberado)"));
            var successColor = title.ForeColor;
            ui.Snapshot(blocked, "mensagem interna não deve ser exibida");
            Assert.That(title.Text, Does.Not.Match(@"^(Bom dia|Boa tarde|Boa noite)!$"));
            Assert.That(Find(client, "clientWelcome").Visible, Is.False);
            Assert.That(title.ForeColor, Is.Not.EqualTo(successColor));
        }
    }

    [Test]
    public void EnrollmentCompletionIsNotPresentedAsAccessAuthorization()
    {
        using var ui = new UiFixture();
        using var client = OpenClient(ui);
        foreach (var message in new[] { "Digital cadastrada na Central. O cadastro ainda não concede autorização de acesso.", "Cadastro confirmado na Central. O acesso precisa ser habilitado pela administração." })
        {
            ui.Snapshot(AgentStatus.Ready, message);
            Assert.That(Find(client, "clientTitle").Text, Is.EqualTo("Digital cadastrada"));
            Assert.That(Find(client, "clientInstruction").Text, Does.Contain("recepcionista"));
            Assert.That(UiFixture.VisibleText(client), Does.Not.Contain("Identificação autorizada"));
        }
    }

    [Test]
    public void ClosingAndReopeningTheClientKeepsTheReaderAndUsesTheLatestState()
    {
        using var ui = new UiFixture();
        using var first = OpenClient(ui);
        first.Close();
        UiFixture.Pump();
        Assert.That(first.IsDisposed, Is.True);
        Assert.That(ui.Form.IsDisposed, Is.False);
        ui.Snapshot(AgentStatus.Offline, "Sem conexão com a Central.");
        using var second = OpenClient(ui);
        Assert.That(Find(second, "clientTitle").Text, Is.EqualTo("Leitor indisponível"));
        ui.Form.Close(); // Receptionist window goes to tray; client keeps working.
        ui.Snapshot(AgentStatus.Ready, "Coloque o dedo cadastrado no leitor.");
        Assert.That(second.Visible, Is.True);
        Assert.That(Find(second, "clientTitle").Text, Is.EqualTo("Coloque o dedo no leitor"));
        Assert.That(ui.Store.Writes, Is.Zero);
    }

    [Test]
    public void ClientWindowShortcutsDoNotCloseTheReceptionistAndShutdownRemovesAuthorization()
    {
        using var ui = new UiFixture();
        using var client = OpenClient(ui);
        Assert.That(client.FormBorderStyle, Is.EqualTo(FormBorderStyle.None));
        Assert.That(client.WindowState, Is.EqualTo(FormWindowState.Maximized));
        Key(client, Keys.F11);
        Assert.That(client.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
        client.WindowState = FormWindowState.Minimized;
        UiFixture.Pump();
        ui.Click("clientScreenButton");
        Assert.That(client.WindowState, Is.EqualTo(FormWindowState.Normal), "Reabrir precisa restaurar a janela minimizada existente.");
        Assert.That(ClientWindows(), Has.Count.EqualTo(1));
        Key(client, Keys.F11);
        client.WindowState = FormWindowState.Minimized;
        UiFixture.Pump();
        ui.Click("clientScreenButton");
        Assert.That(client.WindowState, Is.EqualTo(FormWindowState.Maximized));
        Assert.That(client.FormBorderStyle, Is.EqualTo(FormBorderStyle.None));
        Key(client, Keys.Escape);
        Assert.That(client.IsDisposed, Is.True);
        Assert.That(ui.Form.Visible, Is.True);
        using var reopened = OpenClient(ui);
        ui.Snapshot(AgentStatus.Authorized, "Autorizado pela Central.");
        ui.Form.SetExiting();
        UiFixture.Pump();
        Assert.That(reopened.IsDisposed || !reopened.Visible, Is.True);
        Assert.That(ui.Find<Button>("clientScreenButton").Enabled, Is.False);
    }

    [Test]
    public void AnimationAdvancesWhileTheAuthorizedInstructionRemainsStable()
    {
        using var ui = new UiFixture();
        using var client = OpenClient(ui);
        Key(client, Keys.F11);
        client.ClientSize = new Size(900, 650);
        ui.Snapshot(AgentStatus.Authorized, "Autorizado pela Central.");
        var text = UiFixture.VisibleText(client);
        var output = Environment.GetEnvironmentVariable("FS80H_UI_OUTPUT") ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "ui");
        var frames = Path.Combine(output, "client-motion");
        Directory.CreateDirectory(frames);
        byte[]? first = null;
        byte[]? last = null;
        for (var i = 0; i < 20; i++)
        {
            UiFixture.Pump();
            using var bitmap = new Bitmap(client.Width, client.Height);
            client.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            using var memory = new MemoryStream();
            bitmap.Save(memory, ImageFormat.Png);
            last = memory.ToArray();
            first ??= last;
            File.WriteAllBytes(Path.Combine(frames, $"frame-{i:000}.png"), last);
            var until = DateTime.UtcNow.AddMilliseconds(80);
            while (DateTime.UtcNow < until) { UiFixture.Pump(); Thread.Sleep(10); }
        }
        Assert.That(last, Is.Not.EqualTo(first), "A animação precisa avançar enquanto a tela está visível.");
        Assert.That(UiFixture.VisibleText(client), Is.EqualTo(text));
        Assert.That(ui.Store.Writes, Is.Zero);
    }

    [TestCase("client-ready", AgentStatus.Ready, "Coloque o dedo cadastrado no leitor.")]
    [TestCase("client-lift", AgentStatus.Capturing, "Retire o dedo completamente do leitor.")]
    [TestCase("client-repeat", AgentStatus.Capturing, "Coloque o mesmo dedo novamente para confirmar.")]
    [TestCase("client-authorized", AgentStatus.Authorized, "Autorizado pela Central. Nenhuma porta foi acionada.")]
    [TestCase("client-denied", AgentStatus.Denied, "A Central não autorizou o acesso.")]
    [TestCase("client-offline", AgentStatus.Offline, "Sem conexão com a Central.")]
    public void NativeClientPreviewKeepsTheMainInstructionVisible(string name, AgentStatus status, string message)
    {
        using var ui = new UiFixture();
        using var client = OpenClient(ui);
        Key(client, Keys.F11);
        client.ClientSize = new Size(900, 650);
        ui.Snapshot(status, message);
        var title = Find(client, "clientTitle");
        var instruction = Find(client, "clientInstruction");
        foreach (var control in new[] { title, instruction })
        {
            var bounds = client.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
            Assert.That(client.ClientRectangle.Contains(bounds), Is.True, $"{control.Name} deve estar dentro da janela.");
        }
        var output = Environment.GetEnvironmentVariable("FS80H_UI_OUTPUT") ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "ui");
        Directory.CreateDirectory(output);
        var path = Path.Combine(output, name + ".png");
        client.Update();
        using var bitmap = new Bitmap(client.Width, client.Height);
        client.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(path, ImageFormat.Png);
        TestContext.AddTestAttachment(path, "Tela do cliente com estado sintético, sem rede ou USB.");
    }

    private static List<Form> ClientWindows() => Application.OpenForms.Cast<Form>().Where(f => f.Name == "clientScreen").ToList();
    private static Form OpenClient(UiFixture ui)
    {
        ui.Click("clientScreenButton");
        Assert.That(ClientWindows(), Has.Count.EqualTo(1));
        return ClientWindows().Single();
    }
    private static Control Find(Form form, string name) => form.Controls.Find(name, true).Single();
    private static void Key(Form form, Keys key)
    {
        var message = Message.Create(form.Handle, 0x100, (IntPtr)(int)key, IntPtr.Zero);
        var method = form.GetType().GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.That(method.Invoke(form, new object[] { message, key }), Is.EqualTo(true));
        UiFixture.Pump();
    }
}

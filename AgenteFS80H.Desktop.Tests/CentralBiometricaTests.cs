using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using Futureplace.AgenteFS80H;
using Futureplace.AgenteFS80H.Desktop;
using NUnit.Framework;

namespace AgenteFS80H.Desktop.Tests;

/// <summary>Versão 1.4: tela do cliente nova, pareamento protegido, lista de preparação e som. Tudo sintético.</summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class CentralBiometricaTests
{
    [Test]
    public void TheClientScreenUsesTheNewPageAndSendsOnlyStructuredState()
    {
        using var ui = new UiFixture();
        var host = new FakeWebHost();
        ui.Form.ClientWebHostFactory = () => host;
        ui.Click("clientScreenButton");
        var client = ClientWindow();
        Assert.That(host.Initialized, Is.True);
        Assert.That(Find(client, "clientNativeScreen").Visible, Is.False);
        Assert.That(host.View.Visible, Is.True);
        ui.Snapshot(new AgentSnapshot(AgentStatus.Capturing, $"mensagem interna {UiFixture.SyntheticToken}")
        {
            Enrollment = new EnrollmentProgress { Name = "Ana Souza", Total = 4, Good = 1, Reading = 2, Phase = EnrollmentPhase.Place }
        });
        using (var state = JsonDocument.Parse(host.Sent.Last()))
        {
            Assert.That(state.RootElement.GetProperty("fase").GetString(), Is.EqualTo("cadastro"));
            Assert.That(state.RootElement.GetProperty("leitN").GetString(), Is.EqualTo("Leitura 2 de 4"));
        }
        Assert.That(host.Sent, Has.None.Contain(UiFixture.SyntheticToken));
        var count = host.Sent.Count;
        ui.Snapshot(new AgentSnapshot(AgentStatus.Capturing, "outra mensagem interna")
        {
            Enrollment = new EnrollmentProgress { Name = "Ana Souza", Total = 4, Good = 1, Reading = 2, Phase = EnrollmentPhase.Place }
        });
        Assert.That(host.Sent, Has.Count.EqualTo(count), "O mesmo estado não é reenviado a cada ciclo do leitor.");
        Assert.That(ui.Find<Control>("checkClient").Text, Does.Contain("Aberta"));
        Assert.That(ui.Store.Writes, Is.Zero);
    }

    [TestCase(true, "WebView2 Runtime")]
    [TestCase(false, "modo simples")]
    public void WithoutWebView2TheClientScreenFallsBackAndTheReceptionLearnsWhatToDo(bool missing, string guidance)
    {
        using var ui = new UiFixture();
        var host = new FakeWebHost { Failure = new ClientWebUnavailableException(missing) };
        ui.Form.ClientWebHostFactory = () => host;
        ui.Click("clientScreenButton");
        var client = ClientWindow();
        UiFixture.Pump();
        Assert.That(host.Disposed, Is.True);
        Assert.That(Find(client, "clientNativeScreen").Visible, Is.True);
        Assert.That(Find(client, "clientTitle").Text, Is.Not.Empty);
        Assert.That(ui.Find<Control>("actionMessage").Text, Does.Contain(guidance));
        Assert.That(ui.Find<Control>("checkClient").Text, Does.Contain("modo simples"));
        Assert.That(ui.Find<Button>("checkClientAction").Text, Is.EqualTo("Baixar WebView2"));
        ui.Snapshot(AgentStatus.Offline, "Sem conexão com a Central.");
        Assert.That(Find(client, "clientTitle").Text, Is.EqualTo("Leitor indisponível"));
    }

    [Test]
    public void AFactoryThatCannotCreateTheBrowserAlsoFallsBack()
    {
        using var ui = new UiFixture();
        ui.Form.ClientWebHostFactory = () => throw new DllNotFoundException("WebView2Loader.dll");
        ui.Click("clientScreenButton");
        UiFixture.Pump();
        Assert.That(Find(ClientWindow(), "clientNativeScreen").Visible, Is.True);
        Assert.That(ui.Find<Control>("actionMessage").Text, Does.Contain("modo simples"));
    }

    [Test]
    public void ClearingThePairingRequiresTypingLimparAndTellsTheCentral()
    {
        var central = new FakeCentralClient { Features = new HashSet<string> { CentralClient.FeatureUnpair } };
        using var ui = new UiFixture(central: _ => central, deviceName: "Recepção — FS80H");
        central.OnUnpair = () => Assert.That(ui.Store.LoadTemplates(), Is.Empty, "As digitais saem antes do aviso.");
        TypedConfirmation? asked = null;
        ui.Form.ConfirmTyped = request => { asked = request; return false; };
        ui.Click("navigationSettings");
        ui.Click("clearButton");
        Assert.That(asked?.Word, Is.EqualTo("LIMPAR"));
        Assert.That(asked!.Message, Does.Contain("«Recepção — FS80H»").And.Contain("digitais").And.Contain("LIMPAR"));
        Assert.That(ui.Store.Writes, Is.Zero, "Sem digitar LIMPAR, nada muda.");
        Assert.That(ui.Store.LoadTemplates(), Has.Count.EqualTo(1));
        Assert.That(central.UnpairCalls, Is.Zero);

        ui.Form.ConfirmTyped = _ => true;
        ui.Click("clearButton");
        PumpUntil(() => ui.Form.PendingOperation.IsCompleted);
        Assert.That(central.UnpairCalls, Is.EqualTo(1));
        Assert.That(ui.Store.LoadTemplates(), Is.Empty);
        Assert.That(ui.Runtime.Configuration.IsPaired, Is.False);
        Assert.That(ui.Find<Control>("actionMessage").Text, Does.Contain("A Central também desligou este leitor"));
        Assert.That(ui.Find<Control>("pairingSummary").Text, Is.EqualTo("Ainda não pareado"));
    }

    [Test]
    public void ClearingWithAnOldOrOfflineCentralSaysToRevokeTheReaderManually()
    {
        var central = new FakeCentralClient { FailSync = true };
        using var ui = new UiFixture(central: _ => central);
        ui.Form.ConfirmTyped = _ => true;
        ui.Click("navigationSettings");
        ui.Click("clearButton");
        PumpUntil(() => ui.Form.PendingOperation.IsCompleted);
        Assert.That(ui.Store.LoadTemplates(), Is.Empty);
        Assert.That(central.UnpairCalls, Is.Zero);
        Assert.That(ui.Find<Control>("actionMessage").Text, Does.Contain("Digitais › Central biométrica"));
    }

    [Test]
    public void ReplacingTheReaderOfAPairedComputerRequiresTypingTrocar()
    {
        using var ui = new UiFixture(deviceName: "Recepção — FS80H");
        TypedConfirmation? asked = null;
        ui.Form.ConfirmTyped = request => { asked = request; return false; };
        ui.Click("navigationSettings");
        ui.Find<TextBox>("pairingCode").Text = "codigo-sintetico";
        ui.Click("pairButton");
        Assert.That(asked?.Word, Is.EqualTo("TROCAR"));
        Assert.That(asked!.Message, Does.Contain("«Recepção — FS80H»").And.Contain("TROCAR"));
        Assert.That(ui.Store.Writes, Is.Zero);
        Assert.That(ui.Store.LoadTemplates(), Has.Count.EqualTo(1));
        Assert.That(ui.Runtime.Configuration.IsPaired, Is.True);
        Assert.That(ui.Find<Control>("actionMessage").Text, Does.Contain("Pareamento mantido"));
    }

    [TestCase(" limpar ", "LIMPAR", true)]
    [TestCase("LIMPAR", "LIMPAR", true)]
    [TestCase("Trocar", "TROCAR", true)]
    [TestCase("LIMPA", "LIMPAR", false)]
    [TestCase("LIMPAR!", "LIMPAR", false)]
    [TestCase("TROCAR", "LIMPAR", false)]
    [TestCase("", "LIMPAR", false)]
    [TestCase(null, "LIMPAR", false)]
    public void OnlyTheExactWordConfirms(string? typed, string word, bool expected) =>
        Assert.That(TypedConfirmation.Matches(typed, word), Is.EqualTo(expected));

    [Test]
    public void TheConfirmationWindowOnlyEnablesTheButtonWithTheWord()
    {
        using var dialog = new TypedConfirmationDialog(TypedConfirmation.ClearPairing("Recepção — FS80H"));
        Assert.That(dialog.CanConfirm, Is.False);
        Assert.That(dialog.Accept(), Is.False);
        dialog.TypedText = "LIMPA";
        Assert.That(dialog.CanConfirm, Is.False);
        dialog.TypedText = "limpar";
        Assert.That(dialog.CanConfirm, Is.True);
        Assert.That(dialog.Accept(), Is.True);
        Assert.That(dialog.DialogResult, Is.EqualTo(DialogResult.OK));
        Assert.That(UiFixture.VisibleText(dialog) + dialog.Controls.Find("confirmationMessage", true).Single().Text, Does.Contain("LIMPAR"));
    }

    [Test]
    public void ThePairingStatusShowsTheReaderNameLastContactAndVersion()
    {
        using var ui = new UiFixture(deviceName: "Recepção — FS80H");
        Assert.That(ui.Find<Control>("pairingSummary").Text, Is.EqualTo("Ligado à Central como «Recepção — FS80H»"));
        Assert.That(ui.Find<Control>("pairingDetail").Text, Does.Contain("Ainda sem contato").And.Contain("Central Biométrica " + AppIdentity.Version));
        ui.Snapshot(new AgentSnapshot(AgentStatus.Ready, "Coloque o dedo cadastrado no leitor.") { Hint = ReaderHint.PlaceFinger, LastContactAt = DateTimeOffset.UtcNow });
        Assert.That(ui.Find<Control>("pairingDetail").Text, Does.StartWith("Último contato com a Central:"));
        Assert.That(ui.Find<Control>("checkPairing").Text, Is.EqualTo("Ligado à Central como «Recepção — FS80H»."));
        Assert.That(ui.Find<Control>("checkPairingMark").Text, Is.EqualTo("✓"));
        Assert.That(ui.Find<Control>("checkCentralMark").Text, Is.EqualTo("✓"));
        Assert.That(ui.Store.Writes, Is.Zero);

        using var unpaired = new UiFixture(paired: false, scanner: false);
        Assert.That(unpaired.Find<Control>("pairingSummary").Text, Is.EqualTo("Ainda não pareado"));
        Assert.That(unpaired.Find<Control>("checkPairing").Text, Does.Contain("Digitais › Central biométrica"));
        Assert.That(unpaired.Find<Control>("checkPairingMark").Text, Is.EqualTo("○"));
        Assert.That(unpaired.Find<Button>("checkPairingAction").Visible, Is.True);
        Assert.That(unpaired.Find<Button>("checkLibraryAction").Visible, Is.True);
    }

    [Test]
    public void TheChecklistExplainsEachStepFromTheCurrentStateOnly()
    {
        var paired = new AgentConfiguration { DeviceId = Guid.NewGuid(), DeviceToken = "x", DeviceName = "Recepção — FS80H", ScannerDllPath = @"C:\Futronic\ftrScanAPI.dll" };
        var healthy = new DiagnosticEntry("ok", DiagnosticTone.Healthy);
        var now = DateTimeOffset.UtcNow;
        var ready = new AgentSnapshot(AgentStatus.Ready, "Coloque o dedo cadastrado no leitor.") { Hint = ReaderHint.PlaceFinger, LastContactAt = now };
        var items = MainForm.ChecklistItems(paired, ready, healthy, clientOpen: true, clientWebPage: true, null, now).ToDictionary(i => i.Key, i => i.Item);
        Assert.That(items.Values.Select(i => i.State), Is.All.EqualTo(ChecklistState.Done));

        var offline = new AgentSnapshot(AgentStatus.Offline, "Sem conexão.") { LastContactAt = now.AddMinutes(-10) };
        items = MainForm.ChecklistItems(paired, offline, healthy, clientOpen: true, clientWebPage: false, ClientWebFailure.RuntimeMissing, now).ToDictionary(i => i.Key, i => i.Item);
        Assert.That(items["Central"].State, Is.EqualTo(ChecklistState.Attention));
        Assert.That(items["Client"].State, Is.EqualTo(ChecklistState.Attention));
        Assert.That(items["Client"].Action, Is.EqualTo("Baixar WebView2"));
        Assert.That(items["Reader"].State, Is.Not.EqualTo(ChecklistState.Done), "Sem conexão não se inventa leitor funcionando.");

        var revoked = new AgentSnapshot(AgentStatus.Revoked, "Leitor bloqueado.");
        items = MainForm.ChecklistItems(paired, revoked, healthy, clientOpen: false, clientWebPage: false, null, now).ToDictionary(i => i.Key, i => i.Item);
        Assert.That(items["Pairing"].State, Is.EqualTo(ChecklistState.Attention));
        Assert.That(items["Pairing"].Detail, Does.Contain("Digitais › Central biométrica"));
        Assert.That(items["Client"].State, Is.EqualTo(ChecklistState.Pending));
    }

    [Test]
    public void TheReceptionistFollowsTheEnrollmentReadingByReading()
    {
        using var ui = new UiFixture();
        ui.Snapshot(new AgentSnapshot(AgentStatus.Capturing, "Cadastro de Ana Souza: peça para colocar o mesmo dedo de novo (leitura 2 de 4).")
        {
            Enrollment = new EnrollmentProgress { Name = "Ana Souza", Total = 4, Good = 1, Reading = 2, Phase = EnrollmentPhase.Place }
        });
        Assert.That(ui.Find<Control>("readingTitle").Text, Is.EqualTo("Cadastrando Ana Souza — leitura 2 de 4"));
        Assert.That(ui.Find<Control>("readingInstruction").Text, Does.Contain("leitura 2 de 4"));
        ui.Snapshot(new AgentSnapshot(AgentStatus.Ready, "Cadastro cancelado na Central. Nenhuma digital foi gravada.")
        {
            Enrollment = new EnrollmentProgress { Name = "Ana Souza", Total = 4, Good = 1, Reading = 2, Phase = EnrollmentPhase.Cancelled }
        });
        Assert.That(ui.Find<Control>("readingTitle").Text, Is.EqualTo("Cadastro cancelado na Central"));
        ui.Snapshot(new AgentSnapshot(AgentStatus.Authorized, "Autorizado pela Central. Nenhuma porta foi acionada.")
        {
            Identification = new IdentificationProgress { Completed = 4, Result = IdentificationResult.Authorized, Name = "Ana Souza" }
        });
        Assert.That(ui.Find<Control>("readingTitle").Text, Is.EqualTo("Autorizado pela Central: Ana Souza"));
    }

    [Test]
    public void SoundStartsOnAndTheChoiceIsSavedInTheConfiguration()
    {
        using var ui = new UiFixture();
        ui.Click("navigationSettings");
        var sound = ui.Find<CheckBox>("soundEnabled");
        Assert.That(sound.Checked, Is.True);
        Assert.That(ui.Store.Writes, Is.Zero);
        sound.Checked = false;
        UiFixture.Pump();
        Assert.That(ui.Store.LoadState().Configuration.SoundEnabled, Is.False);
        Assert.That(ui.Store.LoadState().Configuration.DeviceToken, Is.EqualTo(UiFixture.SyntheticToken));
        var writes = ui.Store.Writes;
        ui.Form.ReloadConfiguration();
        Assert.That(sound.Checked, Is.False);
        Assert.That(ui.Store.Writes, Is.EqualTo(writes));
    }

    [Test]
    public void TheTonesAreShortGeneratedWaveFiles()
    {
        foreach (var wave in new[] { ToneGenerator.Positive(), ToneGenerator.Negative() })
        {
            Assert.That(Encoding.ASCII.GetString(wave, 0, 4), Is.EqualTo("RIFF"));
            Assert.That(Encoding.ASCII.GetString(wave, 8, 4), Is.EqualTo("WAVE"));
            Assert.That(BitConverter.ToInt32(wave, 4), Is.EqualTo(wave.Length - 8));
            Assert.That(BitConverter.ToInt32(wave, 24), Is.EqualTo(ToneGenerator.SampleRate));
            Assert.That(BitConverter.ToInt32(wave, 40), Is.EqualTo(wave.Length - 44));
            var seconds = (wave.Length - 44) / 2.0 / ToneGenerator.SampleRate;
            Assert.That(seconds, Is.InRange(0.2, 0.6), "Bipe curto.");
            Assert.That(Enumerable.Range(0, (wave.Length - 44) / 2).Max(i => Math.Abs((int)BitConverter.ToInt16(wave, 44 + i * 2))), Is.GreaterThan(5000));
            Assert.That(BitConverter.ToInt16(wave, 44), Is.EqualTo(0), "Começa em silêncio, sem estalo.");
        }
        Assert.That(ToneGenerator.Positive(), Is.Not.EqualTo(ToneGenerator.Negative()));
    }

    [Test]
    public void TheOldMenuPathIsNoLongerShown()
    {
        using var ui = new UiFixture(paired: false, scanner: false);
        ui.Click("navigationSettings");
        var text = string.Join("\n", UiFixture.Tree(ui.Form).Select(c => c.Text));
        Assert.That(text, Does.Not.Contain("Automação e sensores"));
        Assert.That(text, Does.Contain("Digitais › Central biométrica"));
    }

    [Test]
    public void PairingAndClearingMessagesNameTheReaderAndTheNextStep()
    {
        Assert.That(MainForm.PairingSuccessText(new PairingResult("Recepção — FS80H", UnpairNotice.NotPaired)),
            Is.EqualTo("Pronto! Este computador está ligado à Central como «Recepção — FS80H»."));
        Assert.That(MainForm.PairingSuccessText(new PairingResult("", UnpairNotice.Unavailable)), Does.Contain("revogue o leitor antigo em Digitais › Central biométrica"));
        Assert.That(MainForm.ClearPairingText(UnpairNotice.Unavailable), Does.Contain("Digitais › Central biométrica"));
        Assert.That(MainForm.ClearPairingText(UnpairNotice.Confirmed), Does.Contain("A Central também desligou este leitor"));
    }

    private static Form ClientWindow() => Application.OpenForms.Cast<Form>().Single(f => f.Name == "clientScreen");
    private static Control Find(Form form, string name) => form.Controls.Find(name, true).Single();

    private static void PumpUntil(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(10)) Assert.Fail("A operação não terminou.");
            UiFixture.Pump();
            Thread.Sleep(10);
        }
        UiFixture.Pump();
    }

    private sealed class FakeWebHost : IClientWebHost
    {
        public Control View { get; } = new Panel { Name = "fakeClientPage", BackColor = Color.Black };
        public Exception? Failure { get; init; }
        public bool Initialized { get; private set; }
        public bool Disposed { get; private set; }
        public List<string> Sent { get; } = [];
        public event Action? Failed;
        public Task InitializeAsync()
        {
            if (Failure is not null) return Task.FromException(Failure);
            Initialized = true;
            return Task.CompletedTask;
        }
        public void Show(string json) => Sent.Add(json);
        public void Crash() => Failed?.Invoke();
        public void Dispose() { Disposed = true; View.Dispose(); }
    }

    internal sealed class FakeCentralClient : ICentralClient
    {
        public IReadOnlySet<string> Features { get; init; } = new HashSet<string>();
        public bool FailSync { get; init; }
        public int UnpairCalls { get; private set; }
        public Action? OnUnpair { get; set; }
        public Task<SyncResponse> SynchronizeAsync(CancellationToken cancellationToken) => FailSync
            ? throw new CentralException("Sem conexão.")
            : Task.FromResult(new SyncResponse { Protocolo = 1, ServidorEm = DateTimeOffset.UtcNow, Revogado = false, Cadastros = [], Comando = null });
        public Task CompleteAsync(EnrollmentCompletion completion, CancellationToken cancellationToken) => throw new AssertionException("A UI não conclui cadastros.");
        public Task<DecisionResponse> IdentifyAsync(Guid operation, Guid enrollment, DateTimeOffset capturedAt, CancellationToken cancellationToken) => throw new AssertionException("A UI não identifica.");
        public Task ConfirmRemovalsAsync(IReadOnlyCollection<Guid> ids, bool all, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> UnpairAsync(CancellationToken cancellationToken)
        {
            if (!Features.Contains(CentralClient.FeatureUnpair)) return Task.FromResult(false);
            OnUnpair?.Invoke();
            UnpairCalls++;
            return Task.FromResult(true);
        }
        public void Dispose() { }
    }
}

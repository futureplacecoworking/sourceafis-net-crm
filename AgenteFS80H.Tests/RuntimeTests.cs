using Futureplace.AgenteFS80H;
using NUnit.Framework;

namespace AgenteFS80H.Tests;

public class RuntimeTests
{
    [Test]
    public async Task RevocationDeletesLocalDataBeforeAcknowledgementAndBeforeAnyCapture()
    {
        var id = Guid.NewGuid();
        var store = new MemoryStore();
        store.SaveTemplate(new StoredTemplate(id, Guid.NewGuid(), [7]));
        var api = new FakeCentral { Sync = ActiveSync([]) };
        api.OnConfirm = () => Assert.That(store.LoadTemplates(), Is.Empty);
        var scanner = new SecurityAndCaptureTests.FakeScanner([false]);
        using var runtime = Create(store, api, scanner);
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(store.LoadTemplates(), Is.Empty);
        Assert.That(api.Confirmed, Is.EqualTo(new[] { id }));
        Assert.That(scanner.Captures, Is.Zero);
    }

    [Test]
    public async Task RevokedDeviceRemovesEveryTemplateAndAcknowledgesTotalRemoval()
    {
        var store = new MemoryStore();
        store.SaveTemplate(new StoredTemplate(Guid.NewGuid(), Guid.NewGuid(), [7]));
        var api = new FakeCentral { Sync = new SyncResponse { Protocolo = 1, Revogado = true, Cadastros = [], Comando = null } };
        var scanner = new SecurityAndCaptureTests.FakeScanner([true]);
        using var runtime = Create(store, api, scanner);
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(store.LoadTemplates(), Is.Empty);
        Assert.That(api.ConfirmedAll, Is.True);
        Assert.That(runtime.Snapshot.Status, Is.EqualTo(AgentStatus.Revoked));
        Assert.That(scanner.Captures, Is.Zero);
    }

    [Test]
    public void OfflineCycleNeverCapturesOrQueuesIdentification()
    {
        var store = new MemoryStore();
        var api = new FakeCentral { FailSync = true };
        var scanner = new SecurityAndCaptureTests.FakeScanner([true]);
        using var runtime = Create(store, api, scanner);
        Assert.ThrowsAsync<CentralException>(async () => await runtime.RunCycleAsync(CancellationToken.None));
        Assert.That(scanner.Captures, Is.Zero);
        Assert.That(api.Identifications, Is.Zero);
    }

    [Test]
    public async Task SavedEnrollmentRetriesCompletionWithoutScanningAgain()
    {
        var id = Guid.NewGuid();
        var command = Guid.NewGuid();
        var store = new MemoryStore();
        store.SaveTemplate(new StoredTemplate(id, command, [7]) { ExtraTemplates = [[8], [9], [10]] });
        var api = new FakeCentral { Sync = ActiveSync([id], Command(command, id)) };
        var scanner = new SecurityAndCaptureTests.FakeScanner([true]);
        using var runtime = Create(store, api, scanner);
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(api.Completed, Is.EqualTo(command));
        Assert.That(api.Completions.Single(), Is.EqualTo(new EnrollmentCompletion(command, "cadastrado", Readings: 4)));
        Assert.That(scanner.Captures, Is.Zero);
    }

    [Test]
    public async Task FailedRemovalAcknowledgementRemainsDurableForRetry()
    {
        var id = Guid.NewGuid();
        var store = new MemoryStore();
        store.SaveTemplate(new StoredTemplate(id, Guid.NewGuid(), [7]));
        var api = new FakeCentral { Sync = ActiveSync([]), FailConfirm = true };
        using var runtime = Create(store, api, new SecurityAndCaptureTests.FakeScanner([false]));
        Assert.ThrowsAsync<CentralException>(async () => await runtime.RunCycleAsync(CancellationToken.None));
        Assert.That(store.LoadTemplates(), Is.Empty);
        Assert.That(store.LoadState().PendingRemovals, Contains.Item(id));
        api.FailConfirm = false;
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(store.LoadState().PendingRemovals, Is.Empty);
    }

    [Test]
    public async Task BulkRemovalAcknowledgesAtMost400AndPersistsUnacknowledgedRemainder()
    {
        var store = new MemoryStore();
        var ids = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in ids) store.SaveTemplate(new StoredTemplate(id, Guid.NewGuid(), [7]));
        var api = new FakeCentral { Sync = ActiveSync([]), FailConfirmAtCall = 2 };
        using var runtime = Create(store, api, new SecurityAndCaptureTests.FakeScanner([false]));
        Assert.ThrowsAsync<CentralException>(async () => await runtime.RunCycleAsync(CancellationToken.None));
        Assert.That(store.LoadTemplates(), Is.Empty);
        Assert.That(api.Confirmed, Has.Count.EqualTo(400));
        Assert.That(store.LoadState().PendingRemovals, Has.Count.EqualTo(101));
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(api.Confirmed, Is.EquivalentTo(ids));
        Assert.That(store.LoadState().PendingRemovals, Is.Empty);
    }

    [Test]
    public async Task ConsistentUniqueDigitalRequiresCurrentServerAuthorization()
    {
        var id = Guid.NewGuid();
        var store = new MemoryStore();
        store.SaveTemplate(new StoredTemplate(id, Guid.NewGuid(), [7]));
        var api = new FakeCentral { Sync = ActiveSync([id]), Decision = new() { Autorizado = true, Nome = "Pessoa", Motivo = "permitido", PortaAcionada = false } };
        using var runtime = Create(store, api, new SecurityAndCaptureTests.FakeScanner([false, true]));
        var signals = new List<AgentSignal>();
        runtime.Signaled += signals.Add;
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(api.Identifications, Is.EqualTo(1));
        Assert.That(runtime.Snapshot.Status, Is.EqualTo(AgentStatus.Authorized));
        Assert.That(runtime.Snapshot.Message, Does.Contain("Nenhuma porta foi acionada"));
        Assert.That(runtime.Snapshot.Identification, Is.EqualTo(new IdentificationProgress
        {
            Completed = 4, Result = IdentificationResult.Authorized, Motivo = "permitido", Name = "Pessoa"
        }));
        Assert.That(signals, Is.EqualTo(new[] { AgentSignal.Positive }));
    }

    [Test]
    public async Task IdentificationReportsEachStepBeforeTheCentralDecides()
    {
        var id = Guid.NewGuid();
        var store = new MemoryStore();
        store.SaveTemplate(new StoredTemplate(id, Guid.NewGuid(), [7]));
        var display = new DisplayInfo { Nome = "Ana Souza", PrimeiroNome = "Ana", Sala = "Milão" };
        var api = new FakeCentral
        {
            Sync = ActiveSync([id]) with { FechaduraInstalada = false },
            Decision = new() { Autorizado = false, Nome = "Ana Souza", Motivo = "fora_do_horario", PortaAcionada = false, Display = display }
        };
        using var runtime = Create(store, api, new SecurityAndCaptureTests.FakeScanner([false, true]));
        var steps = new List<IdentificationProgress>();
        var signals = new List<AgentSignal>();
        runtime.Changed += snapshot => { if (snapshot.Identification is { } progress) steps.Add(progress); };
        runtime.Signaled += signals.Add;
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(steps.Select(s => (s.Completed, s.Result)), Is.EqualTo(new[]
        {
            (0, IdentificationResult.InProgress), (1, IdentificationResult.InProgress), (2, IdentificationResult.InProgress), (2, IdentificationResult.Denied)
        }));
        var last = steps.Last();
        Assert.That(last.FailedStep, Is.EqualTo(3));
        Assert.That(last.Motivo, Is.EqualTo("fora_do_horario"));
        Assert.That(last.Display, Is.EqualTo(display));
        Assert.That(runtime.Snapshot.Status, Is.EqualTo(AgentStatus.Denied));
        Assert.That(signals, Is.EqualTo(new[] { AgentSignal.Negative }));
    }

    [Test]
    public async Task EachEnrollmentIsScoredByItsBestReadingBeforeAskingTheCentral()
    {
        var ana = Guid.NewGuid();
        var bruno = Guid.NewGuid();
        var store = new MemoryStore();
        store.SaveTemplate(new StoredTemplate(ana, Guid.NewGuid(), [1]) { ExtraTemplates = [[2]] });
        store.SaveTemplate(new StoredTemplate(bruno, Guid.NewGuid(), [3]));
        var api = new FakeCentral { Sync = ActiveSync([ana, bruno]), Decision = new() { Autorizado = true, Nome = "Ana", Motivo = "permitido", PortaAcionada = false } };
        var recognizer = new SecurityAndCaptureTests.FakeRecognizer { ScoreOf = (_, candidate) => candidate switch { 1 => 20, 2 => 85, _ => 50 } };
        using var runtime = new AgentRuntime(store, _ => api, _ => new SecurityAndCaptureTests.FakeScanner([false, true]), recognizer, TimeProvider.System);
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(api.IdentifiedEnrollments, Is.EqualTo(new[] { ana }));
        Assert.That(store.LoadTemplates().SelectMany(t => t.AllTemplates()).All(t => t[0] != 0), Is.True, "A comparação não pode apagar o que está guardado.");
    }

    [Test]
    public async Task TwoDifferentPeopleWithCloseScoresAreNeverIdentified()
    {
        var ana = Guid.NewGuid();
        var bruno = Guid.NewGuid();
        var store = new MemoryStore();
        store.SaveTemplate(new StoredTemplate(ana, Guid.NewGuid(), [1]) { ExtraTemplates = [[2]] });
        store.SaveTemplate(new StoredTemplate(bruno, Guid.NewGuid(), [3]));
        var api = new FakeCentral { Sync = ActiveSync([ana, bruno]) };
        var recognizer = new SecurityAndCaptureTests.FakeRecognizer { ScoreOf = (_, candidate) => candidate switch { 1 => 30, 2 => 85, _ => 80 } };
        using var runtime = new AgentRuntime(store, _ => api, _ => new SecurityAndCaptureTests.FakeScanner([false, true]), recognizer, TimeProvider.System);
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(api.Identifications, Is.Zero);
        Assert.That(runtime.Snapshot.Identification?.Result, Is.EqualTo(IdentificationResult.NotRecognized));
        Assert.That(runtime.Snapshot.Identification?.FailedStep, Is.EqualTo(2));
    }

    [Test]
    public async Task LocalMatchCannotOverrideServerDenial()
    {
        var id = Guid.NewGuid();
        var store = new MemoryStore();
        store.SaveTemplate(new StoredTemplate(id, Guid.NewGuid(), [7]));
        var api = new FakeCentral { Sync = ActiveSync([id]) };
        using var runtime = Create(store, api, new SecurityAndCaptureTests.FakeScanner([false, true]));
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(api.Identifications, Is.EqualTo(1));
        Assert.That(runtime.Snapshot.Status, Is.EqualTo(AgentStatus.Denied));
    }

    [Test]
    public async Task TheResultStaysOnScreenWhileTheFingerIsStillOnTheReader()
    {
        var id = Guid.NewGuid();
        var store = new MemoryStore();
        store.SaveTemplate(new StoredTemplate(id, Guid.NewGuid(), [7]));
        var api = new FakeCentral { Sync = ActiveSync([id]), Decision = new() { Autorizado = true, Nome = "Pessoa", Motivo = "permitido", PortaAcionada = false } };
        var scanner = new SecurityAndCaptureTests.FakeScanner([false, true, true, true]);
        using var runtime = Create(store, api, scanner);
        await runtime.RunCycleAsync(CancellationToken.None);
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(runtime.Snapshot.Status, Is.EqualTo(AgentStatus.Authorized), "O dedo parado no leitor não apaga o cartão de quem entrou.");
        Assert.That(api.Identifications, Is.EqualTo(1), "Sem retirar o dedo não há nova identificação.");
    }

    [Test]
    public async Task TimeoutAfterAuthorizationImmediatelyReplacesAuthorizedStatus()
    {
        var id = Guid.NewGuid();
        var store = new MemoryStore();
        store.SaveTemplate(new StoredTemplate(id, Guid.NewGuid(), [7]));
        var api = new FakeCentral { Sync = ActiveSync([id]), Decision = new() { Autorizado = true, Nome = "Pessoa", Motivo = "permitido", PortaAcionada = false } };
        using var runtime = Create(store, api, new SecurityAndCaptureTests.FakeScanner([false, true]));
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(runtime.Snapshot.Status, Is.EqualTo(AgentStatus.Authorized));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        runtime.Changed += snapshot => { if (snapshot.Status == AgentStatus.Offline) stop.Cancel(); };
        api.ThrowTimeout = true;
        await runtime.RunAsync(stop.Token);
        Assert.That(runtime.Snapshot.Status, Is.EqualTo(AgentStatus.Offline));
        Assert.That(api.Identifications, Is.EqualTo(1));
    }

    [Test]
    public async Task ASlowCentralCannotMakeAnOldCaptureAppearFresh()
    {
        var id = Guid.NewGuid();
        var store = new MemoryStore();
        store.SaveTemplate(new StoredTemplate(id, Guid.NewGuid(), [7]));
        var clock = new ManualClock();
        var calls = 0;
        var api = new FakeCentral
        {
            // A segunda consulta (depois da leitura) demora 30 segundos.
            OnSync = () => { if (++calls == 2) clock.Advance(TimeSpan.FromSeconds(30)); return ActiveSync([id], serverTime: clock.GetUtcNow()); }
        };
        using var runtime = new AgentRuntime(store, _ => api, _ => new SecurityAndCaptureTests.FakeScanner([false, true]), new SecurityAndCaptureTests.FakeRecognizer(), clock);
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(api.Identifications, Is.Zero);
        Assert.That(runtime.Snapshot.Status, Is.EqualTo(AgentStatus.Denied));
    }

    [Test]
    public async Task DeviceRevokedWhileWaitingForFingerRemovesDataAndNeverCaptures()
    {
        var id = Guid.NewGuid();
        var store = new MemoryStore();
        store.SaveTemplate(new StoredTemplate(id, Guid.NewGuid(), [7]));
        var calls = 0;
        var api = new FakeCentral
        {
            OnSync = () => ++calls == 1 ? ActiveSync([id]) : new SyncResponse { Protocolo = 1, Revogado = true, Cadastros = [], Comando = null }
        };
        // Primeiro ciclo: leitor livre, esperando o dedo. Segundo ciclo: a Central revogou.
        var scanner = new SecurityAndCaptureTests.FakeScanner([false, false, true, true]);
        using var runtime = Create(store, api, scanner);
        await runtime.RunCycleAsync(CancellationToken.None);
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(store.LoadTemplates(), Is.Empty);
        Assert.That(scanner.Captures, Is.Zero);
        Assert.That(api.Identifications, Is.Zero);
        Assert.That(api.ConfirmedAll, Is.True);
        Assert.That(runtime.Snapshot.Status, Is.EqualTo(AgentStatus.Revoked));
    }

    [Test]
    public async Task RevocationBetweenCaptureAndDecisionNeverSendsTheIdentification()
    {
        var id = Guid.NewGuid();
        var store = new MemoryStore();
        store.SaveTemplate(new StoredTemplate(id, Guid.NewGuid(), [7]));
        var calls = 0;
        var api = new FakeCentral
        {
            OnSync = () => ++calls == 1 ? ActiveSync([id]) : new SyncResponse { Protocolo = 1, Revogado = true, Cadastros = [], Comando = null }
        };
        using var runtime = Create(store, api, new SecurityAndCaptureTests.FakeScanner([false, true]));
        Assert.ThrowsAsync<CentralException>(async () => await runtime.RunCycleAsync(CancellationToken.None));
        Assert.That(store.LoadTemplates(), Is.Empty);
        Assert.That(api.Identifications, Is.Zero);
        Assert.That(api.ConfirmedAll, Is.True);
        await Task.CompletedTask;
    }

    [Test]
    public async Task EnrollmentCollectsTheRequestedReadingsRetriesABadOneAndStoresEveryGoodTemplate()
    {
        var cadastro = Guid.NewGuid();
        var command = Guid.NewGuid();
        var store = new MemoryStore();
        var api = new FakeCentral { Sync = ActiveSync([cadastro], Command(command, cadastro, readings: 4)) };
        var scanner = new SecurityAndCaptureTests.AlternatingScanner();
        var recognizer = new SecurityAndCaptureTests.FakeRecognizer { Sequential = true, ScoreOf = (a, b) => a == 3 || b == 3 ? 5 : 80 };
        using var runtime = new AgentRuntime(store, _ => api, _ => scanner, recognizer, TimeProvider.System, TimeSpan.Zero);
        var progress = new List<EnrollmentProgress>();
        var signals = new List<AgentSignal>();
        runtime.Changed += snapshot => { if (snapshot.Enrollment is { } value) progress.Add(value); };
        runtime.Signaled += signals.Add;
        await runtime.RunCycleAsync(CancellationToken.None);
        var saved = store.LoadTemplates().Single();
        Assert.That(saved.CadastroId, Is.EqualTo(cadastro));
        Assert.That(saved.ComandoId, Is.EqualTo(command));
        Assert.That(saved.AllTemplates().Select(t => t[0]), Is.EqualTo(new byte[] { 1, 2, 4, 5 }));
        Assert.That(api.Completions.Single(), Is.EqualTo(new EnrollmentCompletion(command, "cadastrado", Readings: 4)));
        Assert.That(progress, Has.Some.Matches<EnrollmentProgress>(p => p.Phase == EnrollmentPhase.BadReading && p.Reading == 3 && p.Total == 4 && p.Good == 2));
        Assert.That(progress.Last().Phase, Is.EqualTo(EnrollmentPhase.Completed));
        Assert.That(progress.All(p => p.Name == "Ana Souza" && p.Company == "Inova Tecnologia"), Is.True);
        Assert.That(runtime.Snapshot.Status, Is.EqualTo(AgentStatus.Ready));
        Assert.That(signals, Is.EqualTo(new[] { AgentSignal.Positive }));
    }

    [Test]
    public async Task OldCentralWithoutReadingCountStillUsesTwoReadings()
    {
        var cadastro = Guid.NewGuid();
        var command = Guid.NewGuid();
        var store = new MemoryStore();
        var api = new FakeCentral { Sync = ActiveSync([cadastro], Command(command, cadastro, readings: null)) };
        var scanner = new SecurityAndCaptureTests.AlternatingScanner();
        using var runtime = new AgentRuntime(store, _ => api, _ => scanner, new SecurityAndCaptureTests.FakeRecognizer { Sequential = true }, TimeProvider.System, TimeSpan.Zero);
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(scanner.Captures, Is.EqualTo(2));
        Assert.That(store.LoadTemplates().Single().Count, Is.EqualTo(2));
        Assert.That(api.Completions.Single().Readings, Is.EqualTo(2));
    }

    [Test]
    public async Task ATransientCaptureErrorDuringEnrollmentAsksAgainInsteadOfFailing()
    {
        var cadastro = Guid.NewGuid();
        var command = Guid.NewGuid();
        var store = new MemoryStore();
        var api = new FakeCentral { Sync = ActiveSync([cadastro], Command(command, cadastro, readings: 2)) };
        var scanner = new SecurityAndCaptureTests.AlternatingScanner { FailCaptures = [1] };
        using var runtime = new AgentRuntime(store, _ => api, _ => scanner, new SecurityAndCaptureTests.FakeRecognizer { Sequential = true }, TimeProvider.System, TimeSpan.Zero);
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(api.Completions.Single().Result, Is.EqualTo("cadastrado"));
        Assert.That(scanner.Captures, Is.EqualTo(3));
        Assert.That(store.LoadTemplates(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AFingerAlreadyEnrolledFailsAsDuplicateNamingTheOtherEnrollment()
    {
        var other = Guid.NewGuid();
        var cadastro = Guid.NewGuid();
        var command = Guid.NewGuid();
        var store = new MemoryStore();
        store.SaveTemplate(new StoredTemplate(other, Guid.NewGuid(), [9]));
        var api = new FakeCentral { Sync = ActiveSync([other, cadastro], Command(command, cadastro, readings: 4)) };
        var scanner = new SecurityAndCaptureTests.AlternatingScanner();
        var recognizer = new SecurityAndCaptureTests.FakeRecognizer { Sequential = true, ScoreOf = (_, candidate) => candidate == 9 ? 90 : 80 };
        using var runtime = new AgentRuntime(store, _ => api, _ => scanner, recognizer, TimeProvider.System, TimeSpan.Zero);
        var signals = new List<AgentSignal>();
        runtime.Signaled += signals.Add;
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(api.Completions.Single(), Is.EqualTo(new EnrollmentCompletion(command, "falhou", "duplicada", other)));
        Assert.That(scanner.Captures, Is.EqualTo(1), "A digital repetida para o cadastro logo na primeira leitura.");
        Assert.That(store.LoadTemplates().Select(t => t.CadastroId), Is.EqualTo(new[] { other }));
        Assert.That(runtime.Snapshot.Enrollment?.Phase, Is.EqualTo(EnrollmentPhase.Failed));
        Assert.That(runtime.Snapshot.Enrollment?.Motivo, Is.EqualTo("duplicada"));
        Assert.That(signals, Is.EqualTo(new[] { AgentSignal.Negative }));
    }

    [Test]
    public async Task TooManyBadReadingsFailWithCaptureReason()
    {
        var cadastro = Guid.NewGuid();
        var command = Guid.NewGuid();
        var store = new MemoryStore();
        var api = new FakeCentral { Sync = ActiveSync([cadastro], Command(command, cadastro, readings: 4)) };
        var scanner = new SecurityAndCaptureTests.AlternatingScanner();
        using var runtime = new AgentRuntime(store, _ => api, _ => scanner, new SecurityAndCaptureTests.FakeRecognizer { Sequential = true, ScoreValue = 3 }, TimeProvider.System, TimeSpan.Zero);
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(api.Completions.Single(), Is.EqualTo(new EnrollmentCompletion(command, "falhou", "captura")));
        Assert.That(scanner.Captures, Is.EqualTo(EnrollmentCollector.MaxAttempts));
        Assert.That(store.LoadTemplates(), Is.Empty);
    }

    [Test]
    public async Task ACommandCancelledInTheCentralStopsWithoutConcludingOrSaving()
    {
        var cadastro = Guid.NewGuid();
        var command = Guid.NewGuid();
        var store = new MemoryStore();
        var calls = 0;
        var api = new FakeCentral
        {
            // A Central cancela o pedido enquanto a pessoa faz as leituras.
            OnSync = () => ++calls <= 2 ? ActiveSync([cadastro], Command(command, cadastro, readings: 2)) : ActiveSync([])
        };
        var scanner = new SecurityAndCaptureTests.AlternatingScanner();
        using var runtime = new AgentRuntime(store, _ => api, _ => scanner, new SecurityAndCaptureTests.FakeRecognizer { Sequential = true }, TimeProvider.System, TimeSpan.Zero);
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(api.Completions, Is.Empty, "Cancelado na Central: nada de concluir.");
        Assert.That(store.LoadTemplates(), Is.Empty);
        Assert.That(runtime.Snapshot.Enrollment?.Phase, Is.EqualTo(EnrollmentPhase.Cancelled));
        Assert.That(runtime.Snapshot.Message, Does.Contain("Cadastro cancelado na Central"));
    }

    [Test]
    public async Task AnExpiredCommandIsReportedWithoutAskingForTheFinger()
    {
        var cadastro = Guid.NewGuid();
        var command = Guid.NewGuid();
        var api = new FakeCentral { Sync = ActiveSync([cadastro], Command(command, cadastro, expiresIn: TimeSpan.FromSeconds(-1))) };
        var scanner = new SecurityAndCaptureTests.AlternatingScanner();
        using var runtime = new AgentRuntime(new MemoryStore(), _ => api, _ => scanner, new SecurityAndCaptureTests.FakeRecognizer(), TimeProvider.System, TimeSpan.Zero);
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(api.Completions.Single(), Is.EqualTo(new EnrollmentCompletion(command, "falhou", "expirado")));
        Assert.That(scanner.Captures, Is.Zero);
    }

    [Test]
    public async Task AFailureThatCouldNotReachTheCentralIsResentWithoutReadingAgain()
    {
        var cadastro = Guid.NewGuid();
        var command = Guid.NewGuid();
        var api = new FakeCentral { Sync = ActiveSync([cadastro], Command(command, cadastro, readings: 4)), FailCompleteCalls = 1 };
        var scanner = new SecurityAndCaptureTests.AlternatingScanner();
        using var runtime = new AgentRuntime(new MemoryStore(), _ => api, _ => scanner, new SecurityAndCaptureTests.FakeRecognizer { Sequential = true, ScoreValue = 3 }, TimeProvider.System, TimeSpan.Zero);
        Assert.ThrowsAsync<CentralException>(async () => await runtime.RunCycleAsync(CancellationToken.None));
        var captures = scanner.Captures;
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(api.Completions.Single(), Is.EqualTo(new EnrollmentCompletion(command, "falhou", "captura")));
        Assert.That(scanner.Captures, Is.EqualTo(captures));
    }

    [Test]
    public async Task ClearingThePairingDeletesTemplatesBeforeTellingTheCentralWithTheOldBadge()
    {
        var store = new MemoryStore();
        store.SaveTemplate(new StoredTemplate(Guid.NewGuid(), Guid.NewGuid(), [7]));
        var api = new FakeCentral { Sync = ActiveSync([]), Features = new HashSet<string> { CentralClient.FeatureUnpair } };
        AgentConfiguration? usedConfiguration = null;
        api.OnUnpair = () => Assert.That(store.LoadTemplates(), Is.Empty, "As digitais saem antes de avisar a Central.");
        var before = store.LoadState().Configuration with { SoundEnabled = false };
        store.SaveState(store.LoadState() with { Configuration = before });
        using var runtime = new AgentRuntime(store, config => { usedConfiguration = config; return api; }, _ => new SecurityAndCaptureTests.FakeScanner([]), new SecurityAndCaptureTests.FakeRecognizer(), TimeProvider.System);
        var notice = await runtime.ResetPairingAsync(CancellationToken.None);
        Assert.That(notice, Is.EqualTo(UnpairNotice.Confirmed));
        Assert.That(api.UnpairCalls, Is.EqualTo(1));
        Assert.That(usedConfiguration?.DeviceToken, Is.EqualTo(before.DeviceToken), "O aviso usa o crachá antigo.");
        Assert.That(runtime.Configuration.IsPaired, Is.False);
        Assert.That(runtime.Configuration.SoundEnabled, Is.False, "A preferência de som continua.");
        Assert.That(runtime.Configuration.ScannerDllPath, Is.EqualTo(before.ScannerDllPath));
        Assert.That(store.LoadTemplates(), Is.Empty);
    }

    [Test]
    public async Task ClearingWithTheOldCentralOrOfflineStillClearsAndAsksToRevokeManually()
    {
        foreach (var api in new[] { new FakeCentral { Sync = ActiveSync([]) }, new FakeCentral { FailSync = true } })
        {
            var store = new MemoryStore();
            store.SaveTemplate(new StoredTemplate(Guid.NewGuid(), Guid.NewGuid(), [7]));
            using var runtime = new AgentRuntime(store, _ => api, _ => new SecurityAndCaptureTests.FakeScanner([]), new SecurityAndCaptureTests.FakeRecognizer(), TimeProvider.System);
            var notice = await runtime.ResetPairingAsync(CancellationToken.None);
            Assert.That(notice, Is.EqualTo(UnpairNotice.Unavailable));
            Assert.That(api.UnpairCalls, Is.Zero, "A Central antiga não conhece desparear.");
            Assert.That(store.LoadTemplates(), Is.Empty);
            Assert.That(runtime.Configuration.IsPaired, Is.False);
            Assert.That(runtime.Snapshot.Message, Does.Contain("Digitais › Central biométrica"));
        }
    }

    [Test]
    public async Task TheReaderNameFromTheCentralIsRememberedOnlyWhenItChanges()
    {
        var store = new MemoryStore();
        var api = new FakeCentral { Sync = ActiveSync([]) with { Dispositivo = new DeviceInfo { Nome = "Recepção — FS80H" } } };
        using var runtime = Create(store, api, new SecurityAndCaptureTests.FakeScanner([]));
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(runtime.Configuration.DeviceName, Is.EqualTo("Recepção — FS80H"));
        Assert.That(store.LoadState().Configuration.DeviceName, Is.EqualTo("Recepção — FS80H"));
        var writes = store.Writes;
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(store.Writes, Is.EqualTo(writes));
        Assert.That(runtime.Snapshot.LastContactAt, Is.Not.Null);
    }

    [Test]
    public void SoundPreferenceIsSavedWithoutTouchingThePairing()
    {
        var store = new MemoryStore();
        using var runtime = Create(store, new FakeCentral(), new SecurityAndCaptureTests.FakeScanner([]));
        Assert.That(runtime.Configuration.SoundEnabled, Is.True);
        var token = runtime.Configuration.DeviceToken;
        runtime.SetSoundEnabled(false);
        Assert.That(store.LoadState().Configuration.SoundEnabled, Is.False);
        Assert.That(store.LoadState().Configuration.DeviceToken, Is.EqualTo(token));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }

    [Test]
    public async Task FailedPairingPersistenceCannotReuseThePreviousDeviceClient()
    {
        var store = new MemoryStore();
        var api = new FakeCentral { Sync = ActiveSync([]) };
        using var runtime = Create(store, api, new SecurityAndCaptureTests.FakeScanner([false]));
        await runtime.RunCycleAsync(CancellationToken.None);
        store.FailSaves = true;
        Assert.Throws<LocalDataException>(() => runtime.ReplacePairing(new AgentConfiguration
        {
            ProjectUrl = "https://other.supabase.co", DeviceId = Guid.NewGuid(), DeviceToken = "new-opaque-token"
        }));
        Assert.That(runtime.Configuration.IsPaired, Is.False);
        Assert.ThrowsAsync<LocalDataException>(async () => await runtime.RunCycleAsync(CancellationToken.None));
        Assert.That(api.Identifications, Is.Zero);
    }

    private static AgentRuntime Create(MemoryStore store, FakeCentral api, IFingerprintScanner scanner) => new(store, _ => api, _ => scanner, new SecurityAndCaptureTests.FakeRecognizer(), TimeProvider.System);
    private static SyncResponse ActiveSync(Guid[] ids, EnrollmentCommand? command = null, DateTimeOffset? serverTime = null) =>
        new() { Protocolo = 1, ServidorEm = serverTime ?? DateTimeOffset.UtcNow, Revogado = false, Cadastros = ids, Comando = command };
    private static EnrollmentCommand Command(Guid id, Guid cadastro, int? readings = null, TimeSpan? expiresIn = null) => new()
    {
        Id = id, CadastroId = cadastro, Tipo = "cadastrar", Nome = "Ana Souza", Empresa = "Inova Tecnologia", Leituras = readings,
        ExpiraEm = DateTimeOffset.UtcNow + (expiresIn ?? TimeSpan.FromMinutes(10))
    };

    internal sealed class MemoryStore : ILocalStore
    {
        private AgentState state = new() { Configuration = new AgentConfiguration { CentralUrl = "https://central.example", ProjectUrl = "https://project.supabase.co", PublicApiKey = "sb_publishable_test", DeviceToken = "test-token", DeviceId = Guid.NewGuid(), ScannerDllPath = "scanner.dll" } };
        private readonly Dictionary<Guid, StoredTemplate> templates = [];
        public bool FailSaves { get; set; }
        public int Writes { get; private set; }
        public AgentState LoadState() => state;
        public void SaveState(AgentState value)
        {
            if (FailSaves) throw new IOException("Synthetic disk failure.");
            Writes++;
            state = value;
        }
        // Cópias, como o armazenamento de verdade: zerar o que foi lido não apaga o que está guardado.
        public IReadOnlyList<StoredTemplate> LoadTemplates() => templates.Values.Select(Copy).ToArray();
        public IReadOnlyList<Guid> ListTemplateIds() => templates.Keys.ToArray();
        public void DeleteAllTemplates() => templates.Clear();
        public void SaveTemplate(StoredTemplate value) => templates[value.CadastroId] = Copy(value);
        public void DeleteTemplate(Guid id) => templates.Remove(id);
        public void Clear() { templates.Clear(); state = new(); }
        private static StoredTemplate Copy(StoredTemplate value) => value with
        {
            Template = (byte[])value.Template.Clone(),
            ExtraTemplates = value.ExtraTemplates?.Select(extra => (byte[])extra.Clone()).ToArray()
        };
    }

    internal sealed class FakeCentral : ICentralClient
    {
        public SyncResponse Sync { get; init; } = ActiveSync([]);
        public bool FailSync { get; init; }
        public bool FailConfirm { get; set; }
        public Action? OnConfirm { get; set; }
        public Action? OnUnpair { get; set; }
        public List<Guid> Confirmed { get; } = [];
        public bool ConfirmedAll { get; private set; }
        public Guid? Completed { get; private set; }
        public List<EnrollmentCompletion> Completions { get; } = [];
        public int FailCompleteCalls { get; set; }
        public int Identifications { get; private set; }
        public List<Guid> IdentifiedEnrollments { get; } = [];
        public int ConfirmCalls { get; private set; }
        public int UnpairCalls { get; private set; }
        public int FailConfirmAtCall { get; init; }
        public bool ThrowTimeout { get; set; }
        public Func<SyncResponse>? OnSync { get; init; }
        public IReadOnlySet<string> Features { get; init; } = new HashSet<string>();
        public DecisionResponse Decision { get; init; } = new() { Autorizado = false, Motivo = "horario", Nome = "Pessoa", PortaAcionada = false };
        public Task<SyncResponse> SynchronizeAsync(CancellationToken cancellationToken) => ThrowTimeout ? throw new TaskCanceledException("timeout") : FailSync ? throw new CentralException("Sem conexão.") : Task.FromResult(OnSync?.Invoke() ?? Sync);
        public Task CompleteAsync(EnrollmentCompletion completion, CancellationToken cancellationToken)
        {
            if (FailCompleteCalls > 0)
            {
                FailCompleteCalls--;
                throw new CentralException("Sem conexão.");
            }
            Completed = completion.CommandId;
            Completions.Add(completion);
            return Task.CompletedTask;
        }
        public Task<DecisionResponse> IdentifyAsync(Guid operation, Guid enrollment, DateTimeOffset capturedAt, CancellationToken cancellationToken)
        {
            Identifications++;
            IdentifiedEnrollments.Add(enrollment);
            return Task.FromResult(Decision);
        }
        public Task ConfirmRemovalsAsync(IReadOnlyCollection<Guid> ids, bool all, CancellationToken cancellationToken)
        {
            OnConfirm?.Invoke();
            ConfirmCalls++;
            if (FailConfirm || ConfirmCalls == FailConfirmAtCall) throw new CentralException("Sem conexão.");
            Confirmed.AddRange(ids);
            ConfirmedAll = all;
            return Task.CompletedTask;
        }
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

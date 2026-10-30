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
        store.SaveTemplate(new StoredTemplate(id, command, [7]));
        var api = new FakeCentral { Sync = ActiveSync([id], new EnrollmentCommand { Id = command, CadastroId = id, Tipo = "cadastrar", Nome = "Pessoa", ExpiraEm = DateTimeOffset.UtcNow.AddMinutes(2) }) };
        var scanner = new SecurityAndCaptureTests.FakeScanner([true]);
        using var runtime = Create(store, api, scanner);
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(api.Completed, Is.EqualTo(command));
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
        using var runtime = Create(store, api, new SecurityAndCaptureTests.FakeScanner([false, true, true, false, true, false]));
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(api.Identifications, Is.EqualTo(1));
        Assert.That(runtime.Snapshot.Status, Is.EqualTo(AgentStatus.Authorized));
        Assert.That(runtime.Snapshot.Message, Does.Contain("Nenhuma porta foi acionada"));
    }

    [Test]
    public async Task LocalMatchCannotOverrideServerDenial()
    {
        var id = Guid.NewGuid();
        var store = new MemoryStore();
        store.SaveTemplate(new StoredTemplate(id, Guid.NewGuid(), [7]));
        var api = new FakeCentral { Sync = ActiveSync([id]) };
        using var runtime = Create(store, api, new SecurityAndCaptureTests.FakeScanner([false, true, true, false, true, false]));
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(api.Identifications, Is.EqualTo(1));
        Assert.That(runtime.Snapshot.Status, Is.EqualTo(AgentStatus.Denied));
    }

    [Test]
    public async Task TimeoutAfterAuthorizationImmediatelyReplacesAuthorizedStatus()
    {
        var id = Guid.NewGuid();
        var store = new MemoryStore();
        store.SaveTemplate(new StoredTemplate(id, Guid.NewGuid(), [7]));
        var api = new FakeCentral { Sync = ActiveSync([id]), Decision = new() { Autorizado = true, Nome = "Pessoa", Motivo = "permitido", PortaAcionada = false } };
        using var runtime = Create(store, api, new SecurityAndCaptureTests.FakeScanner([false, true, true, false, true, false]));
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
    public async Task LongFingerLiftCannotMakeAnOldCaptureAppearFresh()
    {
        var id = Guid.NewGuid();
        var store = new MemoryStore();
        store.SaveTemplate(new StoredTemplate(id, Guid.NewGuid(), [7]));
        var api = new FakeCentral { Sync = ActiveSync([id]) };
        var clock = new ManualClock();
        var scanner = new SecurityAndCaptureTests.FakeScanner([false, true, true, false, true, false])
        {
            OnPresence = calls => { if (calls == 6) clock.Advance(TimeSpan.FromSeconds(30)); }
        };
        using var runtime = new AgentRuntime(store, _ => api, _ => scanner, new SecurityAndCaptureTests.FakeRecognizer(), clock);
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
        var scanner = new SecurityAndCaptureTests.FakeScanner([false, true, true, false, true, false]);
        using var runtime = Create(store, api, scanner);
        Assert.ThrowsAsync<CentralException>(async () => await runtime.RunCycleAsync(CancellationToken.None));
        Assert.That(store.LoadTemplates(), Is.Empty);
        Assert.That(scanner.Captures, Is.Zero);
        Assert.That(api.Identifications, Is.Zero);
        Assert.That(api.ConfirmedAll, Is.True);
        await Task.CompletedTask;
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
    private static SyncResponse ActiveSync(Guid[] ids, EnrollmentCommand? command = null) => new() { Protocolo = 1, ServidorEm = DateTimeOffset.UtcNow, Revogado = false, Cadastros = ids, Comando = command };

    internal sealed class MemoryStore : ILocalStore
    {
        private AgentState state = new() { Configuration = new AgentConfiguration { CentralUrl = "https://central.example", ProjectUrl = "https://project.supabase.co", PublicApiKey = "sb_publishable_test", DeviceToken = "test-token", DeviceId = Guid.NewGuid(), ScannerDllPath = "scanner.dll" } };
        private readonly Dictionary<Guid, StoredTemplate> templates = [];
        public bool FailSaves { get; set; }
        public AgentState LoadState() => state;
        public void SaveState(AgentState value)
        {
            if (FailSaves) throw new IOException("Synthetic disk failure.");
            state = value;
        }
        public IReadOnlyList<StoredTemplate> LoadTemplates() => templates.Values.Select(t => t with { Template = (byte[])t.Template.Clone() }).ToArray();
        public IReadOnlyList<Guid> ListTemplateIds() => templates.Keys.ToArray();
        public void DeleteAllTemplates() => templates.Clear();
        public void SaveTemplate(StoredTemplate value) => templates[value.CadastroId] = value with { Template = (byte[])value.Template.Clone() };
        public void DeleteTemplate(Guid id) => templates.Remove(id);
        public void Clear() { templates.Clear(); state = new(); }
    }

    internal sealed class FakeCentral : ICentralClient
    {
        public SyncResponse Sync { get; init; } = ActiveSync([]);
        public bool FailSync { get; init; }
        public bool FailConfirm { get; set; }
        public Action? OnConfirm { get; set; }
        public List<Guid> Confirmed { get; } = [];
        public bool ConfirmedAll { get; private set; }
        public Guid? Completed { get; private set; }
        public int Identifications { get; private set; }
        public int ConfirmCalls { get; private set; }
        public int FailConfirmAtCall { get; init; }
        public bool ThrowTimeout { get; set; }
        public Func<SyncResponse>? OnSync { get; init; }
        public DecisionResponse Decision { get; init; } = new() { Autorizado = false, Motivo = "horario", Nome = "Pessoa", PortaAcionada = false };
        public Task<SyncResponse> SynchronizeAsync(CancellationToken cancellationToken) => ThrowTimeout ? throw new TaskCanceledException("timeout") : FailSync ? throw new CentralException("Sem conexão.") : Task.FromResult(OnSync?.Invoke() ?? Sync);
        public Task CompleteAsync(Guid command, string result, string? reason, CancellationToken cancellationToken) { Completed = command; return Task.CompletedTask; }
        public Task<DecisionResponse> IdentifyAsync(Guid operation, Guid enrollment, DateTimeOffset capturedAt, CancellationToken cancellationToken) { Identifications++; return Task.FromResult(Decision); }
        public Task ConfirmRemovalsAsync(IReadOnlyCollection<Guid> ids, bool all, CancellationToken cancellationToken)
        {
            OnConfirm?.Invoke();
            ConfirmCalls++;
            if (FailConfirm || ConfirmCalls == FailConfirmAtCall) throw new CentralException("Sem conexão.");
            Confirmed.AddRange(ids);
            ConfirmedAll = all;
            return Task.CompletedTask;
        }
        public void Dispose() { }
    }
}

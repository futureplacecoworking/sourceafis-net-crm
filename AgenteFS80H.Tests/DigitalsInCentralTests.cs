using System.Text.Json;
using Futureplace.AgenteFS80H;
using NUnit.Framework;

namespace AgenteFS80H.Tests;

/// <summary>A Central guarda a cópia mestra das digitais; este PC guarda só um cache protegido para comparar rápido.</summary>
public class DigitalsInCentralTests
{
    private static readonly IReadOnlySet<string> WithDigitals = new HashSet<string> { CentralClient.FeatureDigitalsInDatabase };

    private static SyncResponse ActiveSync(params Guid[] ids) =>
        new() { Protocolo = 1, ServidorEm = DateTimeOffset.UtcNow, Revogado = false, Cadastros = ids, Comando = null };

    private static string B64(params byte[] bytes) => Convert.ToBase64String(bytes);

    private static AgentRuntime Create(RuntimeTests.MemoryStore store, RuntimeTests.FakeCentral api, IFingerprintRecognizer? recognizer = null, TimeSpan? interval = null) =>
        new(store, _ => api, _ => new SecurityAndCaptureTests.FakeScanner([false]), recognizer ?? new SecurityAndCaptureTests.FakeRecognizer(), TimeProvider.System,
            digitalSyncInterval: interval ?? TimeSpan.Zero);

    private static async Task CycleAsync(AgentRuntime runtime)
    {
        await runtime.RunCycleAsync(CancellationToken.None);
        await runtime.DigitalSyncTask;
    }

    [Test]
    public async Task CentralWithoutTheFeatureReceivesNoNewCalls()
    {
        var id = Guid.NewGuid();
        var store = new RuntimeTests.MemoryStore();
        store.SaveTemplate(new StoredTemplate(id, Guid.NewGuid(), [7]));
        var api = new RuntimeTests.FakeCentral { Sync = ActiveSync(id), DigitalState = new() { Enviar = [id], Baixar = [Guid.NewGuid()] } };
        using var runtime = Create(store, api);
        await CycleAsync(runtime);
        await CycleAsync(runtime);
        Assert.That(api.DigitalStateCalls, Is.Zero);
        Assert.That(api.Stored, Is.Empty);
        Assert.That(api.DownloadRequests, Is.Empty);
        Assert.That(store.ListTemplateIds(), Is.EqualTo(new[] { id }));
    }

    [Test]
    public async Task UploadsOnlyTheLocalEnrollmentsListedInEnviar()
    {
        var wanted = Guid.NewGuid();
        var missingHere = Guid.NewGuid();
        var notAsked = Guid.NewGuid();
        var store = new RuntimeTests.MemoryStore();
        store.SaveTemplate(new StoredTemplate(wanted, Guid.NewGuid(), [1, 2]) { ExtraTemplates = [[3], [4, 5]] });
        store.SaveTemplate(new StoredTemplate(notAsked, Guid.NewGuid(), [9]));
        var api = new RuntimeTests.FakeCentral
        {
            Sync = ActiveSync(wanted, missingHere, notAsked), Features = WithDigitals,
            DigitalState = new() { Enviar = [wanted, missingHere] }
        };
        using var runtime = Create(store, api);
        await CycleAsync(runtime);
        var sent = api.Stored.Single();
        Assert.That(sent.Id, Is.EqualTo(wanted));
        Assert.That(sent.Readings, Is.EqualTo(new[] { B64(1, 2), B64(3), B64(4, 5) }));
        Assert.That(api.DownloadRequests, Is.Empty);
    }

    [Test]
    public async Task DownloadsOnlyMissingEnrollmentsInBatchesOfAtMost20AndSavesThemForMatching()
    {
        var ids = Enumerable.Range(0, 48).Select(_ => Guid.NewGuid()).ToArray();
        var local = ids.Take(5).ToArray();
        var store = new RuntimeTests.MemoryStore();
        foreach (var id in local) store.SaveTemplate(new StoredTemplate(id, Guid.NewGuid(), [99]));
        var api = new RuntimeTests.FakeCentral { Sync = ActiveSync(ids), Features = WithDigitals, DigitalState = new() { Baixar = ids } };
        foreach (var id in ids) api.Downloadable[id] = [B64(1), B64(2), B64(3)];
        using var runtime = Create(store, api);
        await CycleAsync(runtime);
        Assert.That(api.DownloadRequests.Select(batch => batch.Length), Is.EqualTo(new[] { 20, 20, 3 }));
        Assert.That(api.DownloadRequests.SelectMany(batch => batch), Is.EquivalentTo(ids.Skip(5)));
        Assert.That(store.ListTemplateIds(), Is.EquivalentTo(ids));
        var downloaded = store.LoadTemplates().Single(t => t.CadastroId == ids[10]);
        Assert.That(downloaded.ComandoId, Is.EqualTo(Guid.Empty));
        Assert.That(downloaded.Template, Is.EqualTo(new byte[] { 1 }));
        Assert.That(downloaded.ExtraTemplates, Is.EqualTo(new[] { new byte[] { 2 }, new byte[] { 3 } }));
        Assert.That(store.LoadTemplates().Single(t => t.CadastroId == ids[0]).Template, Is.EqualTo(new byte[] { 99 }), "O que já existe aqui não é sobrescrito.");
    }

    [Test]
    public async Task DownloadedTemplatesAreUsedByTheNextIdentification()
    {
        var id = Guid.NewGuid();
        var store = new RuntimeTests.MemoryStore();
        var api = new RuntimeTests.FakeCentral { Sync = ActiveSync(id), Features = WithDigitals, DigitalState = new() { Baixar = [id] } };
        api.Downloadable[id] = [B64(7)];
        var scanner = new SecurityAndCaptureTests.FakeScanner([false, false, true]);
        using var runtime = new AgentRuntime(store, _ => api, _ => scanner, new SecurityAndCaptureTests.FakeRecognizer(), TimeProvider.System, digitalSyncInterval: TimeSpan.Zero);
        await CycleAsync(runtime);
        await runtime.RunCycleAsync(CancellationToken.None); // dedo no leitor
        Assert.That(api.IdentifiedEnrollments, Is.EqualTo(new[] { id }));
    }

    [Test]
    public async Task InvalidDownloadedTemplatesAreSkipped()
    {
        var mixed = Guid.NewGuid();
        var onlyInvalid = Guid.NewGuid();
        var store = new RuntimeTests.MemoryStore();
        var api = new RuntimeTests.FakeCentral { Sync = ActiveSync(mixed, onlyInvalid), Features = WithDigitals, DigitalState = new() { Baixar = [mixed, onlyInvalid] } };
        api.Downloadable[mixed] = [B64(0xFF), "!!não é base64!!", "", B64(5), B64(6)];
        api.Downloadable[onlyInvalid] = [B64(0xFF)];
        using var runtime = Create(store, api, new PickyRecognizer());
        await CycleAsync(runtime);
        Assert.That(store.ListTemplateIds(), Is.EqualTo(new[] { mixed }));
        var saved = store.LoadTemplates().Single();
        Assert.That(saved.AllTemplates().Select(t => t[0]), Is.EqualTo(new byte[] { 5, 6 }));
    }

    [Test]
    public async Task DownloadedEnrollmentThatIsNoLongerActiveIsNotSaved()
    {
        var active = Guid.NewGuid();
        var revoked = Guid.NewGuid();
        var store = new RuntimeTests.MemoryStore();
        var api = new RuntimeTests.FakeCentral { Sync = ActiveSync(active), Features = WithDigitals, DigitalState = new() { Baixar = [active, revoked] } };
        api.Downloadable[active] = [B64(1)];
        api.Downloadable[revoked] = [B64(2)];
        using var runtime = Create(store, api);
        await CycleAsync(runtime);
        Assert.That(store.ListTemplateIds(), Is.EqualTo(new[] { active }));
    }

    [Test]
    public async Task OversizedReadingIsNeverSent()
    {
        var big = Guid.NewGuid();
        var mixed = Guid.NewGuid();
        var store = new RuntimeTests.MemoryStore();
        // 24577 bytes viram 32772 caracteres em base64: acima do limite de 32768.
        store.SaveTemplate(new StoredTemplate(big, Guid.NewGuid(), new byte[24577]));
        store.SaveTemplate(new StoredTemplate(mixed, Guid.NewGuid(), [1]) { ExtraTemplates = [new byte[24577], [2]] });
        var api = new RuntimeTests.FakeCentral { Sync = ActiveSync(big, mixed), Features = WithDigitals, DigitalState = new() { Enviar = [big, mixed] } };
        using var runtime = Create(store, api);
        await CycleAsync(runtime);
        var sent = api.Stored.Single();
        Assert.That(sent.Id, Is.EqualTo(mixed));
        Assert.That(sent.Readings, Is.EqualTo(new[] { B64(1), B64(2) }));
        Assert.That(sent.Readings.All(text => text.Length <= CentralClient.MaximumReadingBase64Length), Is.True);
    }

    [Test]
    public async Task CentralRefusalDoesNothingDestructiveAndTheNextCycleRetries()
    {
        var id = Guid.NewGuid();
        var store = new RuntimeTests.MemoryStore();
        store.SaveTemplate(new StoredTemplate(id, Guid.NewGuid(), [7]));
        var api = new RuntimeTests.FakeCentral { Sync = ActiveSync(id), Features = WithDigitals, FailDigitals = true, DigitalState = new() { Enviar = [id] } };
        using var runtime = Create(store, api);
        await CycleAsync(runtime);
        Assert.That(api.DigitalStateCalls, Is.EqualTo(1));
        Assert.That(store.ListTemplateIds(), Is.EqualTo(new[] { id }), "Nada local foi apagado.");
        Assert.That(runtime.Snapshot.Status, Is.Not.EqualTo(AgentStatus.Error).And.Not.EqualTo(AgentStatus.Revoked).And.Not.EqualTo(AgentStatus.Offline));
        Assert.That(store.LoadState().DeviceRevoked, Is.False);
        api.FailDigitals = false;
        await CycleAsync(runtime);
        Assert.That(api.DigitalStateCalls, Is.EqualTo(2));
        Assert.That(api.Stored.Single().Id, Is.EqualTo(id));
    }

    [Test]
    public async Task OnlyOneSynchronizationRunsAtATime()
    {
        var store = new RuntimeTests.MemoryStore();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new RuntimeTests.FakeCentral { Sync = ActiveSync(), Features = WithDigitals, OnDigitalState = () => release.Task };
        using var runtime = Create(store, api);
        await runtime.RunCycleAsync(CancellationToken.None);
        var first = runtime.DigitalSyncTask;
        await runtime.RunCycleAsync(CancellationToken.None);
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(runtime.DigitalSyncTask, Is.SameAs(first), "Nenhuma segunda sincronização começou enquanto a primeira não terminou.");
        Assert.That(first.IsCompleted, Is.False);
        release.SetResult();
        await first;
        Assert.That(api.DigitalStateCalls, Is.EqualTo(1));
        await CycleAsync(runtime);
        Assert.That(api.DigitalStateCalls, Is.EqualTo(2));
    }

    [Test]
    public async Task CyclesInsideTheIntervalDoNotCallTheCentralAgain()
    {
        var store = new RuntimeTests.MemoryStore();
        var api = new RuntimeTests.FakeCentral { Sync = ActiveSync(), Features = WithDigitals };
        using var runtime = Create(store, api, interval: TimeSpan.FromMinutes(5));
        await CycleAsync(runtime);
        await CycleAsync(runtime);
        await CycleAsync(runtime);
        Assert.That(api.DigitalStateCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task FreshEnrollmentIsUploadedRightAway()
    {
        var cadastro = Guid.NewGuid();
        var command = Guid.NewGuid();
        var store = new RuntimeTests.MemoryStore();
        var enrollment = new EnrollmentCommand
        {
            Id = command, CadastroId = cadastro, Tipo = "cadastrar", Nome = "Ana Souza", Empresa = "Inova", Leituras = 2, ExpiraEm = DateTimeOffset.UtcNow.AddMinutes(10)
        };
        var api = new RuntimeTests.FakeCentral
        {
            Sync = new SyncResponse { Protocolo = 1, ServidorEm = DateTimeOffset.UtcNow, Revogado = false, Cadastros = [cadastro], Comando = enrollment },
            Features = WithDigitals
        };
        var scanner = new SecurityAndCaptureTests.AlternatingScanner();
        // Intervalo longo: o envio imediato não depende do intervalo.
        using var runtime = new AgentRuntime(store, _ => api, _ => scanner, new SecurityAndCaptureTests.FakeRecognizer { Sequential = true }, TimeProvider.System, TimeSpan.Zero,
            digitalSyncInterval: TimeSpan.FromHours(1));
        await runtime.RunCycleAsync(CancellationToken.None);
        await runtime.DigitalSyncTask;
        Assert.That(api.Completions.Single().Result, Is.EqualTo("cadastrado"));
        var sent = api.Stored.Single();
        Assert.That(sent.Id, Is.EqualTo(cadastro));
        Assert.That(sent.Readings, Is.EqualTo(new[] { B64(1), B64(2) }));
    }

    [Test]
    public async Task EnrollmentOnAnOldCentralDoesNotUpload()
    {
        var cadastro = Guid.NewGuid();
        var command = Guid.NewGuid();
        var store = new RuntimeTests.MemoryStore();
        var enrollment = new EnrollmentCommand
        {
            Id = command, CadastroId = cadastro, Tipo = "cadastrar", Nome = "Ana Souza", Empresa = "Inova", Leituras = 2, ExpiraEm = DateTimeOffset.UtcNow.AddMinutes(10)
        };
        var api = new RuntimeTests.FakeCentral { Sync = new SyncResponse { Protocolo = 1, ServidorEm = DateTimeOffset.UtcNow, Revogado = false, Cadastros = [cadastro], Comando = enrollment } };
        using var runtime = new AgentRuntime(store, _ => api, _ => new SecurityAndCaptureTests.AlternatingScanner(), new SecurityAndCaptureTests.FakeRecognizer { Sequential = true }, TimeProvider.System, TimeSpan.Zero,
            digitalSyncInterval: TimeSpan.Zero);
        await runtime.RunCycleAsync(CancellationToken.None);
        await runtime.DigitalSyncTask;
        Assert.That(api.Completions.Single().Result, Is.EqualTo("cadastrado"));
        Assert.That(api.Stored, Is.Empty);
        Assert.That(api.DigitalStateCalls, Is.Zero);
    }

    [Test]
    public async Task RevokedEnrollmentsAreStillRemovedLocallyByTheNormalFlow()
    {
        var kept = Guid.NewGuid();
        var gone = Guid.NewGuid();
        var store = new RuntimeTests.MemoryStore();
        store.SaveTemplate(new StoredTemplate(kept, Guid.Empty, [1]));
        store.SaveTemplate(new StoredTemplate(gone, Guid.Empty, [2]));
        var api = new RuntimeTests.FakeCentral { Sync = ActiveSync(kept), Features = WithDigitals };
        using var runtime = Create(store, api);
        await CycleAsync(runtime);
        Assert.That(store.ListTemplateIds(), Is.EqualTo(new[] { kept }));
        Assert.That(api.Confirmed, Is.EqualTo(new[] { gone }));
    }

    [Test]
    [Platform("Win")]
    public void ProtectedStoreKeepsTemplatesDownloadedFromTheCentralWithoutACommandId()
    {
        var path = Path.Combine(Path.GetTempPath(), "fs80h-" + Guid.NewGuid());
        try
        {
            var store = new ProtectedLocalStore(path);
            var id = Guid.NewGuid();
            store.SaveTemplate(new StoredTemplate(id, Guid.Empty, [1, 2, 3]) { ExtraTemplates = [[4]] });
            var loaded = store.LoadTemplates().Single();
            Assert.That(loaded.ComandoId, Is.EqualTo(Guid.Empty));
            Assert.That(loaded.AllTemplates().Select(t => t.Length), Is.EqualTo(new[] { 3, 1 }));
            Assert.Throws<ArgumentException>(() => store.SaveTemplate(new StoredTemplate(Guid.Empty, Guid.Empty, [1])));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    // ---- Protocolo (CentralClient) ----

    private static AgentConfiguration Configuration => new()
    {
        ProjectUrl = "https://project.supabase.co", PublicApiKey = "sb_publishable_test", DeviceId = Guid.NewGuid(), DeviceToken = "device-secret"
    };

    private static string Sync(string resources) =>
        "{\"ok\":true,\"protocolo\":1,\"servidor_em\":\"" + DateTimeOffset.UtcNow.ToString("O") + "\",\"revogado\":false,\"cadastros\":[],\"comando\":null,\"recursos\":[" + resources + "]}";

    private static (CentralClient Client, List<(string Action, JsonElement Data)> Requests) Recording(Func<string, string> respond)
    {
        var requests = new List<(string, JsonElement)>();
        var client = new CentralClient(Configuration, new ProtocolTests.Handler(async request =>
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var action = document.RootElement.GetProperty("acao").GetString()!;
            requests.Add((action, document.RootElement.GetProperty("dados").Clone()));
            return ProtocolTests.Json(respond(action));
        }));
        return (client, requests);
    }

    [Test]
    public async Task OldCentralNeverReceivesTheNewActions()
    {
        var (client, requests) = Recording(action => action == "sincronizar" ? Sync("\"versao\"") : "{\"ok\":true}");
        using (client)
        {
            await client.SynchronizeAsync(CancellationToken.None);
            Assert.ThrowsAsync<CentralException>(async () => await client.DigitalStateAsync(CancellationToken.None));
            Assert.ThrowsAsync<CentralException>(async () => await client.StoreDigitalAsync(Guid.NewGuid(), [B64(1)], CancellationToken.None));
            Assert.ThrowsAsync<CentralException>(async () => await client.DownloadDigitalsAsync([Guid.NewGuid()], CancellationToken.None));
        }
        Assert.That(requests.Select(r => r.Action), Is.EqualTo(new[] { "sincronizar" }));
    }

    [Test]
    public async Task NewActionsUseTheAgreedPayloads()
    {
        var one = Guid.NewGuid();
        var two = Guid.NewGuid();
        var (client, requests) = Recording(action => action switch
        {
            "sincronizar" => Sync("\"digitais_no_banco\""),
            "estado_digitais" => "{\"ok\":true,\"enviar\":[\"" + one + "\"],\"baixar\":[\"" + two + "\"]}",
            "baixar_digitais" => "{\"ok\":true,\"digitais\":[{\"cadastro_id\":\"" + two + "\",\"leituras\":[\"AQ==\",\"Ag==\"]}]}",
            _ => "{\"ok\":true}"
        });
        using (client)
        {
            await client.SynchronizeAsync(CancellationToken.None);
            Assert.That(client.Features, Does.Contain(CentralClient.FeatureDigitalsInDatabase));
            var state = await client.DigitalStateAsync(CancellationToken.None);
            Assert.That(state.Enviar, Is.EqualTo(new[] { one }));
            Assert.That(state.Baixar, Is.EqualTo(new[] { two }));
            await client.StoreDigitalAsync(one, ["AQ==", "Ag=="], CancellationToken.None);
            var downloads = await client.DownloadDigitalsAsync([two], CancellationToken.None);
            Assert.That(downloads.Single().CadastroId, Is.EqualTo(two));
            Assert.That(downloads.Single().Leituras, Is.EqualTo(new[] { "AQ==", "Ag==" }));
        }
        Assert.That(requests.Select(r => r.Action), Is.EqualTo(new[] { "sincronizar", "estado_digitais", "guardar_digital", "baixar_digitais" }));
        Assert.That(requests[1].Data.EnumerateObject(), Is.Empty);
        Assert.That(requests[2].Data.EnumerateObject().Select(p => p.Name), Is.EquivalentTo(new[] { "cadastro_id", "leituras" }));
        Assert.That(requests[2].Data.GetProperty("cadastro_id").GetGuid(), Is.EqualTo(one));
        Assert.That(requests[2].Data.GetProperty("leituras").EnumerateArray().Select(e => e.GetString()), Is.EqualTo(new[] { "AQ==", "Ag==" }));
        Assert.That(requests[3].Data.GetProperty("cadastros").EnumerateArray().Select(e => e.GetGuid()), Is.EqualTo(new[] { two }));
    }

    [Test]
    public async Task InvalidRequestsAreRefusedBeforeLeavingTheComputer()
    {
        var (client, requests) = Recording(action => action == "sincronizar" ? Sync("\"digitais_no_banco\"") : "{\"ok\":true}");
        using (client)
        {
            await client.SynchronizeAsync(CancellationToken.None);
            var huge = new string('A', CentralClient.MaximumReadingBase64Length + 1);
            Assert.ThrowsAsync<CentralException>(async () => await client.StoreDigitalAsync(Guid.NewGuid(), [huge], CancellationToken.None));
            Assert.ThrowsAsync<CentralException>(async () => await client.StoreDigitalAsync(Guid.NewGuid(), [], CancellationToken.None));
            Assert.ThrowsAsync<CentralException>(async () => await client.StoreDigitalAsync(Guid.NewGuid(), Enumerable.Repeat("AQ==", 11).ToArray(), CancellationToken.None));
            Assert.ThrowsAsync<CentralException>(async () => await client.DownloadDigitalsAsync(Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToArray(), CancellationToken.None));
            Assert.ThrowsAsync<CentralException>(async () => await client.DownloadDigitalsAsync([], CancellationToken.None));
        }
        Assert.That(requests.Select(r => r.Action), Is.EqualTo(new[] { "sincronizar" }));
    }

    [TestCase("{\"ok\":false,\"motivo\":\"sem_chave\"}")]
    [TestCase("{\"ok\":false,\"erro\":\"Leitor não autenticado\"}")]
    [TestCase("{\"ok\":false,\"erro\":\"Este leitor foi revogado.\"}")]
    public async Task OkFalseBecomesASafeErrorWithoutReflectingTheResponse(string response)
    {
        var (client, _) = Recording(action => action == "sincronizar" ? Sync("\"digitais_no_banco\"") : response);
        using (client)
        {
            await client.SynchronizeAsync(CancellationToken.None);
            var error = Assert.ThrowsAsync<CentralException>(async () => await client.DigitalStateAsync(CancellationToken.None));
            Assert.That(error!.Message, Does.Not.Contain("sem_chave").And.Not.Contain("revogado").And.Not.Contain("autenticado"));
            Assert.ThrowsAsync<CentralException>(async () => await client.StoreDigitalAsync(Guid.NewGuid(), ["AQ=="], CancellationToken.None));
            Assert.ThrowsAsync<CentralException>(async () => await client.DownloadDigitalsAsync([Guid.NewGuid()], CancellationToken.None));
        }
    }

    [Test]
    public async Task DownloadsLargerThanOneMegabyteAreAccepted()
    {
        var id = Guid.NewGuid();
        var reading = new string('A', 30000);
        var items = string.Join(",", Enumerable.Range(0, 40).Select(_ => "\"" + reading + "\""));
        var (client, _) = Recording(action => action == "sincronizar" ? Sync("\"digitais_no_banco\"") : "{\"ok\":true,\"digitais\":[{\"cadastro_id\":\"" + id + "\",\"leituras\":[" + items + "]}]}");
        using (client)
        {
            await client.SynchronizeAsync(CancellationToken.None);
            var downloads = await client.DownloadDigitalsAsync([id], CancellationToken.None);
            Assert.That(downloads.Single().Leituras, Has.Length.EqualTo(40));
        }
    }

    /// <summary>Recusa leituras que começam por 0xFF, como o SourceAFIS recusa um template corrompido.</summary>
    private sealed class PickyRecognizer : IFingerprintRecognizer
    {
        public byte[] Extract(FingerprintFrame image) => [7];
        public IFingerprintMatcher Matcher(byte[] probe) => probe[0] == 0xFF ? throw new FormatException() : new Prepared();
        private sealed class Prepared : IFingerprintMatcher
        {
            public double Score(byte[] candidate) => 80;
        }
    }
}

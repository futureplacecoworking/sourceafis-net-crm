using System.Security.Cryptography;
using System.Text.Json;
using Futureplace.AgenteFS80H;
using NUnit.Framework;

namespace AgenteFS80H.Tests;

public class SecurityAndCaptureTests
{
    [Test]
    public void MatchRequiresBothThresholdAndDistanceFromRunnerUp()
    {
        var one = Guid.NewGuid();
        var two = Guid.NewGuid();
        Assert.That(MatchingPolicy.Select([(one, 39d)]), Is.Null);
        Assert.That(MatchingPolicy.Select([(one, 70d), (two, 65d)]), Is.Null);
        Assert.That(MatchingPolicy.Select([(one, 70d), (two, 40d)]), Is.EqualTo(one));
        Assert.That(MatchingPolicy.Select([(one, double.NaN)]), Is.Null);
    }

    [Test]
    public void EachEnrollmentCountsByItsBestTemplateAndAmbiguityIsOnlyBetweenPeople()
    {
        var ana = Guid.NewGuid();
        var bruno = Guid.NewGuid();
        // Duas leituras da mesma pessoa com notas parecidas não são ambiguidade.
        Assert.That(MatchingPolicy.Select([(ana, 70d), (ana, 68d), (bruno, 20d)]), Is.EqualTo(ana));
        // A melhor leitura de cada pessoa decide: 85 da Ana contra 50 do Bruno.
        Assert.That(MatchingPolicy.Select([(ana, 12d), (ana, 85d), (bruno, 50d), (bruno, 45d)]), Is.EqualTo(ana));
        // Entre pessoas diferentes, a margem continua valendo: 85 contra 80 é ambíguo.
        Assert.That(MatchingPolicy.Select([(ana, 30d), (ana, 85d), (bruno, 80d)]), Is.Null);
        Assert.That(MatchingPolicy.Select([(ana, 85d), (bruno, double.PositiveInfinity)]), Is.Null);
        Assert.That(MatchingPolicy.Select([]), Is.Null);
    }

    [Test]
    public void OldTemplateFilesWithOnlyOneTemplateRemainReadable()
    {
        // Formato gravado pela 1.3 (e pelo teste do instalador): só cadastroId, comandoId e template.
        const string legacy = "{\"cadastroId\":\"20000000-0000-0000-0000-000000000002\",\"comandoId\":\"30000000-0000-0000-0000-000000000003\",\"template\":\"BwgJ\"}";
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var template = JsonSerializer.Deserialize<StoredTemplate>(legacy, json)!;
        Assert.That(template.ExtraTemplates, Is.Null);
        Assert.That(template.Count, Is.EqualTo(1));
        Assert.That(template.AllTemplates(), Is.EqualTo(new[] { new byte[] { 7, 8, 9 } }));

        var current = new StoredTemplate(template.CadastroId, template.ComandoId, [1]) { ExtraTemplates = [[2], [3], [4]] };
        var roundTrip = JsonSerializer.Deserialize<StoredTemplate>(JsonSerializer.Serialize(current, json), json)!;
        Assert.That(roundTrip.Count, Is.EqualTo(4));
        Assert.That(roundTrip.AllTemplates().Select(t => t[0]), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        roundTrip.Clear();
        Assert.That(roundTrip.AllTemplates().All(t => t.All(b => b == 0)), Is.True);
    }

    [Test]
    public void OldConfigurationKeepsTheSoundOnAndHasNoReaderName()
    {
        const string legacy = "{\"configuration\":{\"centralUrl\":\"https://central.example\",\"projectUrl\":\"\",\"publicApiKey\":\"\",\"scannerDllPath\":\"\",\"deviceId\":\"00000000-0000-0000-0000-000000000000\",\"deviceToken\":\"\"},\"pendingRemovals\":[],\"deviceRevoked\":false,\"totalRemovalPending\":false}";
        var state = JsonSerializer.Deserialize<AgentState>(legacy, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        Assert.That(state.Configuration.SoundEnabled, Is.True);
        Assert.That(state.Configuration.DeviceName, Is.Empty);
        Assert.That(new AgentConfiguration().SoundEnabled, Is.True);
    }

    [Test]
    [Platform("Win")]
    public void StoreProtectsTokenAndTemplatesAndDetectsTampering()
    {
        var path = Path.Combine(Path.GetTempPath(), "fs80h-" + Guid.NewGuid());
        try
        {
            var store = new ProtectedLocalStore(path);
            var id = Guid.NewGuid();
            store.SaveState(new AgentState { Configuration = new AgentConfiguration { DeviceToken = "opaque-test-secret", DeviceId = id } });
            store.SaveTemplate(new StoredTemplate(id, Guid.NewGuid(), [10, 20, 30, 40]));
            Assert.That(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(path, "state.dpapi"))), Does.Not.Contain("opaque-test-secret"));
            Assert.That(store.LoadState().Configuration.DeviceToken, Is.EqualTo("opaque-test-secret"));
            Assert.That(store.LoadTemplates().Single().Template, Is.EqualTo(new byte[] { 10, 20, 30, 40 }));
            var templateFile = Directory.GetFiles(Path.Combine(path, "templates")).Single();
            var encrypted = File.ReadAllBytes(templateFile);
            encrypted[^1] ^= 1;
            File.WriteAllBytes(templateFile, encrypted);
            Assert.Throws<LocalDataException>(() => store.LoadTemplates());
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Test]
    [Platform("Win")]
    public void StoreKeepsEveryGoodReadingAndStillReadsTheOldSingleTemplateFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "fs80h-" + Guid.NewGuid());
        try
        {
            var store = new ProtectedLocalStore(path);
            var current = Guid.NewGuid();
            store.SaveTemplate(new StoredTemplate(current, Guid.NewGuid(), [1, 1]) { ExtraTemplates = [[2, 2], [3, 3], [4, 4]] });
            // Arquivo como a 1.3 gravava: mesma cifra DPAPI, sem extraTemplates.
            var legacy = Guid.NewGuid();
            var plain = System.Text.Encoding.UTF8.GetBytes($"{{\"cadastroId\":\"{legacy}\",\"comandoId\":\"{Guid.NewGuid()}\",\"template\":\"BwgJ\"}}");
            var protectedBytes = ProtectedData.Protect(plain, System.Text.Encoding.UTF8.GetBytes("Futureplace.AgenteFS80H.protocolo1"), DataProtectionScope.CurrentUser);
            File.WriteAllBytes(Path.Combine(path, "templates", legacy.ToString("N") + ".dpapi"), protectedBytes);
            var loaded = store.LoadTemplates().ToDictionary(t => t.CadastroId);
            Assert.That(loaded[current].AllTemplates().Select(t => t[0]), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            Assert.That(loaded[legacy].AllTemplates().Single(), Is.EqualTo(new byte[] { 7, 8, 9 }));
            Assert.That(loaded[legacy].ExtraTemplates, Is.Null);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Test]
    [Platform("Win")]
    public void RemovalPurgesOrphanedEncryptedCopiesAfterInterruptedAtomicWrite()
    {
        var path = Path.Combine(Path.GetTempPath(), "fs80h-" + Guid.NewGuid());
        try
        {
            var store = new ProtectedLocalStore(path);
            var id = Guid.NewGuid();
            store.SaveTemplate(new StoredTemplate(id, Guid.NewGuid(), [7]));
            var canonical = Path.Combine(path, "templates", id.ToString("N") + ".dpapi");
            File.Copy(canonical, canonical + ".new-" + Guid.NewGuid().ToString("N"));
            File.Delete(canonical);
            Assert.That(store.ListTemplateIds(), Does.Contain(id));
            store.DeleteTemplate(id);
            Assert.That(Directory.GetFiles(Path.Combine(path, "templates")), Is.Empty);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    [Test]
    public async Task EnrollmentRequiresFingerLiftBetweenSamplesAndClearsRawImages()
    {
        var scanner = new AlternatingScanner();
        var recognizer = new FakeRecognizer { Sequential = true };
        var collector = new EnrollmentCollector(scanner, recognizer, TimeProvider.System, TimeSpan.Zero);
        var phases = new List<EnrollmentPhase>();
        var result = await collector.CollectAsync(2, _ => Task.CompletedTask, _ => null, (phase, _, _) => phases.Add(phase), CancellationToken.None);
        Assert.That(result.Select(t => t[0]), Is.EqualTo(new byte[] { 1, 2 }));
        Assert.That(scanner.Captures, Is.EqualTo(2));
        Assert.That(scanner.CapturedImages.All(p => p.All(b => b == 0)), Is.True);
        // Leitor livre no início, dedo, retirada, dedo de novo.
        Assert.That(scanner.PresenceCalls, Is.EqualTo(4));
        Assert.That(phases, Is.EqualTo(new[] { EnrollmentPhase.Place, EnrollmentPhase.Reading, EnrollmentPhase.Lift, EnrollmentPhase.Place, EnrollmentPhase.Reading }));
    }

    [Test]
    public async Task ABadReadingIsAskedAgainWithoutCancellingAndEveryGoodReadingMatchesAPreviousOne()
    {
        var scanner = new AlternatingScanner();
        // A terceira leitura (dedo mexeu) não bate com nenhuma; as outras batem entre si.
        var recognizer = new FakeRecognizer { Sequential = true, ScoreOf = (a, b) => a == 3 || b == 3 ? 5 : 80 };
        var collector = new EnrollmentCollector(scanner, recognizer, TimeProvider.System, TimeSpan.Zero);
        var reports = new List<(EnrollmentPhase Phase, int Good, int Reading)>();
        var result = await collector.CollectAsync(4, _ => Task.CompletedTask, _ => null, (phase, good, reading) => reports.Add((phase, good, reading)), CancellationToken.None);
        Assert.That(result.Select(t => t[0]), Is.EqualTo(new byte[] { 1, 2, 4, 5 }));
        Assert.That(collector.Attempts, Is.EqualTo(5));
        Assert.That(reports, Does.Contain((EnrollmentPhase.BadReading, 2, 3)));
        Assert.That(reports.Last(), Is.EqualTo((EnrollmentPhase.Reading, 3, 4)));
        Assert.That(scanner.CapturedImages.All(p => p.All(b => b == 0)), Is.True);
    }

    [Test]
    public async Task ATransientCaptureErrorIsRetriedInsteadOfFailingTheEnrollment()
    {
        var scanner = new AlternatingScanner { FailCaptures = [2] };
        var collector = new EnrollmentCollector(scanner, new FakeRecognizer { Sequential = true }, TimeProvider.System, TimeSpan.Zero);
        var result = await collector.CollectAsync(2, _ => Task.CompletedTask, _ => null, (_, _, _) => { }, CancellationToken.None);
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(scanner.Captures, Is.EqualTo(3));
    }

    [Test]
    public async Task WhenTheFirstReadingWasTheBadOneTheNextMatchingPairReplacesIt()
    {
        var scanner = new AlternatingScanner();
        // A leitura 1 foi ruim; 2, 3 e 4 são do mesmo dedo e batem entre si.
        var recognizer = new FakeRecognizer { Sequential = true, ScoreOf = (a, b) => a == 1 || b == 1 ? 5 : 80 };
        var collector = new EnrollmentCollector(scanner, recognizer, TimeProvider.System, TimeSpan.Zero);
        var result = await collector.CollectAsync(3, _ => Task.CompletedTask, _ => null, (_, _, _) => { }, CancellationToken.None);
        Assert.That(result.Select(t => t[0]), Is.EqualTo(new byte[] { 2, 3, 4 }));
    }

    [Test]
    public void ReadingsThatNeverMatchFailAfterTheAttemptLimitAndClearEverything()
    {
        var scanner = new AlternatingScanner();
        var collector = new EnrollmentCollector(scanner, new FakeRecognizer { Sequential = true, ScoreValue = 2 }, TimeProvider.System, TimeSpan.Zero);
        var failure = Assert.ThrowsAsync<EnrollmentFailedException>(async () => await collector.CollectAsync(4, _ => Task.CompletedTask, _ => null, (_, _, _) => { }, CancellationToken.None));
        Assert.That(failure!.Reason, Is.EqualTo("captura"));
        Assert.That(scanner.Captures, Is.EqualTo(EnrollmentCollector.MaxAttempts));
        Assert.That(scanner.CapturedImages.All(p => p.All(b => b == 0)), Is.True);
    }

    [Test]
    public void AFingerAlreadyEnrolledForSomeoneElseStopsWithTheOtherEnrollment()
    {
        var other = Guid.NewGuid();
        var collector = new EnrollmentCollector(new AlternatingScanner(), new FakeRecognizer { Sequential = true }, TimeProvider.System, TimeSpan.Zero);
        var failure = Assert.ThrowsAsync<EnrollmentFailedException>(async () => await collector.CollectAsync(4, _ => Task.CompletedTask,
            sample => sample[0] == 2 ? other : null, (_, _, _) => { }, CancellationToken.None));
        Assert.That(failure!.Reason, Is.EqualTo("duplicada"));
        Assert.That(failure.DuplicateOf, Is.EqualTo(other));
    }

    [Test]
    public void RevocationDuringFingerWaitAbortsBeforeCapture()
    {
        var scanner = new AlternatingScanner();
        var collector = new EnrollmentCollector(scanner, new FakeRecognizer(), TimeProvider.System, TimeSpan.Zero);
        Assert.ThrowsAsync<CentralException>(async () => await collector.CollectAsync(2, _ => throw new CentralException("Leitor revogado."), _ => null, (_, _, _) => { }, CancellationToken.None));
        Assert.That(scanner.Captures, Is.Zero);
    }

    internal sealed class FakeScanner(IEnumerable<bool> presence) : IFingerprintScanner
    {
        private readonly Queue<bool> sequence = new(presence);
        public int Captures { get; private set; }
        public Action<int>? OnPresence { get; init; }
        public int PresenceCalls { get; private set; }
        public List<byte[]> CapturedImages { get; } = [];
        public bool IsFingerPresent()
        {
            PresenceCalls++;
            OnPresence?.Invoke(PresenceCalls);
            return sequence.Count > 0 && sequence.Dequeue();
        }
        public FingerprintFrame Capture()
        {
            Captures++;
            byte[] pixels = [1, 2, 3, 4];
            CapturedImages.Add(pixels);
            return new(2, 2, pixels);
        }
        public void Dispose() { }
    }

    /// <summary>Dedo sai e volta a cada consulta: livre, presente, livre, presente...</summary>
    internal sealed class AlternatingScanner : IFingerprintScanner
    {
        private bool present = true;
        public int Captures { get; private set; }
        public int PresenceCalls { get; private set; }
        public HashSet<int> FailCaptures { get; init; } = [];
        public Action<int>? OnPresence { get; init; }
        public List<byte[]> CapturedImages { get; } = [];
        public bool IsFingerPresent()
        {
            PresenceCalls++;
            OnPresence?.Invoke(PresenceCalls);
            present = !present;
            return present;
        }
        public FingerprintFrame Capture()
        {
            Captures++;
            if (FailCaptures.Contains(Captures)) throw new CaptureException("captura");
            byte[] pixels = [1, 2, 3, 4];
            CapturedImages.Add(pixels);
            return new(2, 2, pixels);
        }
        public void Dispose() { }
    }

    internal sealed class FakeRecognizer : IFingerprintRecognizer
    {
        private byte next;
        public double ScoreValue { get; init; } = 80;
        /// <summary>Cada leitura vira um template diferente: 1, 2, 3...</summary>
        public bool Sequential { get; init; }
        public Func<byte, byte, double>? ScoreOf { get; init; }
        public byte[] Extract(FingerprintFrame image) => Sequential ? [++next] : [7];
        public IFingerprintMatcher Matcher(byte[] probe) => new Prepared(this, probe[0]);
        private sealed class Prepared(FakeRecognizer owner, byte probe) : IFingerprintMatcher
        {
            public double Score(byte[] candidate) => owner.ScoreOf?.Invoke(probe, candidate[0]) ?? owner.ScoreValue;
        }
    }
}

using System.Security.Cryptography;
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
    public async Task CaptureRequiresFingerLiftBetweenSamplesAndClearsRawImages()
    {
        var scanner = new FakeScanner([true, true, false, true, false]);
        var recognizer = new FakeRecognizer { ScoreValue = 80 };
        var capture = new CaptureCoordinator(scanner, recognizer, TimeProvider.System, TimeSpan.Zero);
        var result = await capture.CapturePairAsync(_ => Task.CompletedTask, CancellationToken.None);
        Assert.That(result, Is.EqualTo(new byte[] { 7 }));
        Assert.That(scanner.Captures, Is.EqualTo(2));
        Assert.That(scanner.CapturedImages.All(p => p.All(b => b == 0)), Is.True);
        Assert.That(scanner.PresenceCalls, Is.GreaterThanOrEqualTo(5));
    }

    [Test]
    public void InconsistentSamplesNeverProduceEnrollmentTemplate()
    {
        var scanner = new FakeScanner([true, false, true, false]);
        var capture = new CaptureCoordinator(scanner, new FakeRecognizer { ScoreValue = 2 }, TimeProvider.System, TimeSpan.Zero);
        Assert.ThrowsAsync<CaptureException>(async () => await capture.CapturePairAsync(_ => Task.CompletedTask, CancellationToken.None));
        Assert.That(scanner.CapturedImages.All(p => p.All(b => b == 0)), Is.True);
    }

    [Test]
    public void RevocationDuringFingerWaitAbortsBeforeCapture()
    {
        var scanner = new FakeScanner([false]);
        var capture = new CaptureCoordinator(scanner, new FakeRecognizer(), TimeProvider.System, TimeSpan.Zero);
        Assert.ThrowsAsync<CentralException>(async () => await capture.CapturePairAsync(_ => throw new CentralException("Leitor revogado."), CancellationToken.None));
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
    internal sealed class FakeRecognizer : IFingerprintRecognizer
    {
        public double ScoreValue { get; init; } = 80;
        public byte[] Extract(FingerprintFrame image) => [7];
        public double Score(byte[] probe, byte[] candidate) => ScoreValue;
    }
}

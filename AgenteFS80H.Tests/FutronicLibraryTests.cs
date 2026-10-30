using System.Runtime.InteropServices;
using Futureplace.AgenteFS80H;
using NUnit.Framework;

namespace AgenteFS80H.Tests;

public class FutronicLibraryTests
{
    private string directory = null!;
    private string library = null!;

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), "fs80h-pe-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        library = Path.Combine(directory, "ftrScanAPI.dll");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(directory, true);

    [Test]
    public async Task MatchingProcessLibraryCanBeConfiguredWithoutChangingPairing()
    {
        WriteLibrary(Environment.Is64BitProcess ? (ushort)0x8664 : (ushort)0x014c);
        var store = new RuntimeTests.MemoryStore();
        var previous = store.LoadState().Configuration;
        using var runtime = new AgentRuntime(store,
            _ => throw new AssertionException("Configurar a DLL não deve chamar a Central."),
            _ => throw new AssertionException("Configurar a DLL não deve carregar código nativo."),
            new SecurityAndCaptureTests.FakeRecognizer(), TimeProvider.System);

        await runtime.ConfigureScannerAsync(library, CancellationToken.None);

        Assert.That(store.LoadState().Configuration, Is.EqualTo(previous with { ScannerDllPath = library }));
    }

    [Test]
    public void OppositeArchitectureNeverReplacesTheConfiguredLibrary()
    {
        WriteLibrary(Environment.Is64BitProcess ? (ushort)0x014c : (ushort)0x8664);
        var store = new RuntimeTests.MemoryStore();
        var previous = store.LoadState();
        using var runtime = new AgentRuntime(store,
            _ => throw new AssertionException("Não deve chamar a Central."),
            _ => throw new AssertionException("Não deve carregar a DLL incompatível."),
            new SecurityAndCaptureTests.FakeRecognizer(), TimeProvider.System);

        Assert.ThrowsAsync<CaptureException>(async () => await runtime.ConfigureScannerAsync(library, CancellationToken.None));
        Assert.That(store.LoadState(), Is.EqualTo(previous));
    }

    [TestCase(0xAA64)]
    [TestCase(0)]
    public void UnsupportedMachineIsRejected(int machine)
    {
        WriteLibrary((ushort)machine);
        Assert.Throws<CaptureException>(() => FutronicScanner.ValidateLibrary(library));
    }

    [Test]
    public void TruncatedHeaderIsRejected()
    {
        File.WriteAllBytes(library, [0x4d, 0x5a]);
        Assert.Throws<CaptureException>(() => FutronicScanner.ValidateLibrary(library));
    }

    [Test]
    public void RelativePathIsRejected()
    {
        Assert.Throws<CaptureException>(() => FutronicScanner.ValidateLibrary("ftrScanAPI.dll"));
    }

    [Test]
    public void TestHostUsesTheRequestedCiArchitecture()
    {
        var expected = Environment.GetEnvironmentVariable("FS80H_TEST_ARCH");
        if (expected is null) return;
        Assert.That(RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), Is.EqualTo(expected));
    }

    private void WriteLibrary(ushort machine)
    {
        // Header PE sintético. Nunca carregar este arquivo; os testes exercitam a configuração.
        using var writer = new BinaryWriter(File.Create(library));
        writer.Write(new byte[512]);
        writer.BaseStream.Position = 0;
        writer.Write((ushort)0x5a4d);
        writer.BaseStream.Position = 0x3c;
        writer.Write(0x80);
        writer.BaseStream.Position = 0x80;
        writer.Write(0x00004550);
        writer.Write(machine);
    }
}

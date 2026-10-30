using System.Text;
using System.Threading;
using Futureplace.AgenteFS80H;
using NUnit.Framework;

namespace AgenteFS80H.Desktop.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class DiagnosticsTests
{
    [Test]
    public void SavedPairingDoesNotInventAConnectionOrAnAvailableUsbReader()
    {
        using var ui = new UiFixture();
        ui.Click("navigationSettings");
        Assert.That(ui.Find<Control>("diagnosticPairing").Text, Does.Contain("pareado"));
        Assert.That(ui.Find<Control>("diagnosticConnection").Text, Does.Contain("Aguardando"));
        Assert.That(ui.Find<Control>("diagnosticReader").Text, Does.Contain("Aguardando confirmação"));
        Assert.That(ui.Find<Control>("diagnosticReader").Text, Does.Not.Contain("conectado"));
        ui.Click("diagnosticRefreshButton");
        Assert.That(ui.Store.Writes, Is.Zero);
    }

    [Test]
    public void AConnectionFailureReplacesPreviousHealthyConnectionAndReaderEvidence()
    {
        using var ui = new UiFixture();
        ui.Click("navigationSettings");
        ui.Snapshot(AgentStatus.Ready, "Coloque o dedo cadastrado no leitor. A decisão depende da Central conectada.");
        var connection = ui.Find<Control>("diagnosticConnection");
        var reader = ui.Find<Control>("diagnosticReader");
        Assert.That(connection.Text, Does.Contain("respondeu"));
        Assert.That(reader.Text, Does.Contain("respondeu"));
        var healthyConnection = connection.ForeColor;
        var healthyReader = reader.ForeColor;
        ui.Snapshot(AgentStatus.Offline, "Sem conexão com a Central. Nenhum acesso será autorizado offline.");
        ui.Click("diagnosticRefreshButton");
        Assert.That(connection.Text, Does.Contain("Sem resposta"));
        Assert.That(reader.Text, Does.Contain("Aguardando confirmação"));
        Assert.That(connection.ForeColor, Is.Not.EqualTo(healthyConnection));
        Assert.That(reader.ForeColor, Is.Not.EqualTo(healthyReader));
        Assert.That(ui.Store.Writes, Is.Zero);
    }

    [Test]
    public void GenericErrorDoesNotClaimThatTheUsbCableWasDisconnected()
    {
        using var ui = new UiFixture();
        ui.Click("navigationSettings");
        ui.Snapshot(AgentStatus.Capturing, "Retire o dedo completamente do leitor.");
        Assert.That(ui.Find<Control>("diagnosticReader").Text, Does.Contain("respondeu"));
        ui.Snapshot(AgentStatus.Error, $"erro privado {UiFixture.SyntheticToken} {UiFixture.SyntheticKey}");
        var card = ui.Find<Control>("diagnosticsCard");
        var text = string.Join("\n", UiFixture.Tree(card).Select(c => c.Text));
        Assert.That(text, Does.Not.Contain("desconectado").And.Not.Contain("erro privado")
            .And.Not.Contain(UiFixture.SyntheticToken).And.Not.Contain(UiFixture.SyntheticKey));
        Assert.That(ui.Find<Control>("diagnosticGuidance").Text, Does.Contain("orientação"));
        Assert.That(ui.Store.Writes, Is.Zero);
    }

    [Test]
    public void AResumedEnrollmentDoesNotInventANewUsbConfirmation()
    {
        using var ui = new UiFixture();
        ui.Click("navigationSettings");
        ui.Snapshot(AgentStatus.Ready, "Cadastro confirmado na Central. O acesso precisa ser habilitado pela administração.");
        Assert.That(ui.Find<Control>("diagnosticConnection").Text, Does.Contain("respondeu"));
        Assert.That(ui.Find<Control>("diagnosticReader").Text, Does.Contain("Aguardando confirmação"));
    }

    [Test]
    public void InspectingTheConfiguredLibraryChecksArchitectureWithoutLoadingOrChangingIt()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "synthetic-dll-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "ftrScanAPI.dll");
        try
        {
            foreach (var (machine, magic, expected) in new[] { (0x8664, 0x20b, "64 bits encontrado"), (0x014c, 0x10b, "32 bits") })
            {
                File.WriteAllBytes(file, SyntheticPe((ushort)machine, (ushort)magic));
                var initial = File.ReadAllBytes(file);
                var diagnostic = Futureplace.AgenteFS80H.Desktop.ReaderDiagnostics.InspectLibrary(file);
                Assert.That(diagnostic.Message, Does.Contain(expected));
                Assert.That(File.ReadAllBytes(file), Is.EqualTo(initial));
            }
            File.WriteAllText(file, "não é um executável", Encoding.UTF8);
            Assert.That(Futureplace.AgenteFS80H.Desktop.ReaderDiagnostics.InspectLibrary(file).Message, Does.Contain("não é compatível"));
            File.Delete(file);
            Assert.That(Futureplace.AgenteFS80H.Desktop.ReaderDiagnostics.InspectLibrary(file).Message, Does.Contain("não foi encontrado"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static byte[] SyntheticPe(ushort machine, ushort magic)
    {
        var bytes = new byte[512];
        using var memory = new MemoryStream(bytes);
        using var writer = new BinaryWriter(memory);
        writer.Write((ushort)0x5a4d);
        memory.Position = 0x3c;
        writer.Write(64);
        memory.Position = 64;
        writer.Write(0x4550);
        writer.Write(machine);
        memory.Position = 84;
        writer.Write((ushort)240);
        writer.Write((ushort)0x2000);
        writer.Write(magic);
        return bytes;
    }
}

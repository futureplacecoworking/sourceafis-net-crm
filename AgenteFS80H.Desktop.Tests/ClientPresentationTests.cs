using Futureplace.AgenteFS80H;
using Futureplace.AgenteFS80H.Desktop;
using NUnit.Framework;

namespace AgenteFS80H.Desktop.Tests;

public class ClientPresentationTests
{
    // Literal UTC inputs straddle the Brasília boundaries, not the runner's local clock.
    [TestCase("2026-10-30T08:59:59Z", "Boa noite!")]
    [TestCase("2026-10-30T09:00:00Z", "Bom dia!")]
    [TestCase("2026-10-30T14:59:59Z", "Bom dia!")]
    [TestCase("2026-10-30T15:00:00Z", "Boa tarde!")]
    [TestCase("2026-10-30T20:59:59Z", "Boa tarde!")]
    [TestCase("2026-10-30T21:00:00Z", "Boa noite!")]
    [TestCase("2026-10-31T02:59:59Z", "Boa noite!")]
    [TestCase("2026-10-31T03:00:00Z", "Boa noite!")]
    public void AuthorizedGreetingUsesBrasiliaRatherThanTheComputerTimeZone(string instant, string greeting)
    {
        var state = ClientPresentation.From(new AgentSnapshot(AgentStatus.Authorized, "mensagem interna"), DateTimeOffset.Parse(instant));
        Assert.That(state.Title, Is.EqualTo(greeting));
        Assert.That(state.Welcome, Is.EqualTo("Seja bem-vindo ao Future Place!"));
        Assert.That(state.Instruction, Is.EqualTo("Aguarde a recepcionista para entrar."));
    }

    [Test]
    public void UnknownCaptureInstructionNeverReplaysAMisleadingPreviousStep()
    {
        var state = ClientPresentation.From(new AgentSnapshot(AgentStatus.Capturing, "informação privada ou instrução nova"), DateTimeOffset.Parse("2026-10-30T12:00:00Z"));
        Assert.That(state.Title, Is.EqualTo("Aguarde um instante"));
        Assert.That(state.Welcome, Is.Null);
        Assert.That(state.Instruction, Does.Not.Contain("privada").And.Not.Contain("Coloque").And.Not.Contain("Retire"));
    }
}

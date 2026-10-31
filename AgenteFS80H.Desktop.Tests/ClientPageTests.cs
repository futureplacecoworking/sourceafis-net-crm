using System.Text.Json;
using System.Text.RegularExpressions;
using Futureplace.AgenteFS80H;
using Futureplace.AgenteFS80H.Desktop;
using NUnit.Framework;

namespace AgenteFS80H.Desktop.Tests;

/// <summary>O estado que vai para a página da tela do cliente (opção A), sem abrir WebView2.</summary>
public class ClientPageTests
{
    // 17:00 UTC = 14:00 em Brasília.
    private static readonly DateTimeOffset Afternoon = DateTimeOffset.Parse("2026-10-30T17:00:00Z");
    private static readonly DisplayInfo Ana = new()
    {
        Nome = "Ana Souza", PrimeiroNome = "Ana", Empresa = "Inova Tecnologia", Sala = "Milão", Codigo = "128",
        Validade = "2026-12-31", Foto = "https://storage.googleapis.com/fotos/ana.jpg", HorarioTexto = "segunda a sexta, das 8h às 20h"
    };

    private static AgentSnapshot Identified(IdentificationResult result, string motivo = "permitido", bool lockInstalled = false, DisplayInfo? display = null) =>
        new(result == IdentificationResult.Authorized ? AgentStatus.Authorized : AgentStatus.Denied, "mensagem interna do leitor")
        {
            Identification = new IdentificationProgress
            {
                Completed = result == IdentificationResult.Authorized ? 4 : 2, FailedStep = result == IdentificationResult.Authorized ? null : 3,
                Result = result, Motivo = motivo, Name = "Ana Souza", Display = display ?? Ana, LockInstalled = lockInstalled
            }
        };

    [Test]
    public void AuthorizedEntryShowsEveryStepTheRoomAndTheWhiteCard()
    {
        var page = ClientPage.From(Identified(IdentificationResult.Authorized), Afternoon);
        Assert.That(page.Fase, Is.EqualTo("ok"));
        Assert.That(page.Rot, Is.EqualTo("Identificação confirmada"));
        Assert.That(page.Tit, Is.EqualTo("Boa tarde, Ana!"));
        Assert.That(page.Destaque, Is.EqualTo("Ana"));
        Assert.That(page.Txt, Is.EqualTo("A recepção vai abrir a porta para você."));
        Assert.That(page.Sala, Is.EqualTo("Sua sala: Milão"));
        Assert.That(page.Passos, Is.EqualTo(new[] { "feito", "feito", "feito", "feito" }));
        Assert.That(page.Etapas, Is.EqualTo(new[] { "Digital lida", "Pessoa reconhecida", "Acesso confirmado pela Central", "Recepção avisada" }));
        Assert.That(page.Cartao, Is.EqualTo(new ClientPageCard
        {
            Nome = "Ana Souza", Iniciais = "AS", Empresa = "Inova Tecnologia", Codigo = "128", Sala = "Milão", Validade = "31/12/2026",
            Foto = "https://storage.googleapis.com/fotos/ana.jpg", Selo = "Identificação confirmada", Alerta = false
        }));
        Assert.That(page.Txt + page.Rot, Does.Not.Match(@"(?i)porta (liberada|aberta)|pode entrar"), "Sem fechadura, a tela não promete porta aberta.");
    }

    [Test]
    public void WithTheLockTheLastStepBecomesDoorReleased()
    {
        var page = ClientPage.From(Identified(IdentificationResult.Authorized, lockInstalled: true), Afternoon);
        Assert.That(page.Etapas[3], Is.EqualTo("Porta liberada"));
        Assert.That(page.Rot, Is.EqualTo("Acesso liberado"));
        Assert.That(page.Txt, Is.EqualTo("Pode entrar: a porta está liberada."));
    }

    [Test]
    public void OldCentralWithoutCardStillGreetsByTheDecisionName()
    {
        var snapshot = Identified(IdentificationResult.Authorized) with
        {
            Identification = new IdentificationProgress { Completed = 4, Result = IdentificationResult.Authorized, Name = "Bruno Lima" }
        };
        var page = ClientPage.From(snapshot, Afternoon);
        Assert.That(page.Tit, Is.EqualTo("Boa tarde, Bruno!"));
        Assert.That(page.Sala, Is.Null);
        Assert.That(page.Cartao?.Nome, Is.EqualTo("Bruno Lima"));
        Assert.That(page.Cartao?.Iniciais, Is.EqualTo("BL"));
        Assert.That(page.Cartao?.Empresa, Is.Null);
    }

    [TestCase("https://storage.googleapis.com/fotos/ana.jpg", true)]
    [TestCase("https://STORAGE.googleapis.com/fotos/ana.jpg", true)]
    [TestCase("http://storage.googleapis.com/fotos/ana.jpg", false)]
    [TestCase("https://storage.googleapis.com.evil.example/ana.jpg", false)]
    [TestCase("https://evil.example/storage.googleapis.com/ana.jpg", false)]
    [TestCase("https://pessoa@storage.googleapis.com/ana.jpg", false)]
    [TestCase("https://storage.googleapis.com:8443/ana.jpg", false)]
    [TestCase("https://googleapis.com/ana.jpg", false)]
    [TestCase("javascript:alert(1)", false)]
    [TestCase("data:image/png;base64,AAAA", false)]
    [TestCase("", false)]
    public void PhotoOnlyComesFromTheCadastroStorage(string address, bool allowed)
    {
        Assert.That(ClientPage.SafePhoto(address) is not null, Is.EqualTo(allowed));
        var page = ClientPage.From(Identified(IdentificationResult.Authorized, display: Ana with { Foto = address }), Afternoon);
        Assert.That(page.Cartao!.Foto is not null, Is.EqualTo(allowed));
        Assert.That(page.Cartao.Iniciais, Is.EqualTo("AS"), "Sem foto, as iniciais.");
    }

    [Test]
    public void OutsideTheScheduleShowsTheAmberSealAndTheHoursFromTheCentral()
    {
        var page = ClientPage.From(Identified(IdentificationResult.Denied, "fora_do_horario"), Afternoon);
        Assert.That(page.Fase, Is.EqualTo("horario"));
        Assert.That(page.Tit, Is.EqualTo("Olá, Ana"));
        Assert.That(page.Txt, Is.EqualTo("Seu horário de acesso: segunda a sexta, das 8h às 20h. Fale com a recepção."));
        Assert.That(page.Passos, Is.EqualTo(new[] { "feito", "feito", "erro", "" }));
        Assert.That(page.ErroTxt, Is.EqualTo("Fora do seu horário"));
        Assert.That(page.Cartao!.Selo, Is.EqualTo("Fora do horário"));
        Assert.That(page.Cartao.Alerta, Is.True);
        var withoutHours = ClientPage.From(Identified(IdentificationResult.Denied, "fora_do_horario", display: Ana with { HorarioTexto = null }), Afternoon);
        Assert.That(withoutHours.Txt, Is.EqualTo("Agora está fora do seu horário de acesso. Fale com a recepção."));
    }

    [TestCase("desabilitado", "Seu acesso não está liberado agora. Fale com a recepção.")]
    [TestCase("arquivada", "Seu acesso não está liberado agora. Fale com a recepção.")]
    [TestCase("pessoa_arquivada", "Seu acesso não está liberado agora. Fale com a recepção.")]
    [TestCase("pessoa_inativa", "Seu acesso não está liberado agora. Fale com a recepção.")]
    [TestCase("revogado", "Seu acesso não está liberado agora. Fale com a recepção.")]
    [TestCase("horario_nao_definido", "Seu acesso não está liberado agora. Fale com a recepção.")]
    [TestCase("sem_contrato", "Seu acesso venceu. Fale com a recepção.")]
    [TestCase("contrato_encerrado", "Seu acesso venceu. Fale com a recepção.")]
    [TestCase("acesso_expirado", "Seu acesso venceu. Fale com a recepção.")]
    [TestCase("motivo_que_ainda_nao_existe", "Fale com a recepção.")]
    [TestCase("", "Fale com a recepção.")]
    public void EachDenialReasonHasAFriendlyText(string motivo, string text)
    {
        var page = ClientPage.From(Identified(IdentificationResult.Denied, motivo), Afternoon);
        Assert.That(page.Fase, Is.EqualTo("negado"));
        Assert.That(page.Txt, Is.EqualTo(text));
        Assert.That(page.Passos, Is.EqualTo(new[] { "feito", "feito", "erro", "" }));
        Assert.That(page.Cartao, Is.Null);
        // Comparação ordinal: a comparação cultural do .NET (ICU) ignora caracteres invisíveis e acharia texto vazio em qualquer lugar.
        var json = ClientPage.Serialize(page);
        Assert.That(motivo.Length > 0 && json.Contains(motivo, StringComparison.Ordinal), Is.False, "O código interno do motivo não aparece para o cliente.");
    }

    [Test]
    public void ANotRecognizedFingerAsksToTryAgain()
    {
        var snapshot = new AgentSnapshot(AgentStatus.Denied, "Digital não reconhecida ou correspondência ambígua.")
        {
            Identification = new IdentificationProgress { Completed = 1, FailedStep = 2, Result = IdentificationResult.NotRecognized }
        };
        var page = ClientPage.From(snapshot, Afternoon);
        Assert.That(page.Fase, Is.EqualTo("negado"));
        Assert.That(page.Tit, Is.EqualTo("Vamos tentar de novo?"));
        Assert.That(page.Passos, Is.EqualTo(new[] { "feito", "erro", "", "" }));
        Assert.That(page.ErroTxt, Is.EqualTo("Digital não reconhecida"));
        Assert.That(page.Cartao, Is.Null);
    }

    [TestCase(0, "Lendo sua digital…", "ativo", "")]
    [TestCase(1, "Reconhecendo…", "feito", "ativo")]
    [TestCase(2, "Consultando a Central…", "feito", "feito")]
    public void TheTimelineFollowsTheReading(int completed, string title, string first, string second)
    {
        var snapshot = new AgentSnapshot(AgentStatus.Capturing, "interno")
        {
            Identification = new IdentificationProgress { Completed = completed, Result = IdentificationResult.InProgress }
        };
        var page = ClientPage.From(snapshot, Afternoon);
        Assert.That(page.Fase, Is.EqualTo("lendo"));
        Assert.That(page.Tit, Is.EqualTo(title));
        Assert.That(page.Passos[0], Is.EqualTo(first));
        Assert.That(page.Passos[1], Is.EqualTo(second));
    }

    [Test]
    public void EnrollmentFillsTheFingerprintReadingByReading()
    {
        EnrollmentProgress Progress(EnrollmentPhase phase, int good, int reading, string? motivo = null) =>
            new() { Name = "Ana Souza", Company = "Inova Tecnologia", Total = 4, Good = good, Reading = reading, Phase = phase, Motivo = motivo };
        ClientPageState Page(EnrollmentProgress progress) => ClientPage.From(new AgentSnapshot(AgentStatus.Capturing, "interno") { Enrollment = progress }, Afternoon);

        var start = Page(Progress(EnrollmentPhase.Place, 0, 1));
        Assert.That(start.Fase, Is.EqualTo("cadastro"));
        Assert.That(start.Tit, Is.EqualTo("Olá, Ana Souza"));
        Assert.That(start.Txt, Does.Contain("4 vezes"));
        Assert.That(start.Leit, Is.EqualTo(new[] { "agora", "", "", "" }));
        Assert.That(start.LeitN, Is.EqualTo("Leitura 1 de 4"));
        Assert.That(start.Ped, Is.EqualTo("poe"));

        var second = Page(Progress(EnrollmentPhase.Place, 1, 2));
        Assert.That(second.Tit, Is.EqualTo("Coloque o mesmo dedo"));
        Assert.That(second.Leit, Is.EqualTo(new[] { "ok", "agora", "", "" }));
        Assert.That(second.LeitN, Is.EqualTo("Leitura 2 de 4"));
        Assert.That(second.Enche, Is.EqualTo(0.25).Within(0.0001));

        var bad = Page(Progress(EnrollmentPhase.BadReading, 2, 3));
        Assert.That(bad.Tit, Is.EqualTo("Não deu para ler"));
        Assert.That(bad.Txt, Is.EqualTo("Não deu para ler bem. Levante o dedo e coloque de novo, sem mexer."));
        Assert.That(bad.Leit, Is.EqualTo(new[] { "ok", "ok", "erro", "" }));

        var lift = Page(Progress(EnrollmentPhase.Lift, 3, 3));
        Assert.That(lift.Tit, Is.EqualTo("Boa leitura! Levante o dedo"));
        Assert.That(lift.Leit, Is.EqualTo(new[] { "ok", "ok", "ok", "" }));

        var done = Page(Progress(EnrollmentPhase.Completed, 4, 4));
        Assert.That(done.Fase, Is.EqualTo("cadastro-ok"));
        Assert.That(done.Tit, Is.EqualTo("Digital cadastrada!"));
        Assert.That(done.Txt, Does.StartWith("Pronto, Ana."));
        Assert.That(done.Leit, Is.EqualTo(new[] { "ok", "ok", "ok", "ok" }));
        Assert.That(done.LeitN, Is.EqualTo("4 de 4 leituras"));
        Assert.That(done.Enche, Is.EqualTo(1));

        Assert.That(Page(Progress(EnrollmentPhase.Cancelled, 1, 2)).Tit, Is.EqualTo("Cadastro cancelado na Central"));
        Assert.That(Page(Progress(EnrollmentPhase.Failed, 1, 2, "duplicada")).Tit, Is.EqualTo("Esta digital já está cadastrada"));
        Assert.That(Page(Progress(EnrollmentPhase.Failed, 0, 1, "expirado")).Tit, Is.EqualTo("O tempo do cadastro acabou"));
        Assert.That(Page(Progress(EnrollmentPhase.Failed, 0, 1, "captura")).Falhou, Is.True);
    }

    [Test]
    public void WaitingAsksForTheFingerAndPointsToTheReaderBelow()
    {
        var place = ClientPage.From(new AgentSnapshot(AgentStatus.Ready, "interno") { Hint = ReaderHint.PlaceFinger }, Afternoon);
        Assert.That(place.Fase, Is.EqualTo("espera"));
        Assert.That(place.Tit, Is.EqualTo("Coloque o dedo no leitor"));
        var lift = ClientPage.From(new AgentSnapshot(AgentStatus.Ready, "interno") { Hint = ReaderHint.LiftFinger }, Afternoon);
        Assert.That(lift.Tit, Is.EqualTo("Retire o dedo do leitor"));
        foreach (var status in new[] { AgentStatus.Offline, AgentStatus.Error, AgentStatus.Revoked, AgentStatus.Unconfigured })
            Assert.That(ClientPage.From(new AgentSnapshot(status, "interno"), Afternoon).Fase, Is.EqualTo("indisponivel"));
    }

    [Test]
    public void RuntimeMessagesNeverReachTheCustomerPage()
    {
        const string secret = "Thompson; C:\\Futronic\\ftrScanAPI.dll; https://central.example; token-secreto";
        foreach (var status in Enum.GetValues<AgentStatus>())
        {
            var json = ClientPage.Serialize(ClientPage.From(new AgentSnapshot(status, secret), Afternoon));
            foreach (var part in new[] { "Thompson", "Futronic", "central.example", "token-secreto" })
                Assert.That(json, Does.Not.Contain(part), $"Texto interno na tela do cliente em {status}.");
        }
    }

    [Test]
    public void TheJsonUsesThePrototypeNamesAndOmitsEmptyFields()
    {
        using var json = JsonDocument.Parse(ClientPage.Serialize(ClientPage.From(Identified(IdentificationResult.Authorized), Afternoon)));
        var root = json.RootElement;
        foreach (var name in new[] { "fase", "rot", "tit", "destaque", "txt", "sala", "passos", "etapas", "leit", "enche", "cartao" })
            Assert.That(root.TryGetProperty(name, out _), Is.True, name);
        Assert.That(root.TryGetProperty("erroTxt", out _), Is.False);
        Assert.That(root.GetProperty("cartao").GetProperty("iniciais").GetString(), Is.EqualTo("AS"));
        Assert.That(root.GetProperty("cartao").GetProperty("alerta").GetBoolean(), Is.False);
        using var enrollment = JsonDocument.Parse(ClientPage.Serialize(ClientPage.Enrollment(new EnrollmentProgress { Name = "Ana", Total = 4, Good = 1, Reading = 2 })));
        Assert.That(enrollment.RootElement.GetProperty("leitN").GetString(), Is.EqualTo("Leitura 2 de 4"));
    }

    [Test]
    public void ThePageWorksOfflineAndOnlyAllowsThePhotoFromTheCadastroStorage()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "ClientPage");
        foreach (var file in new[] { "index.html", "cliente.css", "cliente.js", "assets/Central.png", "assets/Nunito.ttf", "assets/Nunito-Regular.ttf", "assets/Raleway.ttf", "assets/Raleway-Bold.ttf" })
            Assert.That(File.Exists(Path.Combine(folder, file)), Is.True, file);
        var html = File.ReadAllText(Path.Combine(folder, "index.html"));
        Assert.That(html, Does.Contain("Content-Security-Policy").And.Contain("script-src 'self'").And.Contain("connect-src 'none'"));
        foreach (var name in new[] { "index.html", "cliente.css", "cliente.js" })
        {
            var text = File.ReadAllText(Path.Combine(folder, name));
            foreach (Match address in Regex.Matches(text, @"https?://[^\s""'`)]+"))
                Assert.That(address.Value, Does.StartWith("https://storage.googleapis.com"), $"{name} usa a internet: {address.Value}");
            Assert.That(text, Does.Not.Contain("innerHTML = st").And.Not.Contain("eval("));
        }
    }
}

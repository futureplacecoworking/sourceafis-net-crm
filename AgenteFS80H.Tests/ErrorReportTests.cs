using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Futureplace.AgenteFS80H;
using NUnit.Framework;

namespace AgenteFS80H.Tests;

/// <summary>
/// 1.5.0: o programa avisa a Central dos erros e travamentos (`relatar`). Só códigos técnicos, só quando a Central anuncia o recurso,
/// e nada disso pode mudar uma decisão de acesso nem o estado mostrado ao cliente.
/// </summary>
public class ErrorReportTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 31, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    // Um nome, um caminho e um endereço que nunca podem aparecer no que sai do computador.
    private const string Secret = "Maria da Silva C:\\Users\\maria\\Documents\\digital.dat token-123 https://central.example/chave";
    private static readonly string[] Forbidden = ["Maria", "Silva", "C:", "Users", "digital.dat", "token-123", "central.example", "mensagem externa"];

    private static ErrorOutbox NewOutbox(RuntimeTests.ManualClock clock, string? path = null, string version = "1.5.0") => new(path, version, clock);
    private static InvalidOperationException Oops() => new(Secret);
    private static string TempFile() => Path.Combine(TestContext.CurrentContext.WorkDirectory, "relatos-" + Guid.NewGuid().ToString("N"), ErrorOutbox.FileName);
    private static void Cleanup(string file)
    {
        var directory = Path.GetDirectoryName(file)!;
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
    private static void AddMany(ErrorOutbox outbox, int count)
    {
        for (var i = 0; i < count; i++) outbox.RecordError("Parte " + i, Oops());
    }

    // ---------- fila: o que vira aviso ----------

    [Test]
    public void AnErrorBecomesOneItemWithTheTypeAndWindowsCodeButNeverTheMessage()
    {
        var clock = new RuntimeTests.ManualClock(Start);
        using var outbox = NewOutbox(clock);
        outbox.RecordError("Erro não tratado", Oops());
        Assert.That(outbox.Snapshot().Single(), Is.EqualTo(new IncidentReport(Start, "erro", "Erro nao tratado", "System.InvalidOperationException:0x80131509", 0, 1, "1.5.0")));
    }

    [Test]
    public void AWrapperExceptionShowsTheTypeOfWhatIsInsideIt()
    {
        var clock = new RuntimeTests.ManualClock(Start);
        using var outbox = NewOutbox(clock);
        outbox.RecordError("Tarefa com erro não observado", new AggregateException("mensagem externa secreta", new IOException(Secret)));
        var item = outbox.Snapshot().Single();
        Assert.That(item.Code, Does.StartWith("System.IO.IOException:0x"));
        Assert.That(item.Code, Does.Not.Contain("Aggregate"));
        Assert.That(item.Stage, Is.EqualTo("Tarefa com erro nao observado"));
    }

    [TestCase("ShowPage:Configurações", "ShowPage.Configuracoes")]
    [TestCase("ResizeContent:cliente", "ResizeContent.cliente")]
    [TestCase("ShowSnapshot", "ShowSnapshot")]
    [TestCase("Erro na janela", "Erro na janela")]
    [TestCase("início", "inicio")]
    [TestCase("Não foi possível iniciar", "Nao foi possivel iniciar")]
    [TestCase("a/b\\c*d", "a_b_c_d")]
    [TestCase("", "desconhecida")]
    [TestCase("   ", "desconhecida")]
    public void StagesBecomeTheSimpleCharactersTheCentralAccepts(string input, string expected) =>
        Assert.That(IncidentText.Stage(input), Is.EqualTo(expected));

    [Test]
    public void NoStageOrCodeEverBreaksTheContractWhateverTheInput()
    {
        foreach (var stage in new[] { "", new string('x', 500), "😀 emoji", "tab\there", "linha\nnova", "Configurações — avançadas", "C:\\Users\\maria\\digital.dat", "\0" })
            Assert.That(Regex.IsMatch(IncidentText.Stage(stage), "^[A-Za-z0-9_. -]{1,60}$"), Is.True, "etapa: " + stage);
        // Tipos genéricos e aninhados têm crases, colchetes e vírgulas no nome: viram "_" e o total nunca passa de 120.
        foreach (var error in new Exception[] { new GenericOops<List<int>>(), new Outer.Inner(), new InvalidOperationException(), new OperationCanceledException() })
        {
            var code = IncidentText.Code(error);
            Assert.That(Regex.IsMatch(code, "^[A-Za-z0-9_.:+-]{1,120}$"), Is.True, "codigo: " + code);
            Assert.That(code, Does.Match(":0x[0-9A-F]{8}$"));
        }
        Assert.That(IncidentText.Code(new Outer.Inner()), Does.Contain("ErrorReportTests+Outer+Inner"), "O '+' dos tipos aninhados é permitido.");
    }

    private sealed class GenericOops<T> : Exception;
    private static class Outer
    {
        public sealed class Inner : Exception;
    }

    // ---------- fila: juntar repetidos e limites ----------

    [Test]
    public void IdenticalErrorsWithinTenMinutesAreOneItemWithACount()
    {
        var clock = new RuntimeTests.ManualClock(Start);
        using var outbox = NewOutbox(clock);
        var error = Oops();
        outbox.RecordError("Erro na janela", error);
        clock.Advance(TimeSpan.FromMinutes(4));
        outbox.RecordError("Erro na janela", error);
        clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(59)); // 9 min 59 s depois do primeiro
        outbox.RecordError("Erro na janela", error);
        var merged = outbox.Snapshot().Single();
        Assert.That(merged.Count, Is.EqualTo(3));
        Assert.That(merged.At, Is.EqualTo(Start), "O aviso guarda a hora da primeira vez.");

        clock.Advance(TimeSpan.FromSeconds(1)); // 10 min exatos
        outbox.RecordError("Erro na janela", error);
        Assert.That(outbox.Snapshot().Select(item => item.Count), Is.EqualTo(new[] { 3, 1 }), "Passados 10 minutos nasce outro aviso.");

        outbox.RecordError("Erro na janela", new IOException());
        outbox.RecordError("Outro componente", error);
        Assert.That(outbox.Count, Is.EqualTo(4), "Outro tipo de erro ou outra etapa nunca se juntam.");
    }

    [Test]
    public void TheRepeatCountStopsAt1000()
    {
        var clock = new RuntimeTests.ManualClock(Start);
        using var outbox = NewOutbox(clock);
        var error = Oops();
        for (var i = 0; i < 1005; i++) outbox.RecordError("Erro na janela", error);
        Assert.That(outbox.Snapshot().Single().Count, Is.EqualTo(1000));
    }

    [Test]
    public void TheOutboxKeepsAtMost200ItemsAndDropsTheOldest()
    {
        var clock = new RuntimeTests.ManualClock(Start);
        using var outbox = NewOutbox(clock);
        AddMany(outbox, 205);
        var stages = outbox.Snapshot().Select(item => item.Stage).ToArray();
        Assert.That(stages, Has.Length.EqualTo(200));
        Assert.That(stages[0], Is.EqualTo("Parte 5"));
        Assert.That(stages[^1], Is.EqualTo("Parte 204"));
    }

    [Test]
    public void ItemsOlderThan90DaysAreDroppedBeforeSending()
    {
        var clock = new RuntimeTests.ManualClock(Start);
        using var outbox = NewOutbox(clock);
        outbox.RecordError("Antigo", Oops());
        clock.Advance(TimeSpan.FromDays(60));
        outbox.RecordError("Meio", Oops());
        clock.Advance(TimeSpan.FromDays(30)); // "Antigo" tem 90 dias exatos, "Meio" 30
        var exact = outbox.TakeBatch()!;
        Assert.That(exact.Items.Select(item => item.Stage), Is.EqualTo(new[] { "Antigo", "Meio" }), "Com 90 dias exatos ainda vale.");
        outbox.Return(exact);
        clock.Advance(TimeSpan.FromSeconds(1));
        var batch = outbox.TakeBatch()!;
        Assert.That(batch.Items.Select(item => item.Stage), Is.EqualTo(new[] { "Meio" }), "Passou de 90 dias: não é mais enviado.");
        Assert.That(outbox.Count, Is.EqualTo(1));
    }

    [Test]
    public void BatchesHaveAtMost20ItemsOldestFirstAndKeepEverythingUntilTheCentralConfirms()
    {
        var clock = new RuntimeTests.ManualClock(Start);
        using var outbox = NewOutbox(clock);
        AddMany(outbox, 45);
        var first = outbox.TakeBatch()!;
        Assert.That(first.Items.Select(item => item.Stage), Is.EqualTo(Enumerable.Range(0, 20).Select(i => "Parte " + i)));
        var second = outbox.TakeBatch()!;
        Assert.That(second.Items.Select(item => item.Stage), Is.EqualTo(Enumerable.Range(20, 20).Select(i => "Parte " + i)), "O que já está em envio não entra de novo.");
        Assert.That(outbox.Count, Is.EqualTo(45), "Nada sai da fila antes da Central confirmar.");

        outbox.Accept(first, 20);
        Assert.That(outbox.Count, Is.EqualTo(25));
        var third = outbox.TakeBatch()!;
        Assert.That(third.Items, Has.Count.EqualTo(5));
        Assert.That(outbox.TakeBatch(), Is.Null, "Tudo está em envio.");

        outbox.Return(second);
        outbox.Return(third);
        Assert.That(outbox.TakeBatch()!.Items, Has.Count.EqualTo(20), "Devolvido, volta a ser enviado.");
    }

    [Test]
    public void AcceptingPartOfABatchKeepsTheRest()
    {
        var clock = new RuntimeTests.ManualClock(Start);
        using var outbox = NewOutbox(clock);
        AddMany(outbox, 3);
        var batch = outbox.TakeBatch()!;
        outbox.Accept(batch, 1);
        Assert.That(outbox.Snapshot().Select(item => item.Stage), Is.EqualTo(new[] { "Parte 1", "Parte 2" }));
        Assert.That(outbox.TakeBatch()!.Items, Has.Count.EqualTo(2), "O que sobrou deixa de estar em envio.");
    }

    [Test]
    public void ARepeatWhileAnItemIsBeingSentBecomesANewItemAndIsNotLost()
    {
        var clock = new RuntimeTests.ManualClock(Start);
        using var outbox = NewOutbox(clock);
        outbox.RecordError("Erro na janela", Oops());
        var batch = outbox.TakeBatch()!;
        outbox.RecordError("Erro na janela", Oops());
        Assert.That(outbox.Count, Is.EqualTo(2));
        outbox.Accept(batch, 1);
        Assert.That(outbox.Snapshot().Single().Count, Is.EqualTo(1), "O repetido chegou depois do envio e continua na fila.");
    }

    // ---------- travamentos ----------

    private sealed class Log : AppDiagnostics
    {
        public List<string> Lines { get; } = [];
        protected override void Write(string level, string text) => Lines.Add(level + ": " + text);
    }

    [Test, Category("SemTravar")]
    public void AHangThatRecoversBecomesOneTravamentoWithItsDuration()
    {
        var clock = new RuntimeTests.ManualClock(Start);
        using var outbox = NewOutbox(clock);
        var log = new Log { Incidents = outbox };
        log.Step("ShowPage:Configurações");
        var posted = new List<Action>();
        using var watchdog = new UiWatchdog(log, action => { posted.Add(action); return true; }, clock);

        watchdog.Tick();
        for (var second = 0; second < 3; second++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            watchdog.Tick();
        }
        Assert.That(log.Lines.Single(), Does.StartWith("TRAVADA"));
        Assert.That(outbox.Count, Is.Zero, "Enquanto a janela está presa ainda não há duração final: o aviso nasce quando ela volta.");

        clock.Advance(TimeSpan.FromSeconds(5));
        watchdog.Tick();
        posted[0](); // a janela voltou, 8 s depois do primeiro pedido
        Assert.That(outbox.Snapshot().Single(), Is.EqualTo(new IncidentReport(Start, "travamento", "ShowPage.Configuracoes", "ui_sem_resposta", 8, 1, "1.5.0")));

        watchdog.Tick();
        posted[1]();
        Assert.That(outbox.Count, Is.EqualTo(1), "Uma janela que responde não cria aviso nenhum.");
    }

    [Test, Category("SemTravar")]
    public void RepeatedFreezesOnTheSameStepWithinTenMinutesAreOneItemWithTheLongestDuration()
    {
        var clock = new RuntimeTests.ManualClock(Start);
        using var outbox = NewOutbox(clock);
        var log = new Log { Incidents = outbox };
        log.Hang(TimeSpan.FromSeconds(3), "ResizeContent");
        log.Recovered(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromMinutes(2));
        log.Hang(TimeSpan.FromSeconds(3), "ResizeContent");
        log.Recovered(TimeSpan.FromSeconds(12));
        clock.Advance(TimeSpan.FromMinutes(1));
        log.Hang(TimeSpan.FromSeconds(3), "ShowSnapshot");
        log.Recovered(TimeSpan.FromSeconds(4));
        var items = outbox.Snapshot();
        Assert.That(items.Select(item => (item.Stage, item.Count, item.Seconds)), Is.EqualTo(new[] { ("ResizeContent", 2, 12), ("ShowSnapshot", 1, 4) }));
        Assert.That(items.All(item => item.Kind == "travamento" && item.Code == "ui_sem_resposta"), Is.True);
    }

    [Test, Category("SemTravar")]
    public void AFreezeLongerThanADayIsReportedAsExactlyADay()
    {
        var clock = new RuntimeTests.ManualClock(Start);
        using var outbox = NewOutbox(clock);
        var log = new Log { Incidents = outbox };
        log.Hang(TimeSpan.FromSeconds(3), "ResizeContent");
        log.Recovered(TimeSpan.FromDays(3));
        Assert.That(outbox.Snapshot().Single().Seconds, Is.EqualTo(86400));
    }

    [Test, Category("SemTravar")]
    public void AHangThatNeverRecoversIsReportedOnTheNextStartWithTheLastKnownDuration()
    {
        var clock = new RuntimeTests.ManualClock(Start);
        var file = TempFile();
        try
        {
            using (var first = NewOutbox(clock, file, "1.4.1"))
            {
                clock.Advance(TimeSpan.FromSeconds(3));
                var log = new Log { Incidents = first };
                log.Step("ResizeContent");
                log.Hang(TimeSpan.FromSeconds(3.4), log.LastStep);
                Assert.That(first.Flush(TimeSpan.FromSeconds(10)), Is.True);
                Assert.That(first.Count, Is.Zero);
            }
            // O programa foi fechado à força com a janela presa: nunca houve "voltou a responder".
            clock.Advance(TimeSpan.FromMinutes(2));
            var expected = new IncidentReport(Start, "travamento", "ResizeContent", "ui_sem_resposta", 3, 1, "1.4.1");
            using (var second = NewOutbox(clock, file))
            {
                Assert.That(second.Snapshot().Single(), Is.EqualTo(expected), "Mantém a versão do programa que travou.");
                Assert.That(second.Flush(TimeSpan.FromSeconds(10)), Is.True);
            }
            using var third = NewOutbox(clock, file);
            Assert.That(third.Snapshot().Single(), Is.EqualTo(expected), "Reiniciar de novo não repete o aviso.");
        }
        finally { Cleanup(file); }
    }

    [Test]
    public void AWindowErrorIsFiledUnderTheLastStepAndOtherErrorsUnderTheirComponent()
    {
        var clock = new RuntimeTests.ManualClock(Start);
        using var outbox = NewOutbox(clock);
        var log = new Log { Incidents = outbox };
        log.Error(AppDiagnostics.WindowContext, Oops());
        log.Step("ShowSnapshot");
        log.Error(AppDiagnostics.WindowContext, Oops());
        log.Step("ShowPage:Configurações");
        log.Error(AppDiagnostics.WindowContext, Oops());
        log.Error("Erro não tratado", Oops());
        log.Error("Tarefa com erro não observado", Oops());
        Assert.That(outbox.Snapshot().Select(item => item.Stage),
            Is.EqualTo(new[] { "inicio", "ShowSnapshot", "ShowPage.Configuracoes", "Erro nao tratado", "Tarefa com erro nao observado" }));
        Assert.That(log.Lines[1], Does.StartWith("ERRO: Erro na janela: "), "O registro local continua com o texto de sempre.");
    }

    [Test]
    public void TheLogKeepsWorkingWhenTheRecorderFailsAndFeedsItOtherwise()
    {
        var log = new Log { Incidents = new ThrowingRecorder() };
        Assert.DoesNotThrow(() =>
        {
            log.Error("Erro na janela", Oops());
            log.Hang(TimeSpan.FromSeconds(3), "ShowPage");
            log.Recovered(TimeSpan.FromSeconds(4));
        });
        Assert.That(log.Lines, Has.Count.EqualTo(3), "O registro local continua sendo gravado.");

        var clock = new RuntimeTests.ManualClock(Start);
        using var outbox = NewOutbox(clock);
        var fed = new Log { Incidents = outbox };
        fed.Error("Erro na janela", Oops());
        fed.Info("só uma informação");
        Assert.That(outbox.Count, Is.EqualTo(1), "Só erros e travamentos entram na fila.");
        Assert.That(fed.Lines[0], Does.StartWith("ERRO: Erro na janela: System.InvalidOperationException"));
    }

    private sealed class ThrowingRecorder : IIncidentRecorder
    {
        public void RecordError(string stage, Exception exception) => throw new InvalidOperationException("fila quebrada");
        public void RecordHang(TimeSpan duration, string lastStep) => throw new InvalidOperationException("fila quebrada");
        public void RecordRecovered(TimeSpan duration) => throw new InvalidOperationException("fila quebrada");
    }

    // ---------- guardar no disco ----------

    [Test]
    public void TheQueueSurvivesARestartAndNeverStoresMessagesOrPaths()
    {
        var clock = new RuntimeTests.ManualClock(Start);
        var file = TempFile();
        try
        {
            IReadOnlyList<IncidentReport> before;
            using (var outbox = NewOutbox(clock, file))
            {
                outbox.RecordError("Erro na janela", Oops());
                clock.Advance(TimeSpan.FromMinutes(1));
                outbox.RecordError("Erro na janela", Oops());
                outbox.RecordError("Erro não tratado", new IOException(Secret));
                before = outbox.Snapshot();
                Assert.That(outbox.Flush(TimeSpan.FromSeconds(10)), Is.True);
            }
            Assert.That(before, Has.Count.EqualTo(2));
            Assert.That(before[0].Count, Is.EqualTo(2));
            var text = File.ReadAllText(file);
            foreach (var word in Forbidden) Assert.That(text, Does.Not.Contain(word));
            Assert.That(File.Exists(file + ".tmp"), Is.False, "O arquivo temporário não fica para trás.");

            using var again = NewOutbox(clock, file);
            Assert.That(again.Snapshot(), Is.EqualTo(before));
        }
        finally { Cleanup(file); }
    }

    [TestCase("{ isto não é json")]
    [TestCase("")]
    [TestCase("[]")]
    [TestCase("null")]
    [TestCase("{\"formato\":2,\"itens\":[]}")]
    [TestCase("{\"formato\":1,\"itens\":[null,{\"em\":\"ontem\",\"tipo\":\"erro\"},"
        + "{\"em\":\"2026-10-31T15:00:00Z\",\"tipo\":\"erro\",\"etapa\":\"Bad/Stage\",\"codigo\":\"X\",\"segundos\":0,\"quantidade\":1,\"versao\":\"1.5.0\"},"
        + "{\"em\":\"2026-10-31T15:00:00Z\",\"tipo\":\"erro\",\"etapa\":\"Ok\",\"codigo\":\"X\",\"segundos\":0,\"quantidade\":5000,\"versao\":\"1.5.0\"},"
        + "{\"em\":\"2026-10-31T15:00:00Z\",\"tipo\":\"outro\",\"etapa\":\"Ok\",\"codigo\":\"X\",\"segundos\":0,\"quantidade\":1,\"versao\":\"1.5.0\"},"
        + "{\"em\":\"2026-10-31T15:00:00Z\",\"tipo\":\"erro\",\"etapa\":\"Ok\",\"codigo\":\"X\",\"segundos\":0,\"quantidade\":1,\"versao\":\"1.5\"}],"
        + "\"aberto\":{\"em\":\"x\",\"etapa\":\"Ok\",\"segundos\":3,\"versao\":\"1.5.0\"}}")]
    public void ACorruptedOrHostileFileGivesAnEmptyQueueAndNeverThrows(string content)
    {
        var clock = new RuntimeTests.ManualClock(Start);
        var file = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content);
            ErrorOutbox? outbox = null;
            Assert.DoesNotThrow(() => outbox = NewOutbox(clock, file));
            using (outbox)
            {
                Assert.That(outbox!.Count, Is.Zero);
                outbox.RecordError("Erro na janela", Oops());
                Assert.That(outbox.Flush(TimeSpan.FromSeconds(10)), Is.True);
            }
            using var again = NewOutbox(clock, file);
            Assert.That(again.Snapshot().Select(item => item.Stage), Is.EqualTo(new[] { "Erro na janela" }), "O arquivo estragado foi trocado por um bom.");
        }
        finally { Cleanup(file); }
    }

    [Test]
    public void AHugeFileIsIgnoredWithoutBeingRead()
    {
        var clock = new RuntimeTests.ManualClock(Start);
        var file = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, new string(' ', 600 * 1024));
            using var outbox = NewOutbox(clock, file);
            Assert.That(outbox.Count, Is.Zero);
        }
        finally { Cleanup(file); }
    }

    [Test]
    public void OldAndInvalidItemsInAValidFileAreDroppedOnLoad()
    {
        var clock = new RuntimeTests.ManualClock(Start);
        var file = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            // Escrito à mão: um aviso de 122 dias atrás (cai), um de ontem (fica) e um do futuro (fica, mas sai com a hora de agora).
            File.WriteAllText(file, "{\"formato\":1,\"aberto\":null,\"itens\":["
                + "{\"em\":\"2026-07-01T00:00:00Z\",\"tipo\":\"erro\",\"etapa\":\"Velho\",\"codigo\":\"System.IO.IOException:0x80131620\",\"segundos\":0,\"quantidade\":2,\"versao\":\"1.4.1\"},"
                + "{\"em\":\"2026-10-30T12:00:00Z\",\"tipo\":\"travamento\",\"etapa\":\"ShowPage.Leitura\",\"codigo\":\"ui_sem_resposta\",\"segundos\":9,\"quantidade\":1,\"versao\":\"1.4.1\"},"
                + "{\"em\":\"2026-11-30T12:00:00Z\",\"tipo\":\"erro\",\"etapa\":\"Futuro\",\"codigo\":\"X\",\"segundos\":0,\"quantidade\":1,\"versao\":\"1.5.0\"}]}");
            using var outbox = NewOutbox(clock, file);
            Assert.That(outbox.Snapshot().Select(item => item.Stage), Is.EqualTo(new[] { "ShowPage.Leitura", "Futuro" }));
            var batch = outbox.TakeBatch()!;
            Assert.That(batch.Items[0], Is.EqualTo(new IncidentReport(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero), "travamento", "ShowPage.Leitura", "ui_sem_resposta", 9, 1, "1.4.1")));
            Assert.That(batch.Items[1].At, Is.EqualTo(Start), "Um relógio que andou para trás não manda aviso do futuro.");
        }
        finally { Cleanup(file); }
    }

    [Test]
    public void AnUnwritableFolderNeverThrowsOrBlocks()
    {
        var clock = new RuntimeTests.ManualClock(Start);
        // O "diretório" é um arquivo comum: criar a pasta da fila é impossível.
        var blocker = Path.Combine(TestContext.CurrentContext.WorkDirectory, "relatos-blocker-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "não é uma pasta");
        try
        {
            using var outbox = NewOutbox(clock, Path.Combine(blocker, "logs", ErrorOutbox.FileName));
            Assert.DoesNotThrow(() =>
            {
                outbox.RecordError("Erro na janela", Oops());
                Assert.That(outbox.Flush(TimeSpan.FromSeconds(10)), Is.True);
                outbox.Dispose();
                outbox.RecordError("Depois de fechar", Oops());
                Assert.That(outbox.Flush(TimeSpan.FromSeconds(1)), Is.True, "Depois de fechar não há o que esperar.");
            });
            Assert.That(outbox.Count, Is.EqualTo(2), "Sem disco a fila continua funcionando na memória.");
        }
        finally { File.Delete(blocker); }
    }

    // ---------- o que vai pela rede ----------

    private static AgentConfiguration Configuration => new()
    {
        ProjectUrl = "https://project.supabase.co", PublicApiKey = "sb_publishable_test", DeviceId = Guid.NewGuid(), DeviceToken = "device-secret-token-0123456789"
    };

    private static string SyncBody(bool relatar) =>
        "{\"ok\":true,\"protocolo\":1,\"servidor_em\":\"" + DateTimeOffset.UtcNow.ToString("O") + "\",\"revogado\":false,\"cadastros\":[],\"comando\":null,\"recursos\":[\"versao\""
        + (relatar ? ",\"relatar\"" : "") + "]}";

    private sealed record Seen(string Action, string Body, string? Authorization);

    private static (CentralClient Client, List<Seen> Requests) Recording(Func<string, HttpResponseMessage> respond)
    {
        var requests = new List<Seen>();
        var client = new CentralClient(Configuration, new ProtocolTests.Handler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            var action = document.RootElement.GetProperty("acao").GetString()!;
            requests.Add(new Seen(action, body, request.Headers.Authorization?.ToString()));
            return respond(action);
        }), appVersion: "1.5.0");
        return (client, requests);
    }

    private static IncidentReport Item(string kind = "erro", int count = 3) =>
        kind == "erro"
            ? new IncidentReport(Start, "erro", "Erro na janela", "System.InvalidOperationException:0x80131509", 0, count, "1.5.0")
            : new IncidentReport(Start.AddMinutes(1), "travamento", "ShowPage.Configuracoes", "ui_sem_resposta", 8, count, "1.5.0");

    [Test]
    public async Task AnOldCentralNeverGetsTheRelatarAction()
    {
        var (client, requests) = Recording(action => ProtocolTests.Json(action == "sincronizar" ? SyncBody(relatar: false) : "{\"ok\":true,\"recebidos\":1}"));
        using (client)
        {
            Assert.That(await client.ReportAsync([Item()], CancellationToken.None), Is.Null, "Sem sincronizar ainda não se sabe o que a Central entende.");
            await client.SynchronizeAsync(CancellationToken.None);
            Assert.That(await client.ReportAsync([Item()], CancellationToken.None), Is.Null);
            Assert.That(client.Features, Does.Not.Contain("relatar"));
        }
        Assert.That(requests.Select(request => request.Action), Is.EqualTo(new[] { "sincronizar" }));
    }

    [Test]
    public async Task ABatchHasExactlyTheAgreedKeysAndValueFormats()
    {
        var (client, requests) = Recording(action => ProtocolTests.Json(action == "sincronizar" ? SyncBody(relatar: true) : "{\"ok\":true,\"recebidos\":2}"));
        ReportReceipt? receipt;
        using (client)
        {
            await client.SynchronizeAsync(CancellationToken.None);
            receipt = await client.ReportAsync([Item("erro", 3), Item("travamento", 1)], CancellationToken.None);
        }
        Assert.That(receipt, Is.EqualTo(new ReportReceipt(2, false)));
        var request = requests.Single(seen => seen.Action == "relatar");
        Assert.That(request.Authorization, Is.EqualTo("Bearer device-secret-token-0123456789"));
        Assert.That(request.Body, Does.Contain("\"dados\":{\"itens\":[{\"em\":\"2026-10-31T15:00:00Z\",\"tipo\":\"erro\",\"etapa\":\"Erro na janela\","
            + "\"codigo\":\"System.InvalidOperationException:0x80131509\",\"segundos\":0,\"quantidade\":3,\"versao\":\"1.5.0\"}"));
        using var document = JsonDocument.Parse(request.Body);
        var data = document.RootElement.GetProperty("dados");
        Assert.That(data.EnumerateObject().Select(p => p.Name), Is.EqualTo(new[] { "itens" }));
        var items = data.GetProperty("itens").EnumerateArray().ToArray();
        Assert.That(items, Has.Length.EqualTo(2));
        foreach (var item in items)
        {
            Assert.That(item.EnumerateObject().Select(p => p.Name), Is.EquivalentTo(new[] { "em", "tipo", "etapa", "codigo", "segundos", "quantidade", "versao" }));
            Assert.That(item.GetProperty("em").GetString(), Does.Match(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$"));
            Assert.That(item.GetProperty("tipo").GetString(), Is.AnyOf("erro", "travamento"));
            Assert.That(item.GetProperty("etapa").GetString(), Does.Match("^[A-Za-z0-9_. -]{1,60}$"));
            Assert.That(item.GetProperty("codigo").GetString(), Does.Match("^[A-Za-z0-9_.:+-]{1,120}$"));
            Assert.That(item.GetProperty("segundos").ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(item.GetProperty("quantidade").ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(item.GetProperty("versao").GetString(), Does.Match(@"^\d+\.\d+\.\d+$"));
        }
        Assert.That(items[1].GetProperty("tipo").GetString(), Is.EqualTo("travamento"));
        Assert.That(items[1].GetProperty("segundos").GetInt32(), Is.EqualTo(8));
        Assert.That(items[1].GetProperty("em").GetString(), Is.EqualTo("2026-10-31T15:01:00Z"), "Hora em UTC com Z, sem frações.");
    }

    [TestCase("{\"ok\":true,\"recebidos\":1,\"limite\":true}", 1, true)]
    [TestCase("{\"ok\":true,\"recebidos\":2,\"limite\":false}", 2, false)]
    [TestCase("{\"ok\":true}", 2, false)]
    [TestCase("{\"ok\":true,\"recebidos\":99,\"novo\":{\"x\":1}}", 2, false)]
    [TestCase("{\"ok\":true,\"recebidos\":-4}", 0, false)]
    public async Task TheReceiptReadsHowManyWereKeptAndTheDailyLimit(string response, int received, bool limit)
    {
        var (client, _) = Recording(action => ProtocolTests.Json(action == "sincronizar" ? SyncBody(relatar: true) : response));
        using (client)
        {
            await client.SynchronizeAsync(CancellationToken.None);
            Assert.That(await client.ReportAsync([Item(), Item("travamento")], CancellationToken.None), Is.EqualTo(new ReportReceipt(received, limit)));
        }
    }

    [Test]
    public async Task ARefusalOrAFailureThrowsAndNeverReflectsTheResponse()
    {
        var mode = 0;
        var (client, _) = Recording(action => action == "sincronizar"
            ? ProtocolTests.Json(SyncBody(relatar: true))
            : mode == 0 ? ProtocolTests.Json("{\"ok\":false,\"erro\":\"device-secret\"}") : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using (client)
        {
            await client.SynchronizeAsync(CancellationToken.None);
            var refused = Assert.ThrowsAsync<CentralException>(async () => await client.ReportAsync([Item()], CancellationToken.None));
            Assert.That(refused!.Message, Does.Not.Contain("device-secret"));
            mode = 1;
            Assert.ThrowsAsync<CentralException>(async () => await client.ReportAsync([Item()], CancellationToken.None));
        }
    }

    [Test]
    public async Task ABatchMustHaveBetween1And20ItemsAndNothingIsSentOtherwise()
    {
        var (client, requests) = Recording(action => ProtocolTests.Json(action == "sincronizar" ? SyncBody(relatar: true) : "{\"ok\":true,\"recebidos\":1}"));
        using (client)
        {
            await client.SynchronizeAsync(CancellationToken.None);
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await client.ReportAsync([], CancellationToken.None));
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await client.ReportAsync(Enumerable.Repeat(Item(), 21).ToArray(), CancellationToken.None));
            Assert.That((await client.ReportAsync(Enumerable.Repeat(Item(), 20).ToArray(), CancellationToken.None))!.Received, Is.EqualTo(1));
        }
        Assert.That(requests.Count(seen => seen.Action == "relatar"), Is.EqualTo(1));
    }

    [Test]
    public async Task NeitherTheMessageNorPathsNorNamesEverLeaveTheComputer()
    {
        var clock = new RuntimeTests.ManualClock(Start);
        using var outbox = NewOutbox(clock);
        var log = new Log { Incidents = outbox };
        log.Error("Erro na janela", new AggregateException("mensagem externa secreta", new InvalidOperationException(Secret, new IOException(Secret))));
        log.Error("Tarefa com erro não observado", new TypeInitializationException("Maria da Silva", new FileNotFoundException(Secret, "C:\\Users\\maria\\digital.dat")));
        log.Hang(TimeSpan.FromSeconds(3), "Página:Configurações\\x/y");
        log.Recovered(TimeSpan.FromSeconds(6));
        var batch = outbox.TakeBatch()!;

        var (client, requests) = Recording(action => ProtocolTests.Json(action == "sincronizar" ? SyncBody(relatar: true) : "{\"ok\":true,\"recebidos\":3}"));
        using (client)
        {
            await client.SynchronizeAsync(CancellationToken.None);
            await client.ReportAsync(batch.Items, CancellationToken.None);
        }
        var body = requests.Single(seen => seen.Action == "relatar").Body;
        foreach (var word in Forbidden) Assert.That(body, Does.Not.Contain(word));
        using var document = JsonDocument.Parse(body);
        var items = document.RootElement.GetProperty("dados").GetProperty("itens").EnumerateArray().ToArray();
        Assert.That(items, Has.Length.EqualTo(3));
        Assert.That(items[0].GetProperty("codigo").GetString(), Does.StartWith("System.InvalidOperationException:0x"), "Só o tipo do erro que está dentro do embrulho.");
        Assert.That(items[1].GetProperty("codigo").GetString(), Does.StartWith("System.TypeInitializationException:0x"));
        Assert.That(items[2].GetProperty("etapa").GetString(), Is.EqualTo("Pagina.Configuracoes_x_y"), "Uma etapa com caracteres estranhos só passa pelo filtro.");
    }

    // ---------- no ciclo do leitor ----------

    private static SyncResponse Active(DateTimeOffset serverTime, bool relatar, params Guid[] ids) => new()
    {
        Protocolo = 1, ServidorEm = serverTime, Revogado = false, Cadastros = ids, Comando = null, Recursos = relatar ? ["versao", "relatar"] : ["versao"]
    };

    private sealed class Rig : IDisposable
    {
        public RuntimeTests.ManualClock Clock { get; } = new(Start);
        public RuntimeTests.FakeCentral Api { get; }
        public ErrorOutbox Outbox { get; }
        public AgentRuntime Runtime { get; }
        public SecurityAndCaptureTests.FakeScanner Scanner { get; }
        public bool Relatar { get; set; } = true;
        public Func<SyncResponse>? SyncOverride { get; set; }
        public List<AgentSnapshot> Changes { get; } = [];

        public Rig(IEnumerable<bool>? presence = null, RuntimeTests.MemoryStore? store = null, params Guid[] enrolled)
        {
            Api = new RuntimeTests.FakeCentral
            {
                OnSync = () => SyncOverride?.Invoke() ?? Active(Clock.GetUtcNow(), Relatar, enrolled),
                Decision = new() { Autorizado = true, Nome = "Pessoa", Motivo = "permitido", PortaAcionada = false }
            };
            Outbox = new ErrorOutbox(null, "1.5.0", Clock);
            Scanner = new SecurityAndCaptureTests.FakeScanner(presence ?? []);
            Runtime = new AgentRuntime(store ?? new RuntimeTests.MemoryStore(), _ => Api, _ => Scanner, new SecurityAndCaptureTests.FakeRecognizer(), Clock, appVersion: "1.5.0", outbox: Outbox);
            Runtime.Changed += Changes.Add;
        }

        public Task Cycle() => Runtime.RunCycleAsync(CancellationToken.None);
        public void Dispose() { Runtime.Dispose(); Outbox.Dispose(); }
    }

    [Test]
    public async Task NothingIsSentUnlessTheCentralAnnouncesRelatarInTheSynchronization()
    {
        using var rig = new Rig();
        rig.Relatar = false;
        AddMany(rig.Outbox, 3);
        for (var cycle = 0; cycle < 3; cycle++) await rig.Cycle();
        Assert.That(rig.Api.Reports, Is.Empty);
        Assert.That(rig.Outbox.Count, Is.EqualTo(3), "Os avisos esperam, sem se perder.");

        rig.Relatar = true;
        await rig.Cycle();
        Assert.That(rig.Api.Reports, Has.Count.EqualTo(1));
        Assert.That(rig.Outbox.Count, Is.Zero, "A Central confirmou: saem da fila.");

        rig.Relatar = false;
        rig.Outbox.RecordError("Erro na janela", Oops());
        await rig.Cycle();
        Assert.That(rig.Api.Reports, Has.Count.EqualTo(1), "Uma Central que deixou de anunciar o recurso não recebe mais nada.");
        Assert.That(rig.Outbox.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task EachCycleSendsAtMostOneBatchOf20OldestFirst()
    {
        using var rig = new Rig();
        AddMany(rig.Outbox, 45);
        await rig.Cycle();
        Assert.That(rig.Api.Reports, Has.Count.EqualTo(1));
        Assert.That(rig.Api.Reports[0].Select(item => item.Stage), Is.EqualTo(Enumerable.Range(0, 20).Select(i => "Parte " + i)));
        Assert.That(rig.Outbox.Count, Is.EqualTo(25));
        await rig.Cycle();
        await rig.Cycle();
        Assert.That(rig.Api.Reports.Select(batch => batch.Length), Is.EqualTo(new[] { 20, 20, 5 }));
        await rig.Cycle();
        Assert.That(rig.Api.Reports, Has.Count.EqualTo(3), "Fila vazia: nenhuma chamada.");
        Assert.That(rig.Outbox.Count, Is.Zero);
    }

    [Test]
    public async Task AFailureKeepsEverythingAndRetriesAfter1Then2Then4Minutes()
    {
        using var rig = new Rig();
        rig.Api.OnReport = call =>
        {
            if (call < 4) throw new CentralException("Sem conexão.");
            return null;
        };
        await rig.Cycle(); // sem nada na fila: define o estado "pronto" antes dos avisos
        AddMany(rig.Outbox, 3);

        await rig.Cycle(); // 0 min: 1ª tentativa, falha
        Assert.That(rig.Api.Reports, Has.Count.EqualTo(1));
        rig.Clock.Advance(TimeSpan.FromSeconds(30));
        await rig.Cycle();
        Assert.That(rig.Api.Reports, Has.Count.EqualTo(1), "Espera 1 minuto.");
        rig.Clock.Advance(TimeSpan.FromSeconds(30));
        await rig.Cycle(); // 1 min: 2ª tentativa, falha
        Assert.That(rig.Api.Reports, Has.Count.EqualTo(2));
        rig.Clock.Advance(Minute);
        await rig.Cycle(); // 2 min
        Assert.That(rig.Api.Reports, Has.Count.EqualTo(2), "Agora espera 2 minutos.");
        rig.Clock.Advance(Minute);
        await rig.Cycle(); // 3 min: 3ª tentativa, falha
        Assert.That(rig.Api.Reports, Has.Count.EqualTo(3));
        rig.Clock.Advance(TimeSpan.FromMinutes(3));
        await rig.Cycle(); // 6 min
        Assert.That(rig.Api.Reports, Has.Count.EqualTo(3), "Agora espera 4 minutos.");
        Assert.That(rig.Outbox.Count, Is.EqualTo(3), "Nada se perdeu nas falhas.");
        rig.Clock.Advance(Minute);
        await rig.Cycle(); // 7 min: 4ª tentativa, a Central recebe
        Assert.That(rig.Api.Reports, Has.Count.EqualTo(4));
        Assert.That(rig.Api.Reports[3].Select(item => item.Stage), Is.EqualTo(rig.Api.Reports[0].Select(item => item.Stage)), "O mesmo lote foi reenviado.");
        Assert.That(rig.Outbox.Count, Is.Zero);
        Assert.That(rig.Runtime.Snapshot.Status, Is.EqualTo(AgentStatus.Ready));
        Assert.That(rig.Changes.Select(snapshot => (snapshot.Status, snapshot.Message)).Distinct().Count(), Is.EqualTo(1), "Falha no aviso nunca muda o que a tela mostra (só o horário do último contato anda).");
    }

    [Test]
    public async Task TheRetryWaitGrowsToTenMinutesAndNoFurther()
    {
        using var rig = new Rig();
        rig.Api.OnReport = _ => throw new CentralException("Sem conexão.");
        AddMany(rig.Outbox, 1);
        var attempts = new List<int>();
        for (var minute = 0; minute <= 40; minute++)
        {
            var before = rig.Api.Reports.Count;
            await rig.Cycle();
            if (rig.Api.Reports.Count > before) attempts.Add(minute);
            rig.Clock.Advance(Minute);
        }
        Assert.That(attempts, Is.EqualTo(new[] { 0, 1, 3, 7, 15, 25, 35 }), "Intervalos de 1, 2, 4, 8 e depois 10 minutos.");
    }

    [Test]
    public async Task EveryKindOfFailureIsSwallowedAndKeepsTheItems()
    {
        using var rig = new Rig();
        AddMany(rig.Outbox, 2);
        var failures = new Func<Exception>[]
        {
            () => new CentralException("A Central não confirmou a operação."),
            () => new HttpRequestException("sem rede"),
            () => new TaskCanceledException("tempo esgotado"),
            () => new InvalidOperationException("inesperado")
        };
        rig.Api.OnReport = call => throw failures[call - 1]();
        foreach (var _ in failures)
        {
            Assert.DoesNotThrowAsync(async () => await rig.Cycle());
            rig.Clock.Advance(TimeSpan.FromMinutes(30));
        }
        Assert.That(rig.Api.Reports, Has.Count.EqualTo(4));
        Assert.That(rig.Outbox.Count, Is.EqualTo(2));
        Assert.That(rig.Runtime.Snapshot.Status, Is.EqualTo(AgentStatus.Ready));
    }

    [Test]
    public async Task TheDailyLimitKeepsTheRestAndPausesSendingForADay()
    {
        using var rig = new Rig();
        rig.Api.OnReport = call => call == 1 ? new ReportReceipt(5, LimitReached: true) : null;
        AddMany(rig.Outbox, 25);
        await rig.Cycle();
        Assert.That(rig.Api.Reports, Has.Count.EqualTo(1));
        Assert.That(rig.Outbox.Count, Is.EqualTo(20), "A Central guardou 5 e avisou do limite: os outros 20 ficam.");
        Assert.That(rig.Outbox.Snapshot()[0].Stage, Is.EqualTo("Parte 5"));

        await rig.Cycle();
        rig.Clock.Advance(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59));
        await rig.Cycle();
        Assert.That(rig.Api.Reports, Has.Count.EqualTo(1), "Pausa até o dia seguinte.");

        rig.Clock.Advance(Minute);
        await rig.Cycle();
        Assert.That(rig.Api.Reports, Has.Count.EqualTo(2));
        Assert.That(rig.Outbox.Count, Is.Zero);
    }

    [Test]
    public async Task AnUnpairedOrRevokedReaderKeepsEverythingAndSendsNothing()
    {
        var unpaired = new RuntimeTests.MemoryStore();
        unpaired.Clear();
        using (var rig = new Rig(store: unpaired))
        {
            AddMany(rig.Outbox, 2);
            await rig.Cycle();
            Assert.That(rig.Api.Reports, Is.Empty);
            Assert.That(rig.Outbox.Count, Is.EqualTo(2));
        }

        using var revoked = new Rig();
        revoked.SyncOverride = () => new SyncResponse { Protocolo = 1, ServidorEm = revoked.Clock.GetUtcNow(), Revogado = true, Cadastros = [], Comando = null, Recursos = ["relatar"] };
        AddMany(revoked.Outbox, 2);
        await revoked.Cycle();
        await revoked.Cycle();
        Assert.That(revoked.Runtime.Snapshot.Status, Is.EqualTo(AgentStatus.Revoked));
        Assert.That(revoked.Api.Reports, Is.Empty);
        Assert.That(revoked.Outbox.Count, Is.EqualTo(2), "Os avisos ficam guardados.");
    }

    [Test]
    public async Task AReportNeverDelaysAReadingAndAFailedOneNeverChangesTheDecisionOrTheScreen()
    {
        var id = Guid.NewGuid();
        var store = new RuntimeTests.MemoryStore();
        store.SaveTemplate(new StoredTemplate(id, Guid.NewGuid(), [7]));
        using var rig = new Rig([false, true], store, id);
        rig.Api.OnReport = _ => throw new CentralException("Sem conexão.");
        AddMany(rig.Outbox, 2);

        await rig.Cycle(); // dedo no leitor: identifica e não gasta tempo com avisos
        Assert.That(rig.Api.Identifications, Is.EqualTo(1));
        Assert.That(rig.Api.Reports, Is.Empty);
        Assert.That(rig.Runtime.Snapshot.Status, Is.EqualTo(AgentStatus.Authorized));

        var changes = rig.Changes.Count;
        await rig.Cycle(); // leitor parado: agora o aviso é tentado, e falha
        Assert.That(rig.Api.Reports, Has.Count.EqualTo(1));
        Assert.That(rig.Runtime.Snapshot.Status, Is.EqualTo(AgentStatus.Authorized), "O resultado na tela continua o mesmo.");
        Assert.That(rig.Changes, Has.Count.EqualTo(changes));
        Assert.That(rig.Outbox.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task ReportsAlsoGoOutWhenTheScannerDllWasNotChosenYet()
    {
        var store = new RuntimeTests.MemoryStore();
        var state = store.LoadState();
        store.SaveState(state with { Configuration = state.Configuration with { ScannerDllPath = "" } });
        using var rig = new Rig(store: store);
        AddMany(rig.Outbox, 1);
        await rig.Cycle();
        Assert.That(rig.Runtime.Snapshot.Status, Is.EqualTo(AgentStatus.Unconfigured));
        Assert.That(rig.Api.Reports, Has.Count.EqualTo(1));
        Assert.That(rig.Outbox.Count, Is.Zero);
    }

    [Test]
    public async Task ARuntimeWithoutAQueueNeverCallsRelatar()
    {
        var api = new RuntimeTests.FakeCentral { OnSync = () => Active(DateTimeOffset.UtcNow, relatar: true) };
        using var runtime = new AgentRuntime(new RuntimeTests.MemoryStore(), _ => api, _ => new SecurityAndCaptureTests.FakeScanner([]), new SecurityAndCaptureTests.FakeRecognizer(), TimeProvider.System);
        await runtime.RunCycleAsync(CancellationToken.None);
        Assert.That(api.Reports, Is.Empty);
    }
}

using System.Text;
using Futureplace.AgenteFS80H;
using NUnit.Framework;

namespace AgenteFS80H.Tests;

public class LogAndWatchdogTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    [Test, Category("SemTravar")]
    public void AWindowThatNeverAnswersIsLoggedOnceAfterThreeSecondsWithTheLastStep()
    {
        var clock = new RuntimeTests.ManualClock();
        var log = new RecordingLog();
        log.Step("ShowPage:Configurações");
        var posted = new List<Action>();
        using var watchdog = new UiWatchdog(log, action => { posted.Add(action); return true; }, clock);

        watchdog.Tick(); // 0 s: pede a primeira resposta e a janela nunca responde
        clock.Advance(Second);
        watchdog.Tick();
        clock.Advance(Second);
        watchdog.Tick();
        Assert.That(log.Lines, Is.Empty, "Com 2 s ainda não é travamento.");
        clock.Advance(Second);
        watchdog.Tick();
        Assert.That(log.Lines, Is.EqualTo(new[] { "TRAVADA: UI sem resposta há 3s, última etapa: ShowPage:Configurações" }));

        clock.Advance(TimeSpan.FromSeconds(5));
        watchdog.Tick();
        Assert.That(log.Lines, Has.Count.EqualTo(1), "Uma linha só por travamento.");
        Assert.That(posted, Has.Count.EqualTo(1), "Enquanto a janela não responde, o vigia não empilha mais pedidos.");

        posted[0](); // a janela voltou
        Assert.That(log.Lines, Has.Count.EqualTo(2));
        Assert.That(log.Lines[1], Is.EqualTo("INFO: UI voltou a responder depois de 8s"));
        watchdog.Tick();
        Assert.That(posted, Has.Count.EqualTo(2), "Depois de responder, volta a pedir a cada segundo.");
    }

    [Test, Category("SemTravar")]
    public void AWindowThatAnswersInTimeNeverWritesAnything()
    {
        var clock = new RuntimeTests.ManualClock();
        var log = new RecordingLog();
        var posts = 0;
        using var watchdog = new UiWatchdog(log, action => { posts++; action(); return true; }, clock);
        for (var tick = 0; tick < 30; tick++)
        {
            watchdog.Tick();
            clock.Advance(Second);
        }
        Assert.That(posts, Is.EqualTo(30), "Um pedido por segundo.");
        Assert.That(log.Lines, Is.Empty);
    }

    [Test, Category("SemTravar")]
    public void AnAnswerThatArrivesBeforeThreeSecondsIsNotATravamento()
    {
        var clock = new RuntimeTests.ManualClock();
        var log = new RecordingLog();
        var posted = new List<Action>();
        using var watchdog = new UiWatchdog(log, action => { posted.Add(action); return true; }, clock);
        watchdog.Tick();
        clock.Advance(TimeSpan.FromSeconds(2));
        watchdog.Tick();
        posted[0]();
        clock.Advance(TimeSpan.FromSeconds(2));
        watchdog.Tick();
        Assert.That(log.Lines, Is.Empty);
        Assert.That(posted, Has.Count.EqualTo(2));
    }

    [Test, Category("SemTravar")]
    public void APostThatFailsOrThrowsNeverBreaksTheWatchdogAndIsTriedAgain()
    {
        var clock = new RuntimeTests.ManualClock();
        var log = new RecordingLog();
        var attempts = 0;
        using var watchdog = new UiWatchdog(log, _ =>
        {
            attempts++;
            if (attempts == 1) return false; // janela ainda sem identificador
            if (attempts == 2) throw new InvalidOperationException("janela fechada");
            return true;
        }, clock);
        for (var tick = 0; tick < 3; tick++)
        {
            Assert.DoesNotThrow(watchdog.Tick);
            clock.Advance(Second);
        }
        Assert.That(attempts, Is.EqualTo(3));
        Assert.That(log.Lines, Is.Empty, "Sem janela para testar não há como dizer que travou.");
    }

    [Test]
    public void TheLogKeepsAtMostTwoSmallFilesAndNeverBreaksTheCaller()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "log-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var log = new FileDiagnostics(directory, maximumBytes: 4096))
            {
                for (var i = 0; i < 400; i++) log.Info($"linha de teste número {i} com algum texto para ocupar espaço");
                Assert.That(log.Flush(TimeSpan.FromSeconds(10)), Is.True);
            }
            var files = Directory.GetFiles(directory).Select(Path.GetFileName).Order().ToArray();
            Assert.That(files, Is.EqualTo(new[] { FileDiagnostics.PreviousName, FileDiagnostics.CurrentName }));
            foreach (var file in Directory.GetFiles(directory))
                Assert.That(new FileInfo(file).Length, Is.LessThanOrEqualTo(4096), Path.GetFileName(file));
            var current = File.ReadAllText(Path.Combine(directory, FileDiagnostics.CurrentName));
            Assert.That(current, Does.Contain("número 399"), "O fim do registro está no arquivo atual.");
            Assert.That(current, Does.Match(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} INFO    linha de teste"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public void TheLogDefaultsToTwoFilesOfHalfAMegabyte()
    {
        Assert.That(FileDiagnostics.DefaultMaximumBytes, Is.EqualTo(512 * 1024));
        Assert.That(FileDiagnostics.DefaultDirectory, Does.EndWith(Path.Combine("Futureplace", "AgenteFS80H", "logs")));
    }

    [Test]
    public void AnUnwritableFolderNeverThrowsOrBlocks()
    {
        // O "diretório" é um arquivo comum: criar a pasta do registro é impossível.
        var blocker = Path.Combine(TestContext.CurrentContext.WorkDirectory, "log-blocker-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "não é uma pasta");
        try
        {
            var log = new FileDiagnostics(Path.Combine(blocker, "logs"));
            Assert.DoesNotThrow(() =>
            {
                log.Info("uma linha");
                log.Error("contexto", new InvalidOperationException("erro"));
                log.Hang(TimeSpan.FromSeconds(4), "etapa");
                log.Flush(TimeSpan.FromSeconds(1));
                log.Dispose();
                log.Info("depois de fechar");
            });
        }
        finally { File.Delete(blocker); }
    }

    [Test]
    public void AnErrorLineHasTheTypeAndWhereButNeverTheExceptionMessage()
    {
        var log = new RecordingLog();
        Exception error;
        try
        {
            try { throw new InvalidOperationException("token-secreto-123 https://central.example/chave"); }
            catch (Exception inner) { throw new AggregateException("mensagem externa secreta", inner); }
        }
        catch (Exception caught) { error = caught; }
        log.Error("Erro na janela", error);
        var line = log.Lines.Single();
        Assert.That(line, Does.StartWith("ERRO: Erro na janela: System.AggregateException").And.Contain("System.InvalidOperationException"));
        Assert.That(line, Does.Not.Contain("token-secreto-123").And.Not.Contain("central.example").And.Not.Contain("mensagem externa secreta"));
        Assert.That(line, Does.Contain(nameof(LogAndWatchdogTests)), "O caminho no código aparece.");
    }

    [Test]
    public void TheFileLogWritesTheHangLineWithTheLastStep()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "log-hang-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var log = new FileDiagnostics(directory))
            {
                log.Step("ResizeContent");
                log.Hang(TimeSpan.FromSeconds(3.4), log.LastStep);
                log.Recovered(TimeSpan.FromSeconds(7));
                Assert.That(log.Flush(TimeSpan.FromSeconds(10)), Is.True);
            }
            var text = File.ReadAllText(Path.Combine(directory, FileDiagnostics.CurrentName), Encoding.UTF8);
            Assert.That(text, Does.Contain("TRAVADA UI sem resposta há 3s, última etapa: ResizeContent"));
            Assert.That(text, Does.Contain("UI voltou a responder depois de 7s"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private sealed class RecordingLog : AppDiagnostics
    {
        public List<string> Lines { get; } = [];
        protected override void Write(string level, string text) => Lines.Add(level + ": " + text);
    }
}

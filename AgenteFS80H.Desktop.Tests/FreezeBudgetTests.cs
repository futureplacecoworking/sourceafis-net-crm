using System.Threading;
using Futureplace.AgenteFS80H;
using Futureplace.AgenteFS80H.Desktop;
using NUnit.Framework;

namespace AgenteFS80H.Desktop.Tests;

/// <summary>
/// Contas, não cronômetro: abrir Configurações (e redimensionar a janela) não pode refazer o layout em cascata.
/// Cada teste conta quantas vezes o tamanho dos textos foi recalculado e quantos eventos de layout as páginas receberam.
/// Uma janela que trava no PC da recepção não trava no runner de 96 dpi, mas estoura estas contas.
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
[Category("SemTravar")]
public class FreezeBudgetTests
{
    // Abrir Configurações: poucos recálculos e poucos layouts. Antes da 1.4.1 eram dezenas por clique.
    private const int RunBudget = 3;
    private const int LayoutBudget = 12;

    [TestCase(1.0F, 0, 0, TestName = "ClickingSetupReaderStaysWithinTheBudget_normal_scale")]
    [TestCase(1.5F, 0, 0, TestName = "ClickingSetupReaderStaysWithinTheBudget_scaled_150_percent")]
    [TestCase(1.0F, 1366, 768, TestName = "ClickingSetupReaderStaysWithinTheBudget_1366x768")]
    [TestCase(1.0F, 1024, 768, TestName = "ClickingSetupReaderStaysWithinTheBudget_1024x768")]
    [TestCase(1.5F, 1366, 768, TestName = "ClickingSetupReaderStaysWithinTheBudget_scaled_150_percent_1366x768")]
    public void ClickingSetupReaderStaysWithinTheBudget(float scale, int width, int height)
    {
        using var ui = new UiFixture(paired: false);
        if (scale != 1F)
        {
            ui.Form.Scale(new SizeF(scale, scale));
            UiFixture.Pump();
        }
        if (width > 0)
        {
            ui.Form.ClientSize = new Size(width, height);
            UiFixture.Pump();
        }
        var counter = new LayoutCounter(ui);
        var runs = ui.Form.ResizeContentRuns;

        ui.Click("setupReader");

        Assert.That(ui.Find<Control>("settingsPage").Visible, Is.True);
        Assert.That(ui.Find<Control>("readingPage").Visible, Is.False);
        Assert.That(ui.Form.ResizeContentRuns - runs, Is.LessThanOrEqualTo(RunBudget), "Recálculos do tamanho dos textos ao abrir Configurações.");
        Assert.That(counter.Settings, Is.LessThanOrEqualTo(LayoutBudget), "Layouts da página Configurações.");
        Assert.That(counter.Reading, Is.LessThanOrEqualTo(LayoutBudget), "Layouts da página Leitura.");
        Assert.That(ui.Store.Writes, Is.Zero);
    }

    [TestCase(1.0F)]
    [TestCase(1.5F)]
    public void ResizingBeforeOpeningSettingsDoesNotLeaveTheWorkForTheClick(float scale)
    {
        using var ui = new UiFixture(paired: false);
        if (scale != 1F) ui.Form.Scale(new SizeF(scale, scale));
        ui.Form.ClientSize = new Size(1366, 768); // a janela muda enquanto Configurações está escondida
        UiFixture.Pump();
        var counter = new LayoutCounter(ui);
        var runs = ui.Form.ResizeContentRuns;
        ui.Click("setupReader");
        Assert.That(ui.Form.ResizeContentRuns - runs, Is.Zero, "O tamanho já tinha sido ajustado para as duas páginas na hora de redimensionar.");
        Assert.That(counter.Settings, Is.LessThanOrEqualTo(LayoutBudget));
    }

    [Test]
    public void CallingResizeContentAgainChangesNothing()
    {
        using var ui = new UiFixture(paired: false);
        ui.Form.ClientSize = new Size(1100, 800);
        UiFixture.Pump();
        ui.Form.ResizeContent();
        var runs = ui.Form.ResizeContentRuns;
        var widths = WrappingWidths(ui);
        var counter = new LayoutCounter(ui);

        ui.Form.ResizeContent();
        ui.Form.ResizeContent();

        Assert.That(ui.Form.ResizeContentRuns, Is.EqualTo(runs), "Nada mudou, nada é recalculado.");
        Assert.That(WrappingWidths(ui), Is.EqualTo(widths));
        Assert.That(counter.Settings + counter.Reading, Is.Zero, "Sem recalcular, nenhuma página refaz o layout.");
    }

    [Test]
    public void ResizingTheWindowFiveTimesStaysBounded()
    {
        using var ui = new UiFixture(paired: false);
        var counter = new LayoutCounter(ui);
        foreach (var size in new[] { new Size(1366, 768), new Size(1024, 768), new Size(1280, 720), new Size(900, 700), new Size(1200, 800) })
        {
            var runs = ui.Form.ResizeContentRuns;
            counter.Reset();
            ui.Form.ClientSize = size;
            UiFixture.Pump();
            Assert.That(ui.Form.ResizeContentRuns - runs, Is.LessThanOrEqualTo(2), $"Recálculos ao ir para {size}.");
            Assert.That(counter.Reading, Is.LessThanOrEqualTo(LayoutBudget), $"Layouts da página Leitura ao ir para {size}.");
            Assert.That(counter.Settings, Is.LessThanOrEqualTo(LayoutBudget), $"Layouts da página Configurações ao ir para {size}.");
        }
        Assert.That(ui.Store.Writes, Is.Zero);
    }

    [Test]
    public void TheClientScreenResizesWithinTheBudgetAndIgnoresRepeatedCalls()
    {
        using var ui = new UiFixture();
        ui.Click("clientScreenButton");
        var client = Application.OpenForms.Cast<Form>().OfType<ClientScreenForm>().Single();
        client.FormBorderStyle = FormBorderStyle.Sizable;
        client.WindowState = FormWindowState.Normal;
        UiFixture.Pump();
        foreach (var size in new[] { new Size(900, 650), new Size(1000, 900), new Size(800, 560), new Size(1200, 700), new Size(900, 650) })
        {
            var runs = client.ResizeContentRuns;
            client.ClientSize = size;
            UiFixture.Pump();
            Assert.That(client.ResizeContentRuns - runs, Is.LessThanOrEqualTo(2), $"Recálculos da tela do cliente ao ir para {size}.");
        }
        var settled = client.ResizeContentRuns;
        client.ResizeContent();
        client.ResizeContent();
        Assert.That(client.ResizeContentRuns, Is.EqualTo(settled), "A tela do cliente também ignora chamadas que não mudam nada.");
    }

    [Test]
    public void WidthsScaleWithTheDisplayAndNeverDependOnTheScrollBarState()
    {
        // Contas feitas à mão: 100% (barra de 17 px) e 150% (barra de 26 px, margens e caixas 1,5 vez maiores).
        Assert.That(ContentWidths.Compute(formWidth: 1040, pageWidth: 984, scrollBarWidth: 17, scale: 1F),
            Is.EqualTo(new ContentWidths(Action: 976, Full: 899, Checklist: 639, Hero: 727)));
        Assert.That(ContentWidths.Compute(formWidth: 1560, pageWidth: 1476, scrollBarWidth: 26, scale: 1.5F),
            Is.EqualTo(new ContentWidths(Action: 1464, Full: 1348, Checklist: 958, Hero: 1090)));
        // Janela minúscula: valem os mínimos, também proporcionais à escala (320, 240, 280 e 200 px a 100%).
        Assert.That(ContentWidths.Compute(100, 100, 17, 1F), Is.EqualTo(new ContentWidths(240, 320, 200, 280)));
        Assert.That(ContentWidths.Compute(100, 100, 26, 1.5F), Is.EqualTo(new ContentWidths(360, 480, 300, 420)));
    }

    [Test]
    public void TheHiddenPageIsNotRenderedUntilItIsOpened()
    {
        using var ui = new UiFixture();
        var connection = ui.Find<Control>("diagnosticConnection");
        var reading = ui.Find<Control>("readingTitle");
        var initial = connection.Text;
        Assert.That(initial, Does.Contain("Aguardando"));

        ui.Snapshot(new AgentSnapshot(AgentStatus.Ready, "Coloque o dedo cadastrado no leitor.") { Hint = ReaderHint.PlaceFinger });
        Assert.That(reading.Text, Is.EqualTo("Coloque o dedo no leitor"), "A página Leitura está à vista e acompanha o leitor.");
        Assert.That(connection.Text, Is.EqualTo(initial), "O diagnóstico está escondido: não é refeito a cada estado novo do leitor.");

        ui.Form.ShowSettings();
        UiFixture.Pump();
        Assert.That(connection.Text, Does.Contain("respondeu"), "Ao abrir Configurações, o diagnóstico já mostra o estado atual.");

        ui.Snapshot(new AgentSnapshot(AgentStatus.Offline, "Sem conexão com a Central."));
        Assert.That(connection.Text, Does.Contain("Sem resposta"), "Agora o diagnóstico está à vista e acompanha o leitor.");
        Assert.That(reading.Text, Is.EqualTo("Coloque o dedo no leitor"), "A página Leitura escondida não é refeita.");

        ui.Click("navigationReading");
        Assert.That(reading.Text, Is.EqualTo("Sem conexão com a Central"), "Ao voltar para Leitura, ela já mostra o estado atual.");
        Assert.That(ui.Store.Writes, Is.Zero);
    }

    [Test]
    public void AThousandRuntimeChangesFromAnotherThreadReachTheWindowAsOneUpdate()
    {
        using var ui = new UiFixture();
        var applied = 0;
        var queue = new LatestOnlyQueue<AgentSnapshot>(
            action => { ui.Form.BeginInvoke(action); return true; },
            snapshot => { applied++; ui.Form.ShowSnapshot(snapshot); });
        Action<AgentSnapshot> changed = queue.Submit; // faz o papel de runtime.Changed

        Task.Run(() =>
        {
            for (var i = 1; i <= 1000; i++) changed(new AgentSnapshot(AgentStatus.Capturing, $"mensagem {i}"));
        }).Wait();
        Assert.That(applied, Is.Zero, "Nada chega à janela enquanto ela não processa mensagens.");

        UiFixture.Pump();

        Assert.That(applied, Is.InRange(1, 2));
        Assert.That(ui.Find<Control>("readingInstruction").Text, Is.EqualTo("mensagem 1000"), "Vale o estado mais recente.");
    }

    [Test]
    public void AFailedPostNeverBlocksTheNextUpdateAndNothingArrivesTwice()
    {
        var accept = false;
        var posted = new List<Action>();
        var applied = new List<string>();
        var queue = new LatestOnlyQueue<string>(action => { if (!accept) return false; posted.Add(action); return true; }, applied.Add);

        queue.Submit("a"); // janela sem identificador: o pedido não foi entregue
        accept = true;
        queue.Submit("b");
        queue.Submit("c");
        Assert.That(posted, Has.Count.EqualTo(1), "Um pedido pendente por vez.");
        posted[0]();
        Assert.That(applied, Is.EqualTo(new[] { "c" }));

        queue.Submit("d");
        Assert.That(posted, Has.Count.EqualTo(2), "Depois de atender, um valor novo pede outra atualização.");
        posted[1]();
        posted[1](); // atender de novo não repete nada
        Assert.That(applied, Is.EqualTo(new[] { "c", "d" }));
    }

    [Test]
    public void TheWindowMarksItsStepsForTheFreezeLog()
    {
        var log = new RecordingLog();
        var previous = AppLog.Current;
        AppLog.Current = log;
        try
        {
            using var ui = new UiFixture();
            ui.Snapshot(AgentStatus.Ready, "Coloque o dedo cadastrado no leitor.");
            ui.Form.ShowSettings();
            ui.Form.ReloadConfiguration();
            ui.Click("clientScreenButton");
            Assert.That(log.Steps, Is.SupersetOf(new[] { "ShowSnapshot", "ShowPage:Configurações", "ReloadConfiguration", "ShowClientScreen" }));
            Assert.That(log.Steps, Does.Contain("ResizeContent"), "O tamanho dos textos é calculado ao abrir a janela.");
            Assert.That(log.Lines, Has.Some.Contain("Página Configurações"));
            Assert.That(log.LastStep, Is.Not.EqualTo("início"));
        }
        finally { AppLog.Current = previous; }
    }

    private static int[] WrappingWidths(UiFixture ui) => UiFixture.Tree(ui.Form).OfType<Label>().Select(label => label.MaximumSize.Width).ToArray();

    private sealed class LayoutCounter
    {
        public int Settings { get; private set; }
        public int Reading { get; private set; }
        public LayoutCounter(UiFixture ui)
        {
            ui.Find<Control>("settingsPage").Layout += (_, _) => Settings++;
            ui.Find<Control>("readingPage").Layout += (_, _) => Reading++;
        }
        public void Reset() { Settings = 0; Reading = 0; }
    }

    private sealed class RecordingLog : AppDiagnostics
    {
        public List<string> Lines { get; } = [];
        public List<string> Steps { get; } = [];
        public override void Step(string name)
        {
            Steps.Add(name);
            base.Step(name);
        }
        protected override void Write(string level, string text) => Lines.Add(level + ": " + text);
    }
}

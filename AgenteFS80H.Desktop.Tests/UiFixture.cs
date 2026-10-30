using Futureplace.AgenteFS80H;
using Futureplace.AgenteFS80H.Desktop;
using NUnit.Framework;

namespace AgenteFS80H.Desktop.Tests;

// Uses synthetic in-memory state. Never start AgentRuntime.RunAsync or touch USB/network.
internal sealed class UiFixture : IDisposable
{
    public const string SyntheticToken = "ui-test-token-never-render";
    public const string SyntheticKey = "sb_publishable_ui-test-never-render";
    public readonly MemoryStore Store;
    public readonly AgentRuntime Runtime;
    public readonly MainForm Form;

    public UiFixture(bool paired = true, bool scanner = true)
    {
        Store = new MemoryStore(new AgentState
        {
            Configuration = new AgentConfiguration
            {
                CentralUrl = "https://central.example",
                ProjectUrl = "https://project.supabase.co",
                PublicApiKey = SyntheticKey,
                DeviceId = paired ? Guid.Parse("10000000-0000-0000-0000-000000000001") : Guid.Empty,
                DeviceToken = paired ? SyntheticToken : "",
                ScannerDllPath = scanner ? @"C:\Futronic\ftrScanAPI.dll" : ""
            }
        });
        Runtime = new AgentRuntime(Store,
            _ => throw new AssertionException("A UI de teste não pode iniciar comunicação com a Central."),
            _ => throw new AssertionException("A UI de teste não pode carregar um leitor físico."),
            new UnusedRecognizer(), TimeProvider.System);
        TestContext.Progress.WriteLine($"UI {TestContext.CurrentContext.Test.Name}: criar janela");
        Form = new MainForm(Runtime, CancellationToken.None);
        TestContext.Progress.WriteLine($"UI {TestContext.CurrentContext.Test.Name}: mostrar janela");
        Form.Show();
        Pump();
        TestContext.Progress.WriteLine($"UI {TestContext.CurrentContext.Test.Name}: janela pronta");
    }

    public T Find<T>(string name) where T : Control
    {
        var control = Form.Controls.Find(name, true).SingleOrDefault();
        Assert.That(control, Is.InstanceOf<T>(), $"Controle acessível esperado: {name} ({typeof(T).Name}).");
        return (T)control!;
    }

    public void Click(string name)
    {
        Find<Button>(name).PerformClick();
        Pump();
    }

    public void Snapshot(AgentStatus status, string message)
    {
        Form.ShowSnapshot(new AgentSnapshot(status, message));
        Pump();
    }

    public static void Pump()
    {
        Application.DoEvents();
    }

    public static IEnumerable<Control> Tree(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
            foreach (var nested in Tree(child))
                yield return nested;
    }

    public static string VisibleText(Control root) => string.Join("\n", Tree(root).Where(c => c.Visible).Select(c => c.Text));

    public void Dispose()
    {
        Form.AllowClose = true;
        Form.Close();
        Form.Dispose();
        Runtime.Dispose();
        Pump();
    }

    internal sealed class MemoryStore(AgentState initial) : ILocalStore
    {
        private AgentState state = initial;
        private readonly StoredTemplate template = new(
            Guid.Parse("20000000-0000-0000-0000-000000000002"),
            Guid.Parse("30000000-0000-0000-0000-000000000003"), [7, 8, 9]);
        private bool hasTemplate = true;
        public int Writes { get; private set; }
        public AgentState LoadState() => state;
        public void SaveState(AgentState value) { Writes++; state = value; }
        public IReadOnlyList<StoredTemplate> LoadTemplates() => hasTemplate ? [template] : [];
        public IReadOnlyList<Guid> ListTemplateIds() => hasTemplate ? [template.CadastroId] : [];
        public void DeleteAllTemplates() { Writes++; hasTemplate = false; }
        public void SaveTemplate(StoredTemplate value) => throw new AssertionException("Navegação não cadastra digital.");
        public void DeleteTemplate(Guid id) { Writes++; hasTemplate = false; }
        public void Clear() { Writes++; hasTemplate = false; state = new(); }
    }

    private sealed class UnusedRecognizer : IFingerprintRecognizer
    {
        public byte[] Extract(FingerprintFrame image) => throw new AssertionException("A UI não captura imagens.");
        public double Score(byte[] probe, byte[] candidate) => throw new AssertionException("A UI não compara digitais.");
    }
}

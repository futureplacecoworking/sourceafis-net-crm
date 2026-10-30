namespace Futureplace.AgenteFS80H.Desktop;

internal sealed partial class MainForm
{
    private readonly Label diagnosticPairing = TextLabel("");
    private readonly Label diagnosticLibrary = TextLabel("");
    private readonly Label diagnosticConnection = TextLabel("");
    private readonly Label diagnosticReader = TextLabel("");
    private readonly Label diagnosticGuidance = TextLabel("");
    private AgentSnapshot? diagnosticSnapshot;
    private DiagnosticEntry? inspectedLibrary;
    private string? inspectedLibraryPath;
    private Control? diagnosticCard;

    private Control BuildDiagnosticsCard()
    {
        var content = Stack();
        Add(content, Wrap("Diagnóstico automático", 16, true));
        foreach (var (label, name) in new[]
        {
            (diagnosticPairing, "diagnosticPairing"), (diagnosticLibrary, "diagnosticLibrary"),
            (diagnosticConnection, "diagnosticConnection"), (diagnosticReader, "diagnosticReader"),
            (diagnosticGuidance, "diagnosticGuidance")
        })
        {
            label.Name = name;
            label.AccessibleRole = AccessibleRole.StaticText;
            wrappingLabels.Add(label);
            Add(content, label);
        }
        diagnosticGuidance.ForeColor = CentralTheme.Muted;
        var refresh = new CentralButton("Atualizar diagnóstico") { Name = "diagnosticRefreshButton" };
        refresh.Click += (_, _) => { if (!exiting) ReloadDiagnosticsConfiguration(); };
        Add(content, Buttons(refresh));
        diagnosticCard = Card(content);
        diagnosticCard.Name = "diagnosticsCard";
        return diagnosticCard;
    }

    private void ReloadDiagnosticsConfiguration()
    {
        inspectedLibraryPath = runtime.Configuration.ScannerDllPath;
        inspectedLibrary = ReaderDiagnostics.InspectLibrary(inspectedLibraryPath);
        RenderDiagnostics(diagnosticSnapshot ?? runtime.Snapshot);
    }

    private void UpdateDiagnostics(AgentSnapshot snapshot)
    {
        diagnosticSnapshot = snapshot;
        if (inspectedLibrary is null || inspectedLibraryPath != runtime.Configuration.ScannerDllPath)
        {
            inspectedLibraryPath = runtime.Configuration.ScannerDllPath;
            inspectedLibrary = ReaderDiagnostics.InspectLibrary(inspectedLibraryPath);
        }
        RenderDiagnostics(snapshot);
    }

    private void RenderDiagnostics(AgentSnapshot snapshot)
    {
        if (diagnosticCard is null || diagnosticCard.IsDisposed) return;
        var state = ReaderDiagnostics.Create(runtime.Configuration, snapshot,
            inspectedLibrary ?? ReaderDiagnostics.InspectLibrary(runtime.Configuration.ScannerDllPath));
        diagnosticCard.SuspendLayout();
        try
        {
            ShowDiagnostic(diagnosticPairing, "Pareamento", state.Pairing);
            ShowDiagnostic(diagnosticLibrary, "Arquivo de captura", state.Library);
            ShowDiagnostic(diagnosticConnection, "Conexão", state.Connection);
            ShowDiagnostic(diagnosticReader, "Leitor USB e driver", state.Reader);
            diagnosticGuidance.Text = state.Guidance;
        }
        finally { diagnosticCard.ResumeLayout(true); }
    }

    private static void ShowDiagnostic(Label label, string title, DiagnosticEntry entry)
    {
        label.Text = title + ": " + entry.Message;
        label.ForeColor = entry.Tone switch
        {
            DiagnosticTone.Healthy => CentralTheme.Success,
            DiagnosticTone.Attention => CentralTheme.Warning,
            DiagnosticTone.Error => CentralTheme.Danger,
            _ => CentralTheme.Muted
        };
    }
}

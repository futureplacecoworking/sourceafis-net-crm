using System.Diagnostics;

namespace Futureplace.AgenteFS80H.Desktop;

internal enum ChecklistState { Pending, Done, Attention }

/// <summary>Uma etapa da preparação, já pronta para a tela: estado, explicação e ação.</summary>
internal sealed record ChecklistItem(ChecklistState State, string Detail, string? Action);

internal sealed partial class MainForm
{
    private const string WebViewDownload = "https://developer.microsoft.com/microsoft-edge/webview2/";
    private sealed record ChecklistRow(Label Mark, Label Detail, CentralButton Action);
    private readonly Dictionary<string, ChecklistRow> checklist = [];
    private ClientWebFailure? clientWebFailure;
    private Control? checklistCard;

    private Control BuildChecklistCard()
    {
        var content = Stack();
        Add(content, Wrap("Preparação do leitor", 15, true));
        var intro = Wrap("Cada etapa ganha um ✓ quando está pronta. Use o botão ao lado para resolver o que falta.");
        intro.ForeColor = CentralTheme.Muted;
        Add(content, intro);
        var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 3, Margin = new Padding(0, 8, 0, 0) };
        table.SuspendLayout();
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddChecklistRow(table, "Central", "1. Central conectada", "Ver diagnóstico", () => ShowPage(true));
        AddChecklistRow(table, "Pairing", "2. Pareado", "Parear agora", () => { ShowPage(true); pairingCode.Focus(); });
        AddChecklistRow(table, "Library", "3. Arquivo do leitor (DLL) selecionado", "Selecionar DLL", async () => await ChooseScannerAsync());
        AddChecklistRow(table, "Reader", "4. Leitor FS80H conectado", "Site da Futronic", OpenDownloads);
        AddChecklistRow(table, "Client", "5. Tela do cliente aberta", "Abrir tela do cliente", ClientChecklistAction);
        Add(content, table);
        checklistCard = Card(content);
        checklistCard.Name = "setupChecklist";
        return checklistCard;
    }

    private void AddChecklistRow(TableLayoutPanel table, string key, string title, string action, Action run)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var mark = new Label
        {
            Name = "check" + key + "Mark", AutoSize = false, Size = new Size(32, 32), TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI Symbol", 15F, FontStyle.Bold, GraphicsUnit.Point), Margin = new Padding(0, 4, 8, 8),
            AccessibleRole = AccessibleRole.StaticText
        };
        var text = Stack();
        var heading = TextLabel(title, 11, true);
        heading.Margin = new Padding(0, 4, 0, 2);
        var detail = TextLabel("", 10);
        detail.Name = "check" + key;
        detail.ForeColor = CentralTheme.Muted;
        Add(text, heading);
        Add(text, detail);
        var button = new CentralButton(action) { Name = "check" + key + "Action", Anchor = AnchorStyles.Right | AnchorStyles.Top, Margin = new Padding(8, 0, 0, 8) };
        button.Click += (_, _) => { if (!exiting) run(); };
        table.Controls.Add(mark, 0, row);
        table.Controls.Add(text, 1, row);
        table.Controls.Add(button, 2, row);
        checklist[key] = new ChecklistRow(mark, detail, button);
    }

    private void ResizeChecklist(int width)
    {
        foreach (var row in checklist.Values) row.Detail.MaximumSize = new Size(Math.Max(200, width - 260), 0);
    }

    private void ClientChecklistAction()
    {
        if (clientWebFailure is not null && clientScreen is { IsDisposed: false, Visible: true }) OpenAddress(WebViewDownload,
            "Abra developer.microsoft.com/microsoft-edge/webview2 no navegador e instale o Evergreen Bootstrapper.");
        else ShowClientScreen();
    }

    private void RenderChecklistNow() => RenderChecklist(displayedSnapshot ?? runtime.Snapshot);

    private void RenderChecklist(AgentSnapshot snapshot)
    {
        if (checklist.Count == 0 || exiting) return;
        var items = ChecklistItems(runtime.Configuration, snapshot,
            inspectedLibrary ?? ReaderDiagnostics.InspectLibrary(runtime.Configuration.ScannerDllPath),
            clientScreen is { IsDisposed: false, Visible: true }, clientScreen?.UsingWebPage ?? false, clientWebFailure, DateTimeOffset.UtcNow);
        foreach (var (key, item) in items)
        {
            if (!checklist.TryGetValue(key, out var row)) continue;
            SetText(row.Mark, item.State switch { ChecklistState.Done => "✓", ChecklistState.Attention => "!", _ => "○" });
            row.Mark.ForeColor = item.State switch
            {
                ChecklistState.Done => CentralTheme.Success,
                ChecklistState.Attention => CentralTheme.Warning,
                _ => CentralTheme.Muted
            };
            row.Mark.AccessibleName = item.State switch
            {
                ChecklistState.Done => "Pronto",
                ChecklistState.Attention => "Precisa de atenção",
                _ => "Falta fazer"
            };
            SetText(row.Detail, item.Detail);
            row.Action.Visible = item.Action is not null;
            if (item.Action is not null && row.Action.Text != item.Action)
            {
                row.Action.Text = item.Action;
                row.Action.AccessibleName = item.Action;
            }
        }
    }

    /// <summary>As cinco etapas da preparação, calculadas só com o estado atual (sem abrir USB nem rede).</summary>
    internal static IReadOnlyList<(string Key, ChecklistItem Item)> ChecklistItems(AgentConfiguration configuration, AgentSnapshot snapshot,
        DiagnosticEntry library, bool clientOpen, bool clientWebPage, ClientWebFailure? clientFailure, DateTimeOffset now)
    {
        var diagnostics = ReaderDiagnostics.Create(configuration, snapshot, library);
        var revoked = snapshot.Status == AgentStatus.Revoked;
        var recentContact = snapshot.LastContactAt is { } contact && now - contact <= TimeSpan.FromMinutes(2)
            && snapshot.Status is not (AgentStatus.Offline or AgentStatus.Revoked);

        ChecklistItem central = !configuration.IsPaired
            ? new(ChecklistState.Pending, "Depois do pareamento, o leitor conversa com a Central sozinho.", null)
            : revoked ? new(ChecklistState.Attention, "A Central bloqueou este leitor. Veja a etapa 2.", null)
            : recentContact ? new(ChecklistState.Done, $"A Central respondeu às {snapshot.LastContactAt!.Value.ToLocalTime():HH:mm}.", null)
            : snapshot.Status == AgentStatus.Offline ? new(ChecklistState.Attention, "Sem resposta da Central agora. Confira a internet deste computador; o leitor tenta de novo sozinho.", "Ver diagnóstico")
            : new(ChecklistState.Pending, "Aguardando a primeira resposta da Central.", "Ver diagnóstico");

        ChecklistItem pairing = revoked
            ? new(ChecklistState.Attention, "A Central desligou este leitor. Gere um novo código em Digitais › Central biométrica e pareie de novo.", "Parear agora")
            : configuration.IsPaired
                ? new(ChecklistState.Done, string.IsNullOrWhiteSpace(configuration.DeviceName) ? "Ligado à Central." : $"Ligado à Central como «{configuration.DeviceName}».", null)
                : new(ChecklistState.Pending, "Gere o código na Central em Digitais › Central biométrica e cole em Configurações.", "Parear agora");

        ChecklistItem dll = library.Tone == DiagnosticTone.Healthy
            ? new(ChecklistState.Done, "Arquivo de 64 bits do fabricante selecionado.", null)
            : string.IsNullOrWhiteSpace(configuration.ScannerDllPath)
                ? new(ChecklistState.Pending, "Selecione a ftrScanAPI.dll de 64 bits do fabricante (fica fora do instalador).", "Selecionar DLL")
                : new(ChecklistState.Attention, library.Message, "Selecionar DLL");

        ChecklistItem reader = diagnostics.Reader.Tone == DiagnosticTone.Healthy
            ? new(ChecklistState.Done, "O leitor respondeu na última leitura.", null)
            : diagnostics.Reader.Tone == DiagnosticTone.Attention
                ? new(ChecklistState.Attention, "Não foi possível usar o leitor. Confira o cabo USB, o driver Futronic e o arquivo selecionado.", "Site da Futronic")
                : new(ChecklistState.Pending, "Aguardando o leitor responder. Conecte o FS80H no USB e instale o driver do fabricante.", "Site da Futronic");

        ChecklistItem client = !clientOpen
            ? new(ChecklistState.Pending, "Abra a tela do cliente no monitor em pé, atrás do leitor.", "Abrir tela do cliente")
            : clientWebPage ? new(ChecklistState.Done, "Aberta no outro monitor.", "Abrir tela do cliente")
            : clientFailure == ClientWebFailure.RuntimeMissing
                ? new(ChecklistState.Attention, "Aberta no modo simples: falta o Microsoft Edge WebView2 Runtime. Instale e abra a tela de novo.", "Baixar WebView2")
            : clientFailure == ClientWebFailure.Failed
                ? new(ChecklistState.Attention, "Aberta no modo simples: a tela nova não abriu. Feche e abra de novo; se continuar, reinstale o WebView2 Runtime.", "Baixar WebView2")
                : new(ChecklistState.Done, "Aberta no modo simples.", "Abrir tela do cliente");

        return [("Central", central), ("Pairing", pairing), ("Library", dll), ("Reader", reader), ("Client", client)];
    }

    private void ShowClientWebFailure(ClientWebFailure reason)
    {
        clientWebFailure = reason;
        actionMessage.ForeColor = CentralTheme.Warning;
        actionMessage.Text = reason == ClientWebFailure.RuntimeMissing
            ? "A tela do cliente abriu no modo simples porque falta o Microsoft Edge WebView2 Runtime neste computador. Para a tela nova, use “Baixar WebView2” na Preparação do leitor, instale o “Evergreen Bootstrapper” gratuito da Microsoft e abra a Tela do cliente de novo."
            : "A tela nova do cliente não abriu e ficou no modo simples. Feche e abra a Tela do cliente de novo. Se continuar, use “Baixar WebView2” na Preparação do leitor e reinstale o Microsoft Edge WebView2 Runtime.";
        RenderChecklistNow();
    }

    private void OpenAddress(string address, string fallback)
    {
        try { Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }); }
        catch
        {
            actionMessage.ForeColor = CentralTheme.Warning;
            actionMessage.Text = fallback;
        }
    }
}

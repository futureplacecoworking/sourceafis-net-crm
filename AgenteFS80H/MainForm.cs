using System.Diagnostics;
using System.ComponentModel;

namespace Futureplace.AgenteFS80H.Desktop;

internal sealed class MainForm : Form
{
    private readonly AgentRuntime runtime;
    private readonly CancellationToken lifetime;
    private readonly TextBox centralUrl = new();
    private readonly TextBox pairingCode = new();
    private readonly TextBox scannerPath = new();
    private readonly Label pairingState = new();
    private readonly Label statusTitle = new();
    private readonly Label statusMessage = new();
    private readonly Label actionMessage = new();
    private readonly Button pairButton = new();
    private readonly Button clearButton = new();
    private readonly Button chooseScannerButton = new();
    private readonly CheckBox startup = new();
    private bool busy;
    private bool changingStartup;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AllowClose { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Task PendingOperation { get; private set; } = Task.CompletedTask;

    public MainForm(AgentRuntime runtime, CancellationToken lifetime)
    {
        this.runtime = runtime;
        this.lifetime = lifetime;
        Text = "Leitor de digitais — Central";
        Icon = (Icon)SystemIcons.Application.Clone();
        Font = new Font("Segoe UI", 10F);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(680, 760);
        ClientSize = new Size(700, 780);
        BackColor = Color.FromArgb(246, 247, 249);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 1,
            Padding = new Padding(24),
            RowCount = 7
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = "Leitor de digitais",
            Font = new Font(Font.FontFamily, 20F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        });
        layout.Controls.Add(WrappedLabel(
            "Este computador captura e compara as digitais. A Central decide a autorização. Nenhuma porta é acionada por este programa.",
            0, 0, 0, 16));
        layout.Controls.Add(BuildStatusGroup());
        layout.Controls.Add(BuildPairingGroup());
        layout.Controls.Add(BuildScannerGroup());

        var preferences = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 12, 0, 0)
        };
        startup.Text = "Iniciar ao entrar no Windows";
        startup.AutoSize = true;
        startup.CheckedChanged += StartupChanged;
        preferences.Controls.Add(startup);
        preferences.Controls.Add(WrappedLabel(
            "Fechar esta janela mantém o leitor na bandeja do Windows. Para parar, clique no ícone com o botão direito e escolha Encerrar agente.",
            0, 8, 0, 0));
        layout.Controls.Add(preferences);
        actionMessage.AutoSize = true;
        actionMessage.MaximumSize = new Size(610, 0);
        actionMessage.ForeColor = Color.FromArgb(131, 40, 35);
        actionMessage.Margin = new Padding(0, 12, 0, 0);
        actionMessage.AccessibleRole = AccessibleRole.Alert;
        layout.Controls.Add(actionMessage);
        Controls.Add(layout);
        FormClosing += (_, e) =>
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
        Shown += (_, _) => ReloadConfiguration();
        ReloadConfiguration();
        ShowSnapshot(runtime.Snapshot);
    }

    private Control BuildStatusGroup()
    {
        var group = Group("Estado do leitor");
        statusTitle.Font = new Font(Font.FontFamily, 13F, FontStyle.Bold);
        statusTitle.AutoSize = true;
        statusTitle.Margin = new Padding(0, 0, 0, 8);
        statusMessage.AutoSize = true;
        statusMessage.MaximumSize = new Size(590, 0);
        statusMessage.AccessibleRole = AccessibleRole.Alert;
        var content = Stack();
        content.Controls.Add(statusTitle);
        content.Controls.Add(statusMessage);
        group.Controls.Add(content);
        return group;
    }

    private Control BuildPairingGroup()
    {
        var group = Group("1. Conectar à Central");
        var content = Stack();
        pairingState.AutoSize = true;
        pairingState.Margin = new Padding(0, 0, 0, 10);
        content.Controls.Add(pairingState);
        content.Controls.Add(FieldLabel("Endereço da Central"));
        centralUrl.Width = 580;
        centralUrl.AccessibleName = "Endereço da Central";
        centralUrl.PlaceholderText = "https://central.futureplacecoworking.com.br";
        content.Controls.Add(centralUrl);
        content.Controls.Add(FieldLabel("Código de pareamento gerado na Central"));
        pairingCode.Width = 300;
        pairingCode.MaxLength = 128;
        pairingCode.UseSystemPasswordChar = true;
        pairingCode.AccessibleName = "Código de pareamento";
        content.Controls.Add(pairingCode);
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = true, Margin = new Padding(0, 12, 0, 0)
        };
        pairButton.Text = "Parear este computador";
        pairButton.AutoSize = true;
        pairButton.Padding = new Padding(8, 3, 8, 3);
        pairButton.Click += async (_, _) => await PairAsync();
        clearButton.Text = "Limpar pareamento";
        clearButton.AutoSize = true;
        clearButton.Padding = new Padding(8, 3, 8, 3);
        clearButton.Click += async (_, _) => await ClearPairingAsync();
        buttons.Controls.Add(pairButton);
        buttons.Controls.Add(clearButton);
        content.Controls.Add(buttons);
        group.Controls.Add(content);
        return group;
    }

    private Control BuildScannerGroup()
    {
        var group = Group("2. Preparar o Futronic FS80H");
        var content = Stack();
        content.Controls.Add(WrappedLabel(
            "Instale o driver assinado do fabricante. Depois selecione a DLL de captura oficial para Windows de 64 bits (ftrScanAPI.dll).",
            0, 0, 0, 8));
        scannerPath.Width = 580;
        scannerPath.ReadOnly = true;
        scannerPath.AccessibleName = "Caminho da DLL oficial de captura";
        scannerPath.PlaceholderText = "DLL oficial ainda não selecionada";
        content.Controls.Add(scannerPath);
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = true, Margin = new Padding(0, 10, 0, 0)
        };
        chooseScannerButton.Text = "Selecionar DLL oficial…";
        chooseScannerButton.AutoSize = true;
        chooseScannerButton.Padding = new Padding(8, 3, 8, 3);
        chooseScannerButton.Click += async (_, _) => await ChooseScannerAsync();
        var downloads = new Button
        {
            Text = "Downloads do fabricante", AutoSize = true, Padding = new Padding(8, 3, 8, 3)
        };
        downloads.Click += (_, _) => OpenDownloads();
        buttons.Controls.Add(chooseScannerButton);
        buttons.Controls.Add(downloads);
        content.Controls.Add(buttons);
        group.Controls.Add(content);
        return group;
    }

    private static GroupBox Group(string title) => new()
    {
        Text = title, Dock = DockStyle.Top, AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(14, 22, 14, 14), Margin = new Padding(0, 0, 0, 12)
    };

    private static FlowLayoutPanel Stack() => new()
    {
        FlowDirection = FlowDirection.TopDown, WrapContents = false,
        Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(0)
    };

    private static Label FieldLabel(string text) => new()
    {
        Text = text, AutoSize = true, Margin = new Padding(0, 8, 0, 4)
    };

    private static Label WrappedLabel(string text, int left, int top, int right, int bottom) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(590, 0),
        Margin = new Padding(left, top, right, bottom)
    };

    public void ReloadConfiguration()
    {
        var configuration = runtime.Configuration;
        if (!centralUrl.Focused)
            centralUrl.Text = string.IsNullOrWhiteSpace(configuration.CentralUrl)
                ? "https://central.futureplacecoworking.com.br" : configuration.CentralUrl;
        scannerPath.Text = configuration.ScannerDllPath;
        pairingState.Text = configuration.IsPaired
            ? "Este computador está pareado. O cadastro e as permissões ficam na Central."
            : "Gere um código na área Digitais da Central e informe-o abaixo.";
        // An unreadable DPAPI store has no usable pairing, but the user must
        // still be able to explicitly remove it through the confirmation flow.
        clearButton.Enabled = !busy;
        changingStartup = true;
        try
        {
            startup.Checked = StartupRegistration.IsEnabled();
        }
        catch
        {
            startup.Enabled = false;
        }
        finally
        {
            changingStartup = false;
        }
    }

    public void ShowSnapshot(AgentSnapshot snapshot)
    {
        statusTitle.Text = StatusTitle(snapshot.Status);
        statusTitle.ForeColor = snapshot.Status switch
        {
            AgentStatus.Authorized or AgentStatus.Ready => Color.FromArgb(25, 100, 62),
            AgentStatus.Error or AgentStatus.Offline or AgentStatus.Revoked => Color.FromArgb(146, 45, 37),
            _ => Color.FromArgb(40, 55, 73)
        };
        statusMessage.Text = snapshot.Status == AgentStatus.Authorized
            ? "Autorizado pela Central. Nenhuma porta foi acionada."
            : snapshot.Message;
    }

    internal static string StatusTitle(AgentStatus status) => status switch
    {
        AgentStatus.Unconfigured => "Configuração necessária",
        AgentStatus.Connecting => "Conectando à Central",
        AgentStatus.Ready => "Pronto para ler",
        AgentStatus.Capturing => "Siga a orientação de captura",
        AgentStatus.Authorized => "Autorizado pela Central",
        AgentStatus.Denied => "Acesso não autorizado",
        AgentStatus.Revoked => "Leitor revogado",
        AgentStatus.Offline => "Sem conexão — acesso bloqueado",
        _ => "Verifique o leitor"
    };

    private async Task PairAsync()
    {
        if (busy)
            return;
        if (string.IsNullOrWhiteSpace(pairingCode.Text))
        {
            actionMessage.Text = "Informe o código gerado na Central.";
            pairingCode.Focus();
            return;
        }
        if (runtime.Configuration.IsPaired && MessageBox.Show(this,
            "Trocar o pareamento remove as digitais salvas neste computador. Para bloquear o leitor anterior na Central, revogue-o também na área Digitais. Deseja continuar?",
            "Trocar pareamento", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        await RunActionAsync(async () =>
        {
            try
            {
                await runtime.PairAsync(centralUrl.Text.Trim(), pairingCode.Text.Trim(), lifetime);
                actionMessage.ForeColor = Color.FromArgb(25, 100, 62);
                actionMessage.Text = "Computador pareado. Os cadastros são iniciados na Central.";
            }
            finally
            {
                pairingCode.Clear();
            }
        }, "Não foi possível parear. Confira o endereço da Central, a conexão e se o código ainda está válido.");
    }

    private async Task ClearPairingAsync()
    {
        if (busy || MessageBox.Show(this,
            "Remover o pareamento e todas as digitais deste computador? Esta limpeza local não revoga o leitor na Central; faça a revogação também na área Digitais.",
            "Limpar dados locais", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        await RunActionAsync(async () =>
        {
            await runtime.ResetPairingAsync(lifetime);
            pairingCode.Clear();
            actionMessage.Text = "Pareamento e digitais locais removidos. Revogue também o leitor anterior na Central.";
        }, "Não foi possível limpar os dados locais. Encerre e abra o programa e tente novamente.");
    }

    private async Task ChooseScannerAsync()
    {
        if (busy)
            return;
        using var dialog = new OpenFileDialog
        {
            Title = "Selecione a ftrScanAPI.dll oficial de 64 bits",
            Filter = "DLL de captura Futronic (ftrScanAPI.dll)|ftrScanAPI.dll",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        await RunActionAsync(async () =>
        {
            await runtime.ConfigureScannerAsync(dialog.FileName, lifetime);
            actionMessage.ForeColor = Color.FromArgb(25, 100, 62);
            actionMessage.Text = "DLL configurada. Mantenha o arquivo nessa pasta e conecte o FS80H por USB.";
        }, "Não foi possível configurar a DLL. Selecione a ftrScanAPI.dll oficial de 64 bits e confira se o driver do fabricante está instalado.");
    }

    private async Task RunActionAsync(Func<Task> action, string safeFailure)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PendingOperation = finished.Task;
        busy = true;
        SetInputsEnabled(false);
        actionMessage.ForeColor = Color.FromArgb(131, 40, 35);
        actionMessage.Text = "Aguarde…";
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch
        {
            actionMessage.Text = safeFailure;
        }
        finally
        {
            busy = false;
            try
            {
                if (!IsDisposed && !lifetime.IsCancellationRequested)
                {
                    SetInputsEnabled(true);
                    ReloadConfiguration();
                }
            }
            finally
            {
                finished.TrySetResult();
            }
        }
    }

    private void SetInputsEnabled(bool enabled)
    {
        centralUrl.Enabled = enabled;
        pairingCode.Enabled = enabled;
        pairButton.Enabled = enabled;
        clearButton.Enabled = enabled;
        chooseScannerButton.Enabled = enabled;
    }

    private void StartupChanged(object? sender, EventArgs e)
    {
        if (changingStartup)
            return;
        try
        {
            StartupRegistration.SetEnabled(startup.Checked);
        }
        catch
        {
            actionMessage.Text = "Não foi possível alterar o início automático para este usuário do Windows.";
            changingStartup = true;
            startup.Checked = !startup.Checked;
            changingStartup = false;
        }
    }

    private void OpenDownloads()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://www.futronic-tech.com/download.php")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            actionMessage.Text = "Abra https://www.futronic-tech.com/download.php no navegador para obter o driver e a DLL oficiais.";
        }
    }

    public void SetExiting()
    {
        SetInputsEnabled(false);
        startup.Enabled = false;
        statusTitle.Text = "Encerrando agente…";
        statusMessage.Text = "Aguardando o leitor e a Central encerrarem as operações.";
    }
}

using System.Diagnostics;

namespace Futureplace.AgenteFS80H.Desktop;

internal sealed partial class MainForm
{
    private async Task PairAsync()
    {
        if (busy)
            return;
        if (string.IsNullOrWhiteSpace(pairingCode.Text))
        {
            actionMessage.ForeColor = CentralTheme.Danger;
            actionMessage.Text = "Informe o código gerado na Central.";
            pairingCode.Focus();
            return;
        }
        if (runtime.Configuration.IsPaired && MessageBox.Show(this,
            "Trocar o pareamento remove as digitais salvas neste computador. Para bloquear o leitor anterior na Central, revogue-o também em Automação e sensores › Leitor da recepção. Deseja continuar?",
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
                if (!string.IsNullOrWhiteSpace(runtime.Configuration.ScannerDllPath)) ShowPage(false);
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
            "Remover o pareamento e todas as digitais deste computador? Esta limpeza local não revoga o leitor na Central; faça a revogação também em Automação e sensores › Leitor da recepção.",
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
            RestoreDirectory = true,
            InitialDirectory = Path.GetDirectoryName(runtime.Configuration.ScannerDllPath) is { } folder && Directory.Exists(folder) ? folder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        await RunActionAsync(async () =>
        {
            await runtime.ConfigureScannerAsync(dialog.FileName, lifetime);
            actionMessage.ForeColor = Color.FromArgb(25, 100, 62);
            actionMessage.Text = "DLL configurada. Mantenha o arquivo nessa pasta e conecte o FS80H por USB.";
            if (runtime.Configuration.IsPaired) ShowPage(false);
        }, "Não foi possível configurar a DLL. Selecione a ftrScanAPI.dll oficial de 64 bits e confira se o driver do fabricante está instalado.");
    }

    private async Task RunActionAsync(Func<Task> action, string safeFailure)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PendingOperation = finished.Task;
        busy = true;
        SetInputsEnabled(false);
        actionMessage.ForeColor = CentralTheme.Primary;
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
            actionMessage.ForeColor = CentralTheme.Danger;
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
        downloadsButton.Enabled = enabled;
        startup.Enabled = enabled && startupAvailable;
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
            actionMessage.ForeColor = CentralTheme.Danger;
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

}

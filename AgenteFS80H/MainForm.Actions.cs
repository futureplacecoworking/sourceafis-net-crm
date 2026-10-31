namespace Futureplace.AgenteFS80H.Desktop;

internal sealed partial class MainForm
{
    private static readonly Color SuccessText = Color.FromArgb(25, 100, 62);

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
        // Trocar um pareamento que existe apaga as digitais: pede para digitar TROCAR.
        if (runtime.Configuration.IsPaired && !ConfirmTyped(TypedConfirmation.ReplacePairing(runtime.Configuration.DeviceName)))
        {
            actionMessage.ForeColor = CentralTheme.Primary;
            actionMessage.Text = "Pareamento mantido. Nada foi apagado.";
            return;
        }
        var wasPaired = runtime.Configuration.IsPaired;
        await RunActionAsync(async () =>
        {
            PairingResult result;
            try
            {
                result = await runtime.PairAsync(centralUrl.Text.Trim(), pairingCode.Text.Trim(), lifetime);
            }
            catch (Exception) when (!lifetime.IsCancellationRequested && wasPaired && !runtime.Configuration.IsPaired)
            {
                // O pareamento antigo já saiu (digitais apagadas), mas o código novo não foi aceito.
                actionMessage.ForeColor = CentralTheme.Danger;
                actionMessage.Text = "O pareamento antigo foi removido e as digitais deste computador foram apagadas, mas o código novo não foi aceito. Gere outro código na Central em Digitais › Central biométrica e tente de novo.";
                return;
            }
            finally
            {
                pairingCode.Clear();
            }
            var text = PairingSuccessText(result);
            actionMessage.ForeColor = result.Previous == UnpairNotice.Unavailable ? CentralTheme.Warning : SuccessText;
            actionMessage.Text = text;
            ReloadConfiguration();
            NotifyPairing(text);
            if (!string.IsNullOrWhiteSpace(runtime.Configuration.ScannerDllPath)) ShowPage(false);
        }, "Não foi possível parear. Confira o endereço da Central, a conexão e se o código ainda está válido.");
    }

    internal static string PairingSuccessText(PairingResult result)
    {
        var text = string.IsNullOrWhiteSpace(result.DeviceName)
            ? "Pronto! Este computador está ligado à Central."
            : $"Pronto! Este computador está ligado à Central como «{result.DeviceName}».";
        return result.Previous switch
        {
            UnpairNotice.Confirmed => text + " O leitor antigo foi desligado na Central e as digitais antigas foram apagadas deste computador.",
            UnpairNotice.Unavailable => text + " As digitais antigas foram apagadas deste computador, mas não foi possível avisar a Central: revogue o leitor antigo em Digitais › Central biométrica.",
            _ => text
        };
    }

    private async Task ClearPairingAsync()
    {
        if (busy)
            return;
        // Limpar apaga as digitais e desliga o leitor: pede para digitar LIMPAR.
        if (!ConfirmTyped(TypedConfirmation.ClearPairing(runtime.Configuration.DeviceName)))
        {
            actionMessage.ForeColor = CentralTheme.Primary;
            actionMessage.Text = "Nada foi apagado. O pareamento continua como estava.";
            return;
        }
        await RunActionAsync(async () =>
        {
            var notice = await runtime.ResetPairingAsync(lifetime);
            pairingCode.Clear();
            actionMessage.ForeColor = notice == UnpairNotice.Unavailable ? CentralTheme.Warning : SuccessText;
            actionMessage.Text = ClearPairingText(notice);
        }, "Não foi possível limpar os dados locais. Encerre e abra o programa e tente novamente.");
    }

    internal static string ClearPairingText(UnpairNotice notice) => notice switch
    {
        UnpairNotice.Confirmed => "Pareamento e digitais removidos. A Central também desligou este leitor.",
        UnpairNotice.NotPaired => "Digitais e dados locais removidos. Se este computador estava ligado a um leitor na Central, revogue-o em Digitais › Central biométrica.",
        _ => "Pareamento e digitais removidos deste computador. Não foi possível avisar a Central agora: revogue o leitor antigo em Digitais › Central biométrica."
    };

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
            actionMessage.ForeColor = SuccessText;
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
        sound.Enabled = enabled;
    }

    private void SoundChanged(object? sender, EventArgs e)
    {
        if (changingSound)
            return;
        try
        {
            runtime.SetSoundEnabled(sound.Checked);
        }
        catch
        {
            actionMessage.ForeColor = CentralTheme.Danger;
            actionMessage.Text = "Não foi possível guardar a preferência de som neste computador.";
            changingSound = true;
            sound.Checked = !sound.Checked;
            changingSound = false;
        }
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

    private void OpenDownloads() => OpenAddress("https://www.futronic-tech.com/download.php",
        "Abra https://www.futronic-tech.com/download.php no navegador para obter o driver e a DLL oficiais.");

}

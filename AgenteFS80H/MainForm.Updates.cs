using System.Diagnostics;

namespace Futureplace.AgenteFS80H.Desktop;

internal sealed partial class MainForm
{
    private readonly AppUpdateClient appUpdates;
    private readonly Label updateMessage = TextLabel("");
    private readonly CentralButton installUpdate = new("Atualizar agora", ButtonTone.Primary) { Name = "installUpdateButton" };
    private readonly CentralButton postponeUpdate = new("Depois") { Name = "postponeUpdateButton" };
    private readonly ProgressBar updateProgress = new() { Name = "updateProgress", Minimum = 0, Maximum = 100, Dock = DockStyle.Fill, Visible = false, Height = 10 };
    private Control? updateBanner;
    private CancellationTokenSource? updateLifetime;
    private AppUpdate? availableUpdate;
    private Version? postponedVersion;
    private bool checkingUpdates;
    private bool installingUpdate;
    private bool updatesStopped;

    // Built into the operator shell only; the customer presentation never receives update controls.
    private Control BuildUpdateBanner()
    {
        var stack = Stack();
        updateMessage.Name = "updateMessage";
        updateMessage.AccessibleRole = AccessibleRole.Alert;
        updateMessage.MaximumSize = new Size(900, 0);
        updateMessage.Margin = new Padding(0, 0, 0, 6);
        updateMessage.Text = "Verificando atualizações…";
        Add(stack, updateMessage);
        Add(stack, updateProgress);
        Add(stack, Buttons(installUpdate, postponeUpdate));
        var card = new CentralCard { Name = "updateBanner", AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(28, 0, 28, 12), Padding = new Padding(16, 12, 16, 8), Visible = false };
        card.Controls.Add(stack);
        updateBanner = card;
        installUpdate.Click += async (_, _) => await InstallUpdateAsync();
        postponeUpdate.Click += (_, _) =>
        {
            postponedVersion = availableUpdate?.Version;
            card.Visible = false;
        };
        return card;
    }

    internal void StartUpdateChecks()
    {
        if (updateLifetime is not null || updatesStopped || exiting) return;
        updateLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _ = UpdateLoopAsync(updateLifetime.Token);
    }

    private async Task UpdateLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await CheckForUpdatesAsync(false, cancellationToken);
                await Task.Delay(TimeSpan.FromHours(6), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    internal async Task VerifyUpdatesNowAsync()
    {
        if (updatesStopped || exiting || installingUpdate) return;
        await CheckForUpdatesAsync(true, updateLifetime?.Token ?? lifetime);
    }

    private async Task CheckForUpdatesAsync(bool manual, CancellationToken cancellationToken)
    {
        if (checkingUpdates || installingUpdate || updatesStopped || exiting) return;
        checkingUpdates = true;
        if (manual)
        {
            actionMessage.ForeColor = CentralTheme.Primary;
            actionMessage.Text = "Verificando atualizações da Central Biométrica…";
        }
        try
        {
            var update = await appUpdates.CheckAsync(Version.Parse(AppIdentity.Version), cancellationToken);
            if (updatesStopped || exiting || IsDisposed) return;
            if (update is null)
            {
                if (manual) actionMessage.Text = "Não há uma nova versão publicada para instalar.";
                return;
            }
            availableUpdate = update;
            if (!manual && postponedVersion == update.Version) return;
            if (manual) actionMessage.Text = "";
            updateMessage.Text = $"Central Biométrica {update.Version} disponível. Sua configuração e digitais serão preservadas.";
            updateMessage.ForeColor = CentralTheme.Primary;
            updateProgress.Visible = false;
            updateBanner!.Visible = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch
        {
            // An unavailable update service never interrupts reading or changes the access decision.
            if (manual && !updatesStopped && !exiting && !IsDisposed)
            {
                actionMessage.ForeColor = CentralTheme.Warning;
                actionMessage.Text = "Não foi possível consultar atualizações. Você pode continuar usando a versão atual e tentar novamente mais tarde.";
            }
        }
        finally { checkingUpdates = false; }
    }

    private async Task InstallUpdateAsync()
    {
        var update = availableUpdate;
        if (installingUpdate || update is null || updatesStopped || exiting) return;
        installingUpdate = true;
        installUpdate.Enabled = postponeUpdate.Enabled = false;
        updateProgress.Value = 0;
        updateProgress.Visible = true;
        updateMessage.Text = $"Baixando Central Biométrica {update.Version}… Você pode continuar usando o leitor.";
        var cancellationToken = updateLifetime?.Token ?? lifetime;
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Futureplace", "AgenteFS80H", "updates");
            var progress = new Progress<int>(value =>
            {
                if (!updatesStopped && !exiting && !IsDisposed) updateProgress.Value = Math.Clamp(value, 0, 100);
            });
            var path = await appUpdates.DownloadAsync(update, directory, progress, cancellationToken);
            await AppUpdateClient.VerifyInstallerAsync(path, update, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (updatesStopped || exiting || IsDisposed) return;
            using var installer = Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /AUTORESTART") { UseShellExecute = false, WorkingDirectory = directory })
                ?? throw new AppUpdateException();
            updateMessage.Text = "Instalador aberto. A Central Biométrica será reiniciada após a atualização.";
            updateMessage.ForeColor = CentralTheme.Primary;
            await installer.WaitForExitAsync(cancellationToken);
            if (installer.ExitCode != 0) throw new AppUpdateException();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch
        {
            if (!updatesStopped && !exiting && !IsDisposed)
            {
                updateMessage.Text = "Não foi possível instalar a atualização verificada. Tente novamente; sua versão atual continua disponível.";
                updateMessage.ForeColor = CentralTheme.Warning;
            }
        }
        finally
        {
            installingUpdate = false;
            if (!updatesStopped && !exiting && !IsDisposed) installUpdate.Enabled = postponeUpdate.Enabled = true;
        }
    }

    private void StopUpdateChecks()
    {
        if (updatesStopped) return;
        updatesStopped = true;
        updateLifetime?.Cancel();
        updateLifetime?.Dispose();
        appUpdates.Dispose();
        installUpdate.Enabled = postponeUpdate.Enabled = false;
    }
}

namespace Futureplace.AgenteFS80H.Desktop;

internal sealed class TrayContext : ApplicationContext
{
    private readonly AgentRuntime runtime;
    private readonly MainForm form;
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu;
    private readonly Icon normalIcon = CentralTheme.AppIcon();
    private readonly Icon warningIcon = (Icon)SystemIcons.Warning.Clone();
    private readonly CancellationTokenSource lifetime = new();
    private readonly RegisteredWaitHandle stopWait;
    private readonly RegisteredWaitHandle showWait;
    private readonly Task runTask;
    private bool exiting;
    private AgentStatus? previousStatus;

    public TrayContext(AgentRuntime runtime, EventWaitHandle stop, EventWaitHandle show, bool background)
    {
        this.runtime = runtime;
        form = new MainForm(runtime, lifetime.Token);
        _ = form.Handle;
        menu = new ContextMenuStrip();
        menu.Items.Add("Abrir Central Biométrica", null, (_, _) => ShowForm());
        menu.Items.Add("Tela do cliente", null, (_, _) => form.ShowClientScreen());
        menu.Items.Add("Configurações", null, (_, _) => { form.ShowSettings(); ShowForm(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Encerrar Central Biométrica", null, async (_, _) => await RequestExitAsync());
        tray = new NotifyIcon
        {
            Icon = normalIcon,
            Text = AppIdentity.Name,
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (_, _) => ShowForm();
        runtime.Changed += RuntimeChanged;
        stopWait = ThreadPool.RegisterWaitForSingleObject(stop,
            (_, _) => OnUi(async () => await RequestExitAsync()), null, Timeout.Infinite, false);
        showWait = ThreadPool.RegisterWaitForSingleObject(show,
            (_, _) => OnUi(ShowForm), null, Timeout.Infinite, false);
        ApplySnapshot(runtime.Snapshot);
        form.StartUpdateChecks();
        runTask = Task.Run(() => RunAsync(lifetime.Token));
        if (!background)
            ShowForm();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await runtime.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            OnUi(() => ApplySnapshot(new AgentSnapshot(AgentStatus.Error,
                "Agente interrompido. Encerre e abra o programa novamente. Acesso não autorizado.")));
        }
    }

    private void RuntimeChanged(AgentSnapshot snapshot) => OnUi(() => ApplySnapshot(snapshot));

    private void ApplySnapshot(AgentSnapshot snapshot)
    {
        if (exiting)
            return;
        form.ShowSnapshot(snapshot);
        tray.Icon = snapshot.Status is AgentStatus.Error or AgentStatus.Offline or AgentStatus.Revoked
            ? warningIcon : normalIcon;
        string title = global::Futureplace.AgenteFS80H.Desktop.MainForm.StatusTitle(snapshot.Status);
        var trayCaption = AppIdentity.Name + " — " + title;
        tray.Text = trayCaption[..Math.Min(63, trayCaption.Length)];
        if (snapshot.Status == AgentStatus.Authorized && previousStatus != AgentStatus.Authorized)
            tray.ShowBalloonTip(5000, "Decisão da Central",
                "Autorizado pela Central. Nenhuma porta foi acionada.", ToolTipIcon.Info);
        else if (snapshot.Status == AgentStatus.Revoked && previousStatus != AgentStatus.Revoked)
            tray.ShowBalloonTip(5000, "Leitor revogado",
                "O leitor foi bloqueado pela Central. As digitais locais serão removidas.", ToolTipIcon.Warning);
        previousStatus = snapshot.Status;
    }

    private void ShowForm()
    {
        if (exiting)
            return;
        form.ReloadConfiguration();
        form.Show();
        if (form.WindowState == FormWindowState.Minimized)
            form.WindowState = FormWindowState.Normal;
        form.Activate();
        form.BringToFront();
    }

    private void OnUi(Action action)
    {
        if (form.IsDisposed || !form.IsHandleCreated)
            return;
        try
        {
            form.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // Shutdown can dispose the window between this check and BeginInvoke.
        }
    }

    private async Task RequestExitAsync()
    {
        if (exiting)
            return;
        exiting = true;
        menu.Enabled = false;
        form.SetExiting();
        tray.Text = "Central Biométrica — Encerrando";
        lifetime.Cancel();
        await runTask;
        await form.PendingOperation;
        stopWait.Unregister(null);
        showWait.Unregister(null);
        runtime.Changed -= RuntimeChanged;
        tray.Visible = false;
        form.AllowClose = true;
        form.Close();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lifetime.Cancel();
            stopWait.Unregister(null);
            showWait.Unregister(null);
            runtime.Changed -= RuntimeChanged;
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            form.AllowClose = true;
            form.Dispose();
            normalIcon.Dispose();
            warningIcon.Dispose();
            lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}

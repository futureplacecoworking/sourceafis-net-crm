namespace Futureplace.AgenteFS80H.Desktop;

internal sealed partial class MainForm
{
    private ClientScreenForm? clientScreen;
    private AgentSnapshot? displayedSnapshot;

    internal void ShowClientScreen()
    {
        if (exiting) return;
        if (clientScreen is null || clientScreen.IsDisposed)
        {
            var window = new ClientScreenForm(ClientWebHostFactory);
            clientScreen = window;
            clientWebFailure = null;
            window.WebUnavailable += reason => { if (!exiting && ReferenceEquals(clientScreen, window)) ShowClientWebFailure(reason); };
            window.VisibleChanged += (_, _) => { if (!exiting) RenderChecklistNow(); };
            window.FormClosed += (_, _) =>
            {
                if (!ReferenceEquals(clientScreen, window)) return;
                clientScreen = null;
                if (!exiting) RenderChecklistNow();
            };
        }
        clientScreen.ShowSnapshot(displayedSnapshot ?? runtime.Snapshot);
        clientScreen.Present(ClientScreenDestination());
        RenderChecklistNow();
    }

    private Screen ClientScreenDestination()
    {
        var current = Screen.FromControl(this);
        var others = Screen.AllScreens.Where(screen => screen.DeviceName != current.DeviceName).ToArray();
        // O monitor do cliente fica em pé (9:16) atrás do leitor; se houver um, ele tem preferência.
        return others.FirstOrDefault(screen => screen.Bounds.Height > screen.Bounds.Width) ?? others.FirstOrDefault() ?? current;
    }

    private void CloseClientScreen()
    {
        var window = clientScreen;
        clientScreen = null;
        if (window is not null && !window.IsDisposed) { window.Close(); window.Dispose(); }
    }
}

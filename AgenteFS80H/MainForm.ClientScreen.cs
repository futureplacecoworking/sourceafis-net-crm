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
            var window = new ClientScreenForm();
            clientScreen = window;
            window.FormClosed += (_, _) => { if (ReferenceEquals(clientScreen, window)) clientScreen = null; };
        }
        clientScreen.ShowSnapshot(displayedSnapshot ?? runtime.Snapshot);
        var current = Screen.FromControl(this);
        var destination = Screen.AllScreens.FirstOrDefault(screen => screen.DeviceName != current.DeviceName) ?? current;
        clientScreen.Present(destination);
    }

    private void CloseClientScreen()
    {
        var window = clientScreen;
        clientScreen = null;
        if (window is not null && !window.IsDisposed) { window.Close(); window.Dispose(); }
    }
}

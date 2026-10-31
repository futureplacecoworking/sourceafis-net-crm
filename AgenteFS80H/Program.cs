using System.Security.Principal;

namespace Futureplace.AgenteFS80H.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, _) => ShowSafeError();

        // Named objects are scoped to the Windows session and the signed-in user.
        // A second user's installation cannot stop or reveal this user's instance.
        string user = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Usuário do Windows indisponível.");
        string prefix = $"Local\\Futureplace.AgenteFS80H.{user}";
        string stopName = prefix + ".Stop";
        string showName = prefix + ".Show";

        if (args.Contains("--shutdown", StringComparer.OrdinalIgnoreCase))
        {
            SignalExisting(stopName);
            return;
        }

        using var instance = new Mutex(false, prefix + ".Instance");
        bool acquired;
        try
        {
            acquired = instance.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            SignalExisting(showName);
            return;
        }

        try
        {
            using var stop = new EventWaitHandle(false, EventResetMode.AutoReset, stopName);
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset, showName);
            using var runtime = AgentRuntime.CreateDefault(AppIdentity.Version);
            using var context = new TrayContext(runtime, stop, show,
                args.Contains("--background", StringComparer.OrdinalIgnoreCase));
            Application.Run(context);
        }
        catch
        {
            // Never display exception details: SDK/HTTP messages can carry secrets.
            MessageBox.Show(
                "Não foi possível iniciar o agente. Se os dados locais estiverem protegidos por outro usuário do Windows ou danificados, reinstale escolhendo explicitamente remover os dados e faça um novo pareamento. Nenhum dado foi apagado automaticamente.",
                AppIdentity.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            instance.ReleaseMutex();
        }
    }

    private static void SignalExisting(string name)
    {
        // The first process may still be creating its window and events.
        for (int attempt = 0; attempt < 10; ++attempt)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(name, out var signal))
                {
                    using (signal)
                        signal.Set();
                    return;
                }
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
            Thread.Sleep(100);
        }
    }

    private static void ShowSafeError() => MessageBox.Show(
        "O agente encontrou um problema. Confira o estado exibido e encerre e abra o programa novamente. Nenhuma porta foi acionada.",
        AppIdentity.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
}

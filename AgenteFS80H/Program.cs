using System.Security.Principal;

namespace Futureplace.AgenteFS80H.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            AppLog.Current.Error("Erro na janela", e.Exception);
            ShowSafeError();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception error) AppLog.Current.Error("Erro não tratado", error);
            else AppLog.Current.Info("Erro não tratado de origem desconhecida");
            // O processo vai encerrar: dá um instante para a linha chegar ao disco.
            (AppLog.Current as FileDiagnostics)?.Flush(TimeSpan.FromSeconds(2));
        };
        TaskScheduler.UnobservedTaskException += (_, e) => AppLog.Current.Error("Tarefa com erro não observado", e.Exception);

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

        // O registro local só nasce na instância que fica aberta (a segunda só chama a primeira e sai).
        using var log = new FileDiagnostics(FileDiagnostics.DefaultDirectory);
        AppLog.Current = log;
        log.Info($"{AppIdentity.Name} {AppIdentity.Version} iniciando (Windows {Environment.OSVersion.Version}, {(args.Contains("--background", StringComparer.OrdinalIgnoreCase) ? "em segundo plano" : "com janela")})");
        try
        {
            using var stop = new EventWaitHandle(false, EventResetMode.AutoReset, stopName);
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset, showName);
            using var runtime = AgentRuntime.CreateDefault(AppIdentity.Version);
            using var context = new TrayContext(runtime, stop, show,
                args.Contains("--background", StringComparer.OrdinalIgnoreCase));
            Application.Run(context);
            log.Info("Encerrado normalmente");
        }
        catch (Exception error)
        {
            log.Error("Não foi possível iniciar", error);
            // Never display exception details: SDK/HTTP messages can carry secrets.
            MessageBox.Show(
                "Não foi possível iniciar o agente. Se os dados locais estiverem protegidos por outro usuário do Windows ou danificados, reinstale escolhendo explicitamente remover os dados e faça um novo pareamento. Nenhum dado foi apagado automaticamente.",
                AppIdentity.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            log.Flush(TimeSpan.FromSeconds(2));
            AppLog.Current = new NullAppDiagnostics();
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

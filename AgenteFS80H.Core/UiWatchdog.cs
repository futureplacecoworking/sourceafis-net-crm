namespace Futureplace.AgenteFS80H;

/// <summary>
/// Vigia da janela: a cada segundo pede à thread da janela uma resposta. Se ela não responder em 3 segundos,
/// registra uma linha com a última etapa marcada; quando voltar, registra outra. Nunca mexe na janela.
/// </summary>
public sealed class UiWatchdog : IDisposable
{
    public static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan HangAfter = TimeSpan.FromSeconds(3);
    private readonly IAppDiagnostics log;
    private readonly Func<Action, bool> postToUi;
    private readonly TimeProvider clock;
    private readonly object gate = new();
    private ITimer? timer;
    private bool disposed;
    private bool waiting;
    private bool hangLogged;
    private DateTimeOffset pingSentAt;

    /// <param name="postToUi">Entrega a ação à thread da janela sem esperar (BeginInvoke). Devolve false se a janela não existe mais.</param>
    public UiWatchdog(IAppDiagnostics log, Func<Action, bool> postToUi, TimeProvider? clock = null)
    {
        this.log = log;
        this.postToUi = postToUi;
        this.clock = clock ?? TimeProvider.System;
    }

    public void Start()
    {
        lock (gate)
        {
            if (disposed || timer is not null) return;
            timer = clock.CreateTimer(_ => Tick(), null, PingInterval, PingInterval);
        }
    }

    /// <summary>Um passo do vigia (o relógio chama a cada segundo). Nunca lança erro.</summary>
    internal void Tick()
    {
        try
        {
            var send = false;
            lock (gate)
            {
                if (disposed) return;
                var now = clock.GetUtcNow();
                if (!waiting)
                {
                    waiting = true;
                    pingSentAt = now;
                    send = true;
                }
                else if (!hangLogged && now - pingSentAt >= HangAfter)
                {
                    hangLogged = true;
                    log.Hang(now - pingSentAt, log.LastStep);
                }
            }
            // Com um pedido sem resposta, não manda outro: a fila da janela não enche enquanto ela está presa.
            if (send && !Post())
                lock (gate) waiting = false;
        }
        catch (Exception) { }
    }

    private bool Post()
    {
        try { return postToUi(Answer); }
        catch (Exception) { return false; }
    }

    // Roda na thread da janela: ela só responde quando consegue tratar mensagens.
    private void Answer()
    {
        try
        {
            lock (gate)
            {
                if (!waiting) return;
                waiting = false;
                if (!hangLogged) return;
                hangLogged = false;
                log.Recovered(clock.GetUtcNow() - pingSentAt);
            }
        }
        catch (Exception) { }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            timer?.Dispose();
            timer = null;
        }
    }
}

namespace Futureplace.AgenteFS80H.Desktop;

/// <summary>
/// Entrega à janela só o valor mais recente. Quem avisa pode chamar mil vezes sem esperar; a janela fica com no máximo
/// um pedido pendente por vez e, quando o atende, recebe o último valor.
/// </summary>
internal sealed class LatestOnlyQueue<T> where T : class
{
    private readonly Func<Action, bool> post;
    private readonly Action<T> apply;
    private T? latest;
    private int queued;

    /// <param name="post">Entrega a ação à thread da janela sem esperar. Devolve false se a janela não existe mais.</param>
    public LatestOnlyQueue(Func<Action, bool> post, Action<T> apply)
    {
        this.post = post;
        this.apply = apply;
    }

    public void Submit(T value)
    {
        Interlocked.Exchange(ref latest, value);
        if (Interlocked.Exchange(ref queued, 1) == 0 && !post(Drain))
            Interlocked.Exchange(ref queued, 0);
    }

    // Roda na thread da janela. Libera a vaga antes de ler o valor: o que chegar enquanto aplica gera um novo pedido.
    private void Drain()
    {
        Interlocked.Exchange(ref queued, 0);
        if (Interlocked.Exchange(ref latest, null) is { } value) apply(value);
    }
}

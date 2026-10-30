using System.Security.Cryptography;
using SourceAFIS;

namespace Futureplace.AgenteFS80H;

public sealed class SourceAfisRecognizer : IFingerprintRecognizer
{
    public byte[] Extract(FingerprintFrame image) => new FingerprintTemplate(new FingerprintImage(image.Width, image.Height, image.Pixels, new FingerprintImageOptions { Dpi = 500 })).ToByteArray();
    public double Score(byte[] probe, byte[] candidate) => new FingerprintMatcher(new FingerprintTemplate(probe)).Match(new FingerprintTemplate(candidate));
}

public static class MatchingPolicy
{
    // Valores iniciais conservadores, sujeitos a calibração no leitor real; não representam probabilidade.
    public const double Threshold = 40;
    public const double AmbiguityMargin = 10;
    public static Guid? Select(IEnumerable<(Guid Id, double Score)> scores)
    {
        var ordered = scores.ToArray();
        if (ordered.Any(value => !double.IsFinite(value.Score))) return null;
        Array.Sort(ordered, (a, b) => b.Score.CompareTo(a.Score));
        if (ordered.Length == 0 || ordered[0].Score < Threshold || (ordered.Length > 1 && ordered[0].Score - ordered[1].Score < AmbiguityMargin)) return null;
        return ordered[0].Id;
    }
}

public sealed class CaptureCoordinator(IFingerprintScanner scanner, IFingerprintRecognizer recognizer, TimeProvider clock, TimeSpan? pollInterval = null)
{
    public event Action<string>? Instruction;
    public DateTimeOffset CapturedAt { get; private set; }
    private readonly TimeSpan poll = pollInterval ?? TimeSpan.FromMilliseconds(150);

    public async Task<byte[]> CapturePairAsync(Func<CancellationToken, Task> guard, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        byte[]? first = null;
        byte[]? second = null;
        try
        {
            Instruction?.Invoke("Coloque o dedo no leitor para a primeira leitura.");
            await WaitAsync(present: true, guard, token).ConfigureAwait(false);
            first = Extract();
            Instruction?.Invoke("Retire o dedo completamente do leitor.");
            await WaitAsync(present: false, guard, token).ConfigureAwait(false);
            Instruction?.Invoke("Coloque o mesmo dedo novamente para confirmar.");
            await WaitAsync(present: true, guard, token).ConfigureAwait(false);
            second = Extract();
            var score = recognizer.Score(first, second);
            if (!double.IsFinite(score) || score < MatchingPolicy.Threshold) throw new CaptureException("inconsistente");
            Instruction?.Invoke("Retire o dedo para concluir a confirmação.");
            await WaitAsync(present: false, guard, token).ConfigureAwait(false);
            var result = first;
            first = null;
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CaptureException("captura");
        }
        finally
        {
            if (first is not null) CryptographicOperations.ZeroMemory(first);
            if (second is not null) CryptographicOperations.ZeroMemory(second);
        }
    }

    private byte[] Extract()
    {
        var image = scanner.Capture();
        CapturedAt = clock.GetUtcNow();
        try { return recognizer.Extract(image); }
        finally { CryptographicOperations.ZeroMemory(image.Pixels); }
    }

    private async Task WaitAsync(bool present, Func<CancellationToken, Task> guard, CancellationToken token)
    {
        // Sincroniza no início de cada etapa e ao menos uma vez por segundo enquanto aguarda o dedo.
        var lastGuard = DateTimeOffset.MinValue;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (clock.GetUtcNow() - lastGuard >= TimeSpan.FromSeconds(1))
            {
                await guard(token).ConfigureAwait(false);
                lastGuard = clock.GetUtcNow();
            }
            if (scanner.IsFingerPresent() == present) return;
            await Task.Delay(poll, clock, token).ConfigureAwait(false);
        }
    }
}

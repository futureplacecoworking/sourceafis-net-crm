using System.Security.Cryptography;

namespace Futureplace.AgenteFS80H;

/// <summary>
/// Junta as leituras boas do mesmo dedo para um cadastro. A partir da segunda, cada leitura só é boa se bater com uma
/// leitura boa anterior. Leitura ruim (dedo mexeu, saiu cedo, erro do aparelho) pede de novo, sem cancelar o cadastro.
/// Só falha por muitas tentativas (motivo "captura") ou por digital que já é de outro cadastro ("duplicada").
/// </summary>
public sealed class EnrollmentCollector
{
    public const int MaxAttempts = 15;
    // Leituras recusadas guardadas para reconhecer quando a primeira leitura é que foi a ruim.
    private const int MaxRejected = 3;
    private readonly IFingerprintScanner scanner;
    private readonly IFingerprintRecognizer recognizer;
    private readonly TimeProvider clock;
    private readonly CaptureCoordinator capture;
    private readonly TimeSpan poll;
    private readonly TimeSpan errorBackoff;

    public EnrollmentCollector(IFingerprintScanner scanner, IFingerprintRecognizer recognizer, TimeProvider clock, TimeSpan? pollInterval = null)
    {
        this.scanner = scanner;
        this.recognizer = recognizer;
        this.clock = clock;
        poll = pollInterval ?? TimeSpan.FromMilliseconds(150);
        errorBackoff = poll == TimeSpan.Zero ? TimeSpan.Zero : TimeSpan.FromSeconds(1);
        capture = new CaptureCoordinator(scanner, recognizer, clock);
    }

    /// <summary>Tentativas usadas: cada dedo lido e cada falha do aparelho.</summary>
    public int Attempts { get; private set; }

    /// <summary>
    /// Devolve as leituras boas, na ordem. Quem chama apaga os templates da memória depois de gravar.
    /// <paramref name="report"/> recebe a fase, as leituras boas e o número da leitura atual.
    /// </summary>
    public async Task<List<byte[]>> CollectAsync(int required, Func<CancellationToken, Task> guard, Func<byte[], Guid?> findDuplicate,
        Action<EnrollmentPhase, int, int> report, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(required, 1);
        var good = new List<byte[]>();
        var rejected = new List<byte[]>();
        try
        {
            // Começa com o leitor livre: um dedo esquecido no vidro não vira a primeira leitura.
            await WaitAsync(present: false, guard, () => report(EnrollmentPhase.Lift, 0, 1), token).ConfigureAwait(false);
            while (good.Count < required)
            {
                if (Attempts >= MaxAttempts) throw new EnrollmentFailedException("captura");
                var reading = good.Count + 1;
                report(EnrollmentPhase.Place, good.Count, reading);
                await WaitAsync(present: true, guard, null, token).ConfigureAwait(false);
                report(EnrollmentPhase.Reading, good.Count, reading);
                Attempts++;
                var accepted = Read(good, rejected);
                foreach (var sample in accepted)
                    if (findDuplicate(sample) is { } other)
                        throw new EnrollmentFailedException("duplicada", other);
                if (accepted.Count == 0)
                {
                    report(EnrollmentPhase.BadReading, good.Count, reading);
                    if (Attempts >= MaxAttempts) throw new EnrollmentFailedException("captura");
                    await WaitAsync(present: false, guard, null, token).ConfigureAwait(false);
                    continue;
                }
                // Depois da última leitura boa não é preciso esperar o dedo sair: a identificação só volta com o leitor livre.
                if (good.Count >= required) break;
                report(EnrollmentPhase.Lift, good.Count, good.Count);
                await WaitAsync(present: false, guard, null, token).ConfigureAwait(false);
            }
            var result = good;
            good = [];
            return result;
        }
        finally
        {
            Clear(good);
            Clear(rejected);
        }
    }

    private List<byte[]> Read(List<byte[]> good, List<byte[]> rejected)
    {
        byte[] sample;
        try { sample = capture.ReadTemplate(); }
        catch (Exception ex) when (ex is CaptureException or ArgumentException or InvalidOperationException or IndexOutOfRangeException or FormatException)
        {
            // Dedo mexeu, saiu cedo ou o aparelho falhou nesta leitura: pede de novo.
            return [];
        }
        if (good.Count == 0)
        {
            good.Add(sample);
            return [sample];
        }
        var matcher = recognizer.Matcher(sample);
        if (good.Any(previous => MatchingPolicy.IsMatch(matcher.Score(previous))))
        {
            good.Add(sample);
            return [sample];
        }
        // A primeira leitura pode ter sido a ruim: se esta bate com uma recusada, as duas passam a ser as boas.
        if (good.Count == 1 && rejected.FirstOrDefault(previous => MatchingPolicy.IsMatch(matcher.Score(previous))) is { } partner)
        {
            rejected.Remove(partner);
            CryptographicOperations.ZeroMemory(good[0]);
            good.Clear();
            good.Add(partner);
            good.Add(sample);
            return [partner, sample];
        }
        rejected.Add(sample);
        if (rejected.Count > MaxRejected)
        {
            CryptographicOperations.ZeroMemory(rejected[0]);
            rejected.RemoveAt(0);
        }
        return [];
    }

    private async Task WaitAsync(bool present, Func<CancellationToken, Task> guard, Action? waiting, CancellationToken token)
    {
        // Consulta a Central no início e ao menos uma vez por segundo enquanto espera o dedo.
        var lastGuard = DateTimeOffset.MinValue;
        var announced = false;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (clock.GetUtcNow() - lastGuard >= TimeSpan.FromSeconds(1))
            {
                await guard(token).ConfigureAwait(false);
                lastGuard = clock.GetUtcNow();
            }
            bool current;
            try { current = scanner.IsFingerPresent(); }
            catch (CaptureException)
            {
                // Falha passageira do aparelho conta como tentativa; o cadastro continua.
                if (++Attempts >= MaxAttempts) throw new EnrollmentFailedException("captura");
                await Task.Delay(errorBackoff, clock, token).ConfigureAwait(false);
                continue;
            }
            if (current == present) return;
            if (!announced)
            {
                announced = true;
                waiting?.Invoke();
            }
            await Task.Delay(poll, clock, token).ConfigureAwait(false);
        }
    }

    private static void Clear(List<byte[]> samples)
    {
        foreach (var sample in samples) CryptographicOperations.ZeroMemory(sample);
        samples.Clear();
    }
}

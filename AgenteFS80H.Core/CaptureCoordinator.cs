using System.Security.Cryptography;
using SourceAFIS;

namespace Futureplace.AgenteFS80H;

public sealed class SourceAfisRecognizer : IFingerprintRecognizer
{
    public byte[] Extract(FingerprintFrame image) => new FingerprintTemplate(new FingerprintImage(image.Width, image.Height, image.Pixels, new FingerprintImageOptions { Dpi = 500 })).ToByteArray();

    // O FingerprintMatcher prepara a leitura uma vez; cada template guardado é só comparado com ela.
    public IFingerprintMatcher Matcher(byte[] probe) => new PreparedMatcher(new FingerprintMatcher(new FingerprintTemplate(probe)));

    private sealed class PreparedMatcher(FingerprintMatcher matcher) : IFingerprintMatcher
    {
        public double Score(byte[] candidate) => matcher.Match(new FingerprintTemplate(candidate));
    }
}

public static class MatchingPolicy
{
    // Valores iniciais conservadores, sujeitos a calibração no leitor real; não representam probabilidade.
    public const double Threshold = 40;
    public const double AmbiguityMargin = 10;

    public static bool IsMatch(double score) => double.IsFinite(score) && score >= Threshold;

    /// <summary>
    /// Cada cadastro vale pela melhor nota entre os seus templates. A margem de ambiguidade é sempre entre
    /// cadastros diferentes, nunca entre leituras da mesma pessoa.
    /// </summary>
    public static Guid? Select(IEnumerable<(Guid Id, double Score)> scores)
    {
        var best = new Dictionary<Guid, double>();
        foreach (var (id, score) in scores)
        {
            if (!double.IsFinite(score)) return null;
            if (!best.TryGetValue(id, out var current) || score > current) best[id] = score;
        }
        var ordered = best.OrderByDescending(pair => pair.Value).ToArray();
        if (ordered.Length == 0 || ordered[0].Value < Threshold || (ordered.Length > 1 && ordered[0].Value - ordered[1].Value < AmbiguityMargin)) return null;
        return ordered[0].Key;
    }
}

/// <summary>Leitura do FS80H: captura do dedo que já está no leitor.</summary>
public sealed class CaptureCoordinator(IFingerprintScanner scanner, IFingerprintRecognizer recognizer, TimeProvider clock)
{
    public DateTimeOffset CapturedAt { get; private set; }

    /// <summary>Captura o dedo presente e extrai o template. A imagem é apagada logo depois.</summary>
    public byte[] ReadTemplate()
    {
        var image = scanner.Capture();
        CapturedAt = clock.GetUtcNow();
        try { return recognizer.Extract(image); }
        finally { CryptographicOperations.ZeroMemory(image.Pixels); }
    }
}

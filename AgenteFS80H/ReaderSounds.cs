using System.Media;

namespace Futureplace.AgenteFS80H.Desktop;

/// <summary>
/// Som curto do leitor: dois tons agradáveis para ok (entrada confirmada, digital cadastrada) e um tom grave para não.
/// Os sons são gerados na memória; nenhum arquivo de áudio acompanha o programa.
/// </summary>
internal sealed class ReaderSounds : IDisposable
{
    private readonly MemoryStream positiveWave = new(ToneGenerator.Positive(), writable: false);
    private readonly MemoryStream negativeWave = new(ToneGenerator.Negative(), writable: false);
    private readonly SoundPlayer positive;
    private readonly SoundPlayer negative;
    private bool disposed;

    public ReaderSounds()
    {
        // Os tocadores ficam vivos enquanto o programa roda: o Windows toca a partir da memória deles.
        positive = new SoundPlayer(positiveWave);
        negative = new SoundPlayer(negativeWave);
    }

    public void Play(AgentSignal signal)
    {
        if (disposed) return;
        try { (signal == AgentSignal.Positive ? positive : negative).Play(); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            // Sem placa de som ou dispositivo ocupado: o resultado continua na tela.
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        positive.Stop();
        negative.Stop();
        positive.Dispose();
        negative.Dispose();
        positiveWave.Dispose();
        negativeWave.Dispose();
    }
}

/// <summary>Gera WAV PCM 16 bits mono com notas suaves (início e fim em rampa, sem estalo).</summary>
internal static class ToneGenerator
{
    public const int SampleRate = 22050;
    private const double Volume = 0.32;

    public static byte[] Positive() => Wave((880.0, 0.11), (0, 0.025), (1318.5, 0.17));
    public static byte[] Negative() => Wave((330.0, 0.16), (0, 0.04), (247.0, 0.26));

    internal static byte[] Wave(params (double Frequency, double Seconds)[] notes)
    {
        var samples = new List<short>();
        foreach (var (frequency, seconds) in notes)
        {
            var count = (int)Math.Round(seconds * SampleRate);
            var ramp = Math.Max(1, Math.Min(count / 2, (int)(0.012 * SampleRate)));
            for (var i = 0; i < count; i++)
            {
                if (frequency <= 0) { samples.Add(0); continue; }
                var envelope = Math.Min(1.0, Math.Min(i, count - 1 - i) / (double)ramp);
                var value = Math.Sin(2 * Math.PI * frequency * i / SampleRate) * envelope * Volume;
                samples.Add((short)Math.Round(value * short.MaxValue));
            }
        }
        var dataLength = samples.Count * 2;
        using var memory = new MemoryStream(44 + dataLength);
        using var writer = new BinaryWriter(memory);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataLength);
        foreach (var sample in samples) writer.Write(sample);
        writer.Flush();
        return memory.ToArray();
    }
}

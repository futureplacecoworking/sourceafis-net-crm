using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Futureplace.AgenteFS80H;

/// <summary>
/// Registro local de erros e travamentos do programa. Só entram textos fixos, nomes de etapa e o tipo do erro:
/// nunca dados de pessoas, digitais, endereços, tokens ou a mensagem original de uma exceção.
/// </summary>
public interface IAppDiagnostics
{
    /// <summary>Última etapa registrada por <see cref="Step"/> (lida de qualquer thread).</summary>
    string LastStep { get; }
    /// <summary>Marca a etapa em que a janela está. Barato: só guarda o nome, sem gravar nada.</summary>
    void Step(string name);
    void Info(string message);
    void Error(string context, Exception exception);
    /// <summary>A janela não respondeu por <paramref name="duration"/>; <paramref name="lastStep"/> é a última etapa marcada.</summary>
    void Hang(TimeSpan duration, string lastStep);
    /// <summary>A janela voltou a responder depois de um travamento.</summary>
    void Recovered(TimeSpan duration);
}

/// <summary>Monta as linhas do registro; quem herda só decide onde gravá-las.</summary>
public abstract class AppDiagnostics : IAppDiagnostics
{
    private string lastStep = "início";

    public string LastStep => Volatile.Read(ref lastStep);
    public virtual void Step(string name) => Volatile.Write(ref lastStep, name);
    public void Info(string message) => Write("INFO", message);
    public void Error(string context, Exception exception) => Write("ERRO", context + ": " + Describe(exception));
    public void Hang(TimeSpan duration, string lastStep) => Write("TRAVADA", $"UI sem resposta há {Seconds(duration)}s, última etapa: {lastStep}");
    public void Recovered(TimeSpan duration) => Write("INFO", $"UI voltou a responder depois de {Seconds(duration)}s");

    protected abstract void Write(string level, string text);

    private static int Seconds(TimeSpan duration) => (int)Math.Max(0, Math.Floor(duration.TotalSeconds));

    // Só o tipo, o código do erro do Windows e o caminho no código. A mensagem fica de fora: pode carregar endereços ou chaves.
    internal static string Describe(Exception exception)
    {
        var text = new StringBuilder();
        var current = exception;
        for (var depth = 0; current is not null && depth < 4; depth++, current = current.InnerException)
        {
            if (depth > 0) text.Append(" <- ");
            text.Append(current.GetType().FullName).Append(" (0x").Append(current.HResult.ToString("X8")).Append(')');
        }
        // O caminho no código vem de onde o erro nasceu (o mais interno), não de quem o embrulhou.
        var origin = exception;
        while (origin.InnerException is { } deeper) origin = deeper;
        var lines = (origin.StackTrace ?? exception.StackTrace ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length > 0) text.Append(" | ").Append(string.Join(" | ", lines.Take(10)));
        return text.ToString();
    }
}

/// <summary>Registro que não grava nada (testes e o início do programa).</summary>
public sealed class NullAppDiagnostics : AppDiagnostics
{
    protected override void Write(string level, string text) { }
}

/// <summary>Ponto único para o resto do programa achar o registro, sem passar o objeto por todo construtor.</summary>
public static class AppLog
{
    private static volatile IAppDiagnostics current = new NullAppDiagnostics();

    public static IAppDiagnostics Current { get => current; set => current = value ?? new NullAppDiagnostics(); }
    public static void Step(string name) => current.Step(name);
}

/// <summary>
/// Grava em %LOCALAPPDATA%\Futureplace\AgenteFS80H\logs, em dois arquivos de até 512 KB (agente.log e agente.1.log).
/// A gravação roda em outra thread: quem registra só entra numa fila, então um disco ou antivírus lento
/// nunca atrasa a janela. Nenhum método lança erro.
/// </summary>
public sealed class FileDiagnostics : AppDiagnostics, IDisposable
{
    public const long DefaultMaximumBytes = 512 * 1024;
    public const string CurrentName = "agente.log";
    public const string PreviousName = "agente.1.log";
    private const int MaximumQueue = 2000;
    private const int MaximumLine = 1000;
    private const int MaximumBatchBytes = 64 * 1024;
    private static readonly Encoding Utf8 = new UTF8Encoding(false);
    private readonly string directory;
    private readonly long maximumBytes;
    private readonly TimeProvider clock;
    private readonly BlockingCollection<string> queue = new(MaximumQueue);
    private readonly Thread writer;
    private int pending;
    private volatile bool disposed;

    public FileDiagnostics(string directory, long maximumBytes = DefaultMaximumBytes, TimeProvider? clock = null)
    {
        this.directory = directory;
        this.maximumBytes = maximumBytes;
        this.clock = clock ?? TimeProvider.System;
        writer = new Thread(WriteLoop) { IsBackground = true, Name = "Registro local", Priority = ThreadPriority.BelowNormal };
        writer.Start();
    }

    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Futureplace", "AgenteFS80H", "logs");

    protected override void Write(string level, string text)
    {
        if (disposed) return;
        var counted = false;
        try
        {
            var clean = text.Replace('\r', ' ').Replace('\n', ' ');
            if (clean.Length > MaximumLine) clean = clean[..MaximumLine] + "…";
            var line = $"{clock.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} {level,-7} {clean}";
            Interlocked.Increment(ref pending);
            counted = true;
            if (queue.TryAdd(line)) return;
        }
        catch (Exception) { }
        // Fila cheia ou encerrando: perde a linha, nunca atrasa quem registrou.
        if (counted) Interlocked.Decrement(ref pending);
    }

    /// <summary>Espera as linhas já registradas chegarem ao disco. Devolve false se passou do prazo.</summary>
    public bool Flush(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (Volatile.Read(ref pending) > 0)
        {
            if (DateTime.UtcNow >= until) return false;
            Thread.Sleep(5);
        }
        return true;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            queue.CompleteAdding();
            writer.Join(TimeSpan.FromSeconds(3));
        }
        catch (Exception) { }
    }

    private void WriteLoop()
    {
        var batch = new List<string>();
        try
        {
            foreach (var first in queue.GetConsumingEnumerable())
            {
                batch.Clear();
                batch.Add(first);
                var bytes = first.Length;
                while (bytes < MaximumBatchBytes && queue.TryTake(out var next))
                {
                    batch.Add(next);
                    bytes += next.Length;
                }
                Append(batch);
                Interlocked.Add(ref pending, -batch.Count);
            }
        }
        catch (Exception) { }
    }

    private void Append(List<string> lines)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var current = Path.Combine(directory, CurrentName);
            var length = File.Exists(current) ? new FileInfo(current).Length : 0;
            var chunk = new StringBuilder();
            long chunkBytes = 0;
            foreach (var line in lines)
            {
                var size = Utf8.GetByteCount(line) + Environment.NewLine.Length;
                if (length + chunkBytes + size > maximumBytes)
                {
                    Save(current, chunk);
                    chunk.Clear();
                    chunkBytes = 0;
                    // O arquivo cheio vira o "anterior" e o anterior mais velho é descartado.
                    if (File.Exists(current)) File.Move(current, Path.Combine(directory, PreviousName), overwrite: true);
                    length = 0;
                }
                chunk.AppendLine(line);
                chunkBytes += size;
            }
            Save(current, chunk);
        }
        catch (Exception) { }
    }

    private static void Save(string path, StringBuilder text)
    {
        if (text.Length > 0) File.AppendAllText(path, text.ToString(), Utf8);
    }
}

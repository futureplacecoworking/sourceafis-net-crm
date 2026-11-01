using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Futureplace.AgenteFS80H;

/// <summary>Quem recebe os erros e travamentos que o registro local anota, para depois avisar a Central.</summary>
public interface IIncidentRecorder
{
    /// <summary><paramref name="stage"/> é a etapa (ou o componente) em que o erro aconteceu; vira o código `etapa` depois de limpo.</summary>
    void RecordError(string stage, Exception exception);
    /// <summary>A janela parou de responder; <paramref name="lastStep"/> é a última etapa marcada.</summary>
    void RecordHang(TimeSpan duration, string lastStep);
    /// <summary>A janela voltou; <paramref name="duration"/> é o tempo total parada.</summary>
    void RecordRecovered(TimeSpan duration);
}

/// <summary>
/// Um aviso para a Central (ação `relatar`). Só códigos técnicos: nunca nomes, digitais, imagens, tokens,
/// caminhos de arquivo nem a mensagem de uma exceção.
/// </summary>
public sealed record IncidentReport(DateTimeOffset At, string Kind, string Stage, string Code, int Seconds, int Count, string Version)
{
    public const string ErrorKind = "erro";
    public const string HangKind = "travamento";
    /// <summary>Código dos travamentos: a janela não respondeu.</summary>
    public const string HangCode = "ui_sem_resposta";

    /// <summary>Hora em ISO 8601 UTC com Z, sem frações: 2026-10-31T15:00:00Z.</summary>
    public static string FormatTime(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

/// <summary>Um lote tirado da fila para envio. Até a Central confirmar, os avisos continuam guardados.</summary>
public sealed class IncidentBatch
{
    internal IncidentBatch(IncidentEntry[] entries, IncidentReport[] items)
    {
        Entries = entries;
        Items = items;
    }

    internal IncidentEntry[] Entries { get; }
    public IReadOnlyList<IncidentReport> Items { get; }
}

internal sealed class IncidentEntry(DateTimeOffset at, string kind, string stage, string code, int seconds, int count, string version)
{
    public DateTimeOffset At { get; } = at;
    public string Kind { get; } = kind;
    public string Stage { get; } = stage;
    public string Code { get; } = code;
    public int Seconds { get; set; } = seconds;
    public int Count { get; set; } = count;
    public string Version { get; } = version;
    /// <summary>Já foi entregue num lote ainda sem resposta: repetições novas viram outro aviso.</summary>
    public bool InFlight { get; set; }
}

/// <summary>
/// Fila de erros e travamentos a avisar à Central. Alimentada pelo registro local (<see cref="AppDiagnostics.Incidents"/>):
/// junta os repetidos (mesmo tipo, etapa e código em 10 minutos), guarda no máximo 200 avisos, descarta os de mais de
/// 90 dias e se grava em <c>relatos.json</c> (fora da thread da janela) para sobreviver a um reinício.
/// Nenhum método lança erro. Quem envia é o ciclo do leitor (<see cref="AgentRuntime"/>), nunca a janela.
/// </summary>
public sealed class ErrorOutbox : IIncidentRecorder, IDisposable
{
    public const int MaximumItems = 200;
    public const int BatchSize = 20;
    public const int MaximumCount = 1000;
    public const int MaximumSeconds = 86400;
    public const string FileName = "relatos.json";
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MaximumAge = TimeSpan.FromDays(90);
    private const int FormatVersion = 1;
    private const long MaximumFileBytes = 512 * 1024;
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    private readonly object gate = new();
    private readonly List<IncidentEntry> entries = [];
    private readonly string version;
    private readonly TimeProvider clock;
    private readonly string? path;
    private readonly AutoResetEvent? wake;
    private readonly Thread? writer;
    private OpenHang? openHang;
    private long dirty;
    private long saved;
    private volatile bool disposed;

    /// <param name="path">Arquivo onde a fila é guardada. Nulo: só na memória (testes).</param>
    /// <param name="version">Versão do programa (x.y.z), enviada em cada aviso novo.</param>
    public ErrorOutbox(string? path, string version, TimeProvider? clock = null)
    {
        this.path = path;
        this.version = CentralClient.IsStableVersion(version) ? version : "0.0.0";
        this.clock = clock ?? TimeProvider.System;
        if (path is null) return;
        Load();
        wake = new AutoResetEvent(false);
        writer = new Thread(WriteLoop) { IsBackground = true, Name = "Fila de avisos", Priority = ThreadPriority.BelowNormal };
        writer.Start();
        // Um travamento que ficou aberto (o programa foi encerrado preso) já virou aviso no Load: grava sem ele.
        if (Volatile.Read(ref dirty) > 0) wake.Set();
    }

    /// <summary>Pasta e nome padrão: junto do registro local, em <c>logs</c>.</summary>
    public static string DefaultPath => Path.Combine(FileDiagnostics.DefaultDirectory, FileName);

    /// <summary>Quantos avisos estão guardados (os de um lote em envio contam até a Central confirmar).</summary>
    public int Count
    {
        get { lock (gate) return entries.Count; }
    }

    /// <summary>Cópia dos avisos guardados, do mais antigo para o mais novo.</summary>
    public IReadOnlyList<IncidentReport> Snapshot()
    {
        lock (gate) return entries.Select(entry => ToReport(entry, entry.At)).ToArray();
    }

    public void RecordError(string stage, Exception exception)
    {
        try
        {
            var cleanStage = IncidentText.Stage(stage);
            var code = IncidentText.Code(exception);
            lock (gate)
            {
                Add(clock.GetUtcNow(), IncidentReport.ErrorKind, cleanStage, code, 0, version);
                MarkDirty();
            }
        }
        catch (Exception) { }
    }

    public void RecordHang(TimeSpan duration, string lastStep)
    {
        try
        {
            var now = clock.GetUtcNow();
            var seconds = IncidentText.Seconds(duration);
            lock (gate)
            {
                // Um travamento aberto que nunca voltou (não deveria acontecer) é fechado com o tempo que se sabe dele.
                CloseOpenHang(null);
                openHang = new OpenHang(now - TimeSpan.FromSeconds(seconds), IncidentText.Stage(lastStep), seconds, version);
                MarkDirty();
            }
        }
        catch (Exception) { }
    }

    public void RecordRecovered(TimeSpan duration)
    {
        try
        {
            lock (gate)
            {
                if (openHang is null) return;
                CloseOpenHang(IncidentText.Seconds(duration));
                MarkDirty();
            }
        }
        catch (Exception) { }
    }

    /// <summary>Tira da fila até 20 avisos (os mais antigos) para enviar. Nulo quando não há nada a enviar.</summary>
    public IncidentBatch? TakeBatch()
    {
        try
        {
            lock (gate)
            {
                var now = clock.GetUtcNow();
                Prune(now);
                var taken = entries.Where(entry => !entry.InFlight).Take(BatchSize).ToArray();
                if (taken.Length == 0) return null;
                foreach (var entry in taken) entry.InFlight = true;
                // Um relógio que andou para trás não pode mandar um aviso "do futuro".
                return new IncidentBatch(taken, taken.Select(entry => ToReport(entry, entry.At > now ? now : entry.At)).ToArray());
            }
        }
        catch (Exception) { return null; }
    }

    /// <summary>A Central recebeu: os <paramref name="accepted"/> primeiros avisos do lote saem da fila e o resto volta a ficar pendente.</summary>
    public void Accept(IncidentBatch batch, int accepted)
    {
        try
        {
            lock (gate)
            {
                var done = batch.Entries.Take(Math.Clamp(accepted, 0, batch.Entries.Length)).ToHashSet();
                entries.RemoveAll(done.Contains);
                foreach (var entry in batch.Entries) entry.InFlight = false;
                MarkDirty();
            }
        }
        catch (Exception) { }
    }

    /// <summary>O envio não deu certo: o lote volta inteiro para a fila.</summary>
    public void Return(IncidentBatch batch)
    {
        try
        {
            lock (gate)
                foreach (var entry in batch.Entries) entry.InFlight = false;
        }
        catch (Exception) { }
    }

    /// <summary>Espera a fila chegar ao disco. Devolve false se passou do prazo.</summary>
    public bool Flush(TimeSpan timeout)
    {
        if (writer is null || disposed) return true;
        var until = DateTime.UtcNow + timeout;
        while (Volatile.Read(ref saved) < Volatile.Read(ref dirty))
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
            wake?.Set();
            if (writer is not null && writer.Join(TimeSpan.FromSeconds(3))) wake?.Dispose();
        }
        catch (Exception) { }
    }

    // ---- fila (sempre com o gate) ----

    private void Add(DateTimeOffset at, string kind, string stage, string code, int seconds, string itemVersion)
    {
        Prune(clock.GetUtcNow());
        seconds = Math.Clamp(seconds, 0, MaximumSeconds);
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            var entry = entries[index];
            if (entry.InFlight || entry.Kind != kind || entry.Stage != stage || entry.Code != code || (at - entry.At).Duration() >= CoalesceWindow) continue;
            entry.Count = Math.Min(entry.Count + 1, MaximumCount);
            // Vários travamentos juntos: vale o mais longo.
            entry.Seconds = Math.Max(entry.Seconds, seconds);
            return;
        }
        entries.Add(new IncidentEntry(at, kind, stage, code, seconds, 1, itemVersion));
        while (entries.Count > MaximumItems) entries.RemoveAt(0);
    }

    private void CloseOpenHang(int? totalSeconds)
    {
        if (openHang is not { } open) return;
        openHang = null;
        Add(open.At, IncidentReport.HangKind, open.Stage, IncidentReport.HangCode, Math.Max(open.Seconds, totalSeconds ?? 0), open.Version);
    }

    private void Prune(DateTimeOffset now) => entries.RemoveAll(entry => !entry.InFlight && now - entry.At > MaximumAge);

    private static IncidentReport ToReport(IncidentEntry entry, DateTimeOffset at) =>
        new(at, entry.Kind, entry.Stage, entry.Code, entry.Seconds, entry.Count, entry.Version);

    // ---- disco ----

    private void MarkDirty()
    {
        Interlocked.Increment(ref dirty);
        try { wake?.Set(); }
        catch (ObjectDisposedException) { }
    }

    private void WriteLoop()
    {
        try
        {
            while (true)
            {
                wake!.WaitOne();
                var target = Volatile.Read(ref dirty);
                if (target != Volatile.Read(ref saved))
                {
                    string json;
                    lock (gate) json = Serialize();
                    Save(json);
                    Volatile.Write(ref saved, target);
                }
                if (disposed) break;
            }
        }
        catch (Exception) { }
    }

    private string Serialize()
    {
        var file = new StoredFile
        {
            Format = FormatVersion,
            Open = openHang is { } open ? new StoredHang { Em = IncidentReport.FormatTime(open.At), Etapa = open.Stage, Segundos = open.Seconds, Versao = open.Version } : null,
            Items = entries.Select(entry => new StoredItem
            {
                Em = IncidentReport.FormatTime(entry.At), Tipo = entry.Kind, Etapa = entry.Stage, Codigo = entry.Code,
                Segundos = entry.Seconds, Quantidade = entry.Count, Versao = entry.Version
            }).ToList()
        };
        return JsonSerializer.Serialize(file);
    }

    private void Save(string json)
    {
        try
        {
            var directory = Path.GetDirectoryName(path!);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            // Grava num arquivo ao lado e troca: uma queda no meio nunca deixa a fila pela metade.
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, json, Utf8);
            File.Move(temporary, path!, overwrite: true);
        }
        catch (Exception) { }
    }

    private void Load()
    {
        try
        {
            var info = new FileInfo(path!);
            if (!info.Exists || info.Length > MaximumFileBytes) return;
            var file = JsonSerializer.Deserialize<StoredFile>(File.ReadAllBytes(path!));
            if (file is null || file.Format != FormatVersion) return;
            var now = clock.GetUtcNow();
            foreach (var item in file.Items ?? [])
                if (ReadEntry(item) is { } entry && now - entry.At <= MaximumAge) entries.Add(entry);
            while (entries.Count > MaximumItems) entries.RemoveAt(0);
            // O programa foi encerrado com a janela presa: o travamento vira aviso com o tempo que se sabia.
            if (file.Open is { } open && ReadHang(open) is { } hang && now - hang.At <= MaximumAge)
            {
                openHang = hang;
                CloseOpenHang(null);
            }
            if (file.Open is not null) Interlocked.Increment(ref dirty);
        }
        catch (Exception)
        {
            // Arquivo estragado: começa vazio (o próximo aviso grava um arquivo novo).
            entries.Clear();
            openHang = null;
        }
    }

    private static IncidentEntry? ReadEntry(StoredItem? item)
    {
        if (item is null || !TryTime(item.Em, out var at) || item.Tipo is not (IncidentReport.ErrorKind or IncidentReport.HangKind) ||
            !IncidentText.IsStage(item.Etapa) || !IncidentText.IsCode(item.Codigo) || !CentralClient.IsStableVersion(item.Versao) ||
            item.Segundos is < 0 or > MaximumSeconds || item.Quantidade is < 1 or > MaximumCount) return null;
        return new IncidentEntry(at, item.Tipo!, item.Etapa!, item.Codigo!, item.Segundos, item.Quantidade, item.Versao!);
    }

    private static OpenHang? ReadHang(StoredHang hang) =>
        TryTime(hang.Em, out var at) && IncidentText.IsStage(hang.Etapa) && CentralClient.IsStableVersion(hang.Versao) && hang.Segundos is >= 0 and <= MaximumSeconds
            ? new OpenHang(at, hang.Etapa!, hang.Segundos, hang.Versao!)
            : null;

    private static bool TryTime(string? text, out DateTimeOffset value) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);

    private sealed record OpenHang(DateTimeOffset At, string Stage, int Seconds, string Version);

    private sealed class StoredFile
    {
        [JsonPropertyName("formato")] public int Format { get; set; }
        [JsonPropertyName("aberto")] public StoredHang? Open { get; set; }
        [JsonPropertyName("itens")] public List<StoredItem>? Items { get; set; }
    }

    private sealed class StoredHang
    {
        [JsonPropertyName("em")] public string? Em { get; set; }
        [JsonPropertyName("etapa")] public string? Etapa { get; set; }
        [JsonPropertyName("segundos")] public int Segundos { get; set; }
        [JsonPropertyName("versao")] public string? Versao { get; set; }
    }

    private sealed class StoredItem
    {
        [JsonPropertyName("em")] public string? Em { get; set; }
        [JsonPropertyName("tipo")] public string? Tipo { get; set; }
        [JsonPropertyName("etapa")] public string? Etapa { get; set; }
        [JsonPropertyName("codigo")] public string? Codigo { get; set; }
        [JsonPropertyName("segundos")] public int Segundos { get; set; }
        [JsonPropertyName("quantidade")] public int Quantidade { get; set; }
        [JsonPropertyName("versao")] public string? Versao { get; set; }
    }
}

/// <summary>Transforma nomes de etapa e tipos de exceção nos códigos que a Central aceita.</summary>
internal static class IncidentText
{
    public const int MaximumStage = 60;
    public const int MaximumCode = 120;
    private const string AccentedFrom = "áàâãäéèêëíìîïóòôõöúùûüçñÁÀÂÃÄÉÈÊËÍÌÎÏÓÒÔÕÖÚÙÛÜÇÑ";
    private const string AccentedTo = "aaaaaeeeeiiiiooooouuuucnAAAAAEEEEIIIIOOOOOUUUUCN";

    public static int Seconds(TimeSpan duration) => (int)Math.Clamp(Math.Floor(duration.TotalSeconds), 0, ErrorOutbox.MaximumSeconds);

    /// <summary>Etapa: letras, números, _ . - e espaço (até 60). Acentos saem; ":" vira "."; o resto vira "_".</summary>
    public static string Stage(string? value)
    {
        var text = new StringBuilder();
        foreach (var raw in value ?? "")
        {
            if (text.Length >= MaximumStage) break;
            var ch = Plain(raw);
            text.Append(ch == ':' ? '.' : IsStageChar(ch) ? ch : '_');
        }
        var clean = text.ToString().Trim();
        return clean.Length == 0 ? "desconhecida" : clean;
    }

    /// <summary>
    /// Código: o tipo da exceção (sem a mensagem, que pode ter nomes ou caminhos) e o código do erro do Windows,
    /// como <c>System.IO.IOException:0x80070020</c>. Exceções que só embrulham outra mostram a de dentro.
    /// </summary>
    public static string Code(Exception exception)
    {
        var current = exception;
        for (var depth = 0; depth < 8 && (current is AggregateException || current is TargetInvocationException) && current.InnerException is { } inner; depth++)
            current = inner;
        var suffix = ":0x" + current.HResult.ToString("X8", CultureInfo.InvariantCulture);
        var name = current.GetType().FullName ?? current.GetType().Name;
        var text = new StringBuilder();
        foreach (var ch in name)
        {
            if (text.Length >= MaximumCode - suffix.Length) break;
            text.Append(IsCodeChar(ch) && ch != ':' ? ch : '_');
        }
        return text.Length == 0 ? "Exception" + suffix : text.ToString() + suffix;
    }

    public static bool IsStage(string? value) => value is { Length: >= 1 and <= MaximumStage } && value.All(IsStageChar);
    public static bool IsCode(string? value) => value is { Length: >= 1 and <= MaximumCode } && value.All(IsCodeChar);

    private static char Plain(char ch)
    {
        var index = AccentedFrom.IndexOf(ch);
        return index >= 0 ? AccentedTo[index] : ch;
    }

    private static bool IsStageChar(char ch) => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '.' or ' ' or '-';
    private static bool IsCodeChar(char ch) => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '.' or ':' or '+' or '-';
}

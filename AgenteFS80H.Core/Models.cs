using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Futureplace.AgenteFS80H;

public sealed record AgentConfiguration
{
    public string CentralUrl { get; init; } = "https://central.futureplacecoworking.com.br";
    public string ProjectUrl { get; init; } = "";
    public string PublicApiKey { get; init; } = "";
    public string ScannerDllPath { get; init; } = "";
    public Guid DeviceId { get; init; }
    public string DeviceToken { get; init; } = "";
    // Nome do leitor na Central (resposta de parear e sincronizar). Arquivos da 1.3 não têm o campo.
    public string DeviceName { get; init; } = "";
    // Som curto de ok e de não. Arquivos da 1.3 não têm o campo: começa ligado.
    public bool SoundEnabled { get; init; } = true;
    [JsonIgnore] public bool IsPaired => DeviceId != Guid.Empty && !string.IsNullOrEmpty(DeviceToken);
    public override string ToString() => "Configuração protegida do leitor";
}

public sealed record AgentState
{
    public AgentConfiguration Configuration { get; init; } = new();
    public List<Guid> PendingRemovals { get; init; } = [];
    public bool DeviceRevoked { get; init; }
    public bool TotalRemovalPending { get; init; }
}

public sealed record StoredTemplate(Guid CadastroId, Guid ComandoId, byte[] Template)
{
    // Leituras boas adicionais do mesmo dedo (1.4). Arquivos antigos guardam só `template`.
    public byte[][]? ExtraTemplates { get; init; }

    public IEnumerable<byte[]> AllTemplates()
    {
        yield return Template;
        if (ExtraTemplates is { } extras)
            foreach (var extra in extras)
                if (extra is not null) yield return extra;
    }

    public int Count => 1 + (ExtraTemplates?.Count(extra => extra is not null) ?? 0);

    public void Clear()
    {
        if (Template is not null) CryptographicOperations.ZeroMemory(Template);
        if (ExtraTemplates is { } extras)
            foreach (var extra in extras)
                if (extra is not null) CryptographicOperations.ZeroMemory(extra);
    }

    public override string ToString() => "Template biométrico protegido";
}
public sealed record FingerprintFrame(int Width, int Height, byte[] Pixels)
{
    public override string ToString() => "Imagem biométrica transitória";
}

public enum AgentStatus { Unconfigured, Connecting, Ready, Capturing, Authorized, Denied, Revoked, Offline, Error }

/// <summary>Orientação de espera, para as telas não dependerem do texto da mensagem.</summary>
public enum ReaderHint { None, PlaceFinger, LiftFinger }

/// <summary>Sinal de resultado para o som curto do leitor.</summary>
public enum AgentSignal { Positive, Negative }

/// <summary>Estado atual do leitor. A mensagem é para a recepção; as telas usam os campos estruturados.</summary>
public sealed record AgentSnapshot(AgentStatus Status, string Message)
{
    public ReaderHint Hint { get; init; }
    public IdentificationProgress? Identification { get; init; }
    public EnrollmentProgress? Enrollment { get; init; }
    public DateTimeOffset? LastContactAt { get; init; }
}

public enum IdentificationResult { InProgress, Authorized, Denied, NotRecognized, BadReading }

/// <summary>
/// Etapas da identificação: 1 digital lida, 2 pessoa reconhecida, 3 Central confirmou,
/// 4 recepção avisada (ou porta liberada, quando a fechadura existir).
/// </summary>
public sealed record IdentificationProgress
{
    public const int Steps = 4;
    public int Completed { get; init; }
    public int? FailedStep { get; init; }
    public IdentificationResult Result { get; init; }
    public string? Motivo { get; init; }
    public string? Name { get; init; }
    public DisplayInfo? Display { get; init; }
    public bool LockInstalled { get; init; }
    public override string ToString() => "Andamento da identificação";
}

public enum EnrollmentPhase { Place, Reading, Lift, BadReading, Checking, Completed, Failed, Cancelled }

/// <summary>Andamento do cadastro: leitura atual, total pedido e quantas leituras boas já foram guardadas.</summary>
public sealed record EnrollmentProgress
{
    public string Name { get; init; } = "";
    public string? Company { get; init; }
    public int Reading { get; init; } = 1;
    public int Total { get; init; } = 2;
    public int Good { get; init; }
    public EnrollmentPhase Phase { get; init; }
    public string? Motivo { get; init; }
    public override string ToString() => "Andamento do cadastro";
}

/// <summary>Cartão de quem se identificou, como a Central calculou (`exibir`). Só texto; sem dados biométricos.</summary>
public sealed record DisplayInfo
{
    public string? Nome { get; init; }
    public string? PrimeiroNome { get; init; }
    public string? Empresa { get; init; }
    public string? Sala { get; init; }
    public string? Codigo { get; init; }
    public string? Validade { get; init; }
    public string? ValidadeTexto { get; init; }
    public string? Foto { get; init; }
    public string? HorarioTexto { get; init; }

    // Campo desconhecido ou de tipo inesperado é ignorado: o cartão é só apresentação, nunca decide acesso.
    internal static DisplayInfo? From(JsonElement? value)
    {
        if (value is not { ValueKind: JsonValueKind.Object } element) return null;
        var info = new DisplayInfo
        {
            Nome = Text(element, "nome"),
            PrimeiroNome = Text(element, "primeiro_nome"),
            Empresa = Text(element, "empresa"),
            Sala = Text(element, "sala"),
            Codigo = Text(element, "codigo"),
            Validade = Text(element, "validade", 10),
            ValidadeTexto = Text(element, "validade_texto"),
            Foto = Text(element, "foto", 2048),
            HorarioTexto = Text(element, "horario_texto", 500)
        };
        return info == new DisplayInfo() ? null : info;
    }

    private static string? Text(JsonElement element, string name, int maximum = 300)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
        text = text?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > maximum || text.Any(char.IsControl) ? null : text;
    }
}

public sealed class CentralException(string message) : Exception(message);
public sealed class LocalDataException() : Exception("Não foi possível ler os dados protegidos deste usuário. Restaure o perfil original ou limpe o pareamento nas configurações.");
public sealed class CaptureException(string reason) : Exception("Não foi possível confirmar a digital. Retire o dedo e tente novamente.")
{
    public string Reason { get; } = reason;
}

/// <summary>O cadastro terminou sem digital: o motivo vai para a Central em `concluir`.</summary>
public sealed class EnrollmentFailedException(string reason, Guid? duplicateOf = null) : Exception("O cadastro da digital não foi concluído.")
{
    public string Reason { get; } = reason;
    public Guid? DuplicateOf { get; } = duplicateOf;
}

/// <summary>A Central cancelou ou trocou o pedido de cadastro: nada é gravado e `concluir` não é chamado.</summary>
public sealed class EnrollmentCancelledException() : Exception("O cadastro foi cancelado na Central.");

public sealed record PairingResponse
{
    [JsonRequired] public int Protocolo { get; init; }
    [JsonRequired] public string Token { get; init; } = "";
    [JsonRequired] public Guid DispositivoId { get; init; }
    public string? Nome { get; init; }
    public override string ToString() => "Resposta de pareamento protegida";
}
public sealed record SyncResponse
{
    [JsonRequired] public int Protocolo { get; init; }
    public DateTimeOffset? ServidorEm { get; init; }
    [JsonRequired] public bool Revogado { get; init; }
    [JsonRequired] public Guid[] Cadastros { get; init; } = [];
    [JsonRequired] public EnrollmentCommand? Comando { get; init; }
    // Campos da Central nova (opcionais). A Central antiga não envia nenhum deles.
    public string[]? Recursos { get; init; }
    public DeviceInfo? Dispositivo { get; init; }
    public bool? FechaduraInstalada { get; init; }
}
public sealed record DeviceInfo
{
    public Guid? Id { get; init; }
    public string? Nome { get; init; }
}
public sealed record EnrollmentCommand
{
    public const int DefaultReadings = 2;
    public const int MinimumReadings = 2;
    public const int MaximumReadings = 6;
    [JsonRequired] public Guid Id { get; init; }
    [JsonRequired] public string Tipo { get; init; } = "";
    [JsonRequired] public Guid CadastroId { get; init; }
    [JsonRequired] public string Nome { get; init; } = "";
    [JsonRequired] public DateTimeOffset ExpiraEm { get; init; }
    public string? Empresa { get; init; }
    public int? Leituras { get; init; }
    // A Central antiga não informa: duas leituras, como na 1.3.
    [JsonIgnore] public int RequiredReadings => Math.Clamp(Leituras ?? DefaultReadings, MinimumReadings, MaximumReadings);
}
public sealed record DecisionResponse
{
    [JsonRequired] public bool Autorizado { get; init; }
    [JsonRequired] public string Motivo { get; init; } = "";
    [JsonRequired] public string Nome { get; init; } = "";
    [JsonRequired] public bool PortaAcionada { get; init; }
    public JsonElement? Exibir { get; init; }
    [JsonIgnore] public DisplayInfo? Display { get; init; }
}

/// <summary>Dados de `concluir`. Os campos novos só seguem quando a Central anuncia `duplicada_de`.</summary>
public sealed record EnrollmentCompletion(Guid CommandId, string Result, string? Reason = null, Guid? DuplicateOf = null, int? Readings = null);

public interface ICentralClient : IDisposable
{
    /// <summary>Recursos anunciados na última sincronização bem-sucedida (vazio na Central antiga).</summary>
    IReadOnlySet<string> Features { get; }
    Task<SyncResponse> SynchronizeAsync(CancellationToken cancellationToken);
    Task CompleteAsync(EnrollmentCompletion completion, CancellationToken cancellationToken);
    Task<DecisionResponse> IdentifyAsync(Guid operation, Guid enrollment, DateTimeOffset capturedAt, CancellationToken cancellationToken);
    Task ConfirmRemovalsAsync(IReadOnlyCollection<Guid> ids, bool all, CancellationToken cancellationToken);
    /// <summary>Avisa a Central que este leitor foi despareado. Devolve false quando a Central não oferece a ação.</summary>
    Task<bool> UnpairAsync(CancellationToken cancellationToken);
}
public interface ILocalStore
{
    AgentState LoadState();
    void SaveState(AgentState state);
    IReadOnlyList<StoredTemplate> LoadTemplates();
    IReadOnlyList<Guid> ListTemplateIds();
    void DeleteAllTemplates();
    void SaveTemplate(StoredTemplate template);
    void DeleteTemplate(Guid id);
    void Clear();
}
public interface IFingerprintScanner : IDisposable
{
    bool IsFingerPresent();
    FingerprintFrame Capture();
}
public interface IFingerprintRecognizer
{
    byte[] Extract(FingerprintFrame image);
    /// <summary>Prepara a comparação de uma leitura uma vez só, para comparar com vários templates.</summary>
    IFingerprintMatcher Matcher(byte[] probe);
}
public interface IFingerprintMatcher
{
    double Score(byte[] candidate);
}

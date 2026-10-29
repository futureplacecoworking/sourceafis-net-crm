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
    public override string ToString() => "Template biométrico protegido";
}
public sealed record FingerprintFrame(int Width, int Height, byte[] Pixels)
{
    public override string ToString() => "Imagem biométrica transitória";
}

public enum AgentStatus { Unconfigured, Connecting, Ready, Capturing, Authorized, Denied, Revoked, Offline, Error }
public sealed record AgentSnapshot(AgentStatus Status, string Message);
public sealed class CentralException(string message) : Exception(message);
public sealed class LocalDataException() : Exception("Não foi possível ler os dados protegidos deste usuário. Restaure o perfil original ou limpe o pareamento nas configurações.");
public sealed class CaptureException(string reason) : Exception("Não foi possível confirmar a digital. Retire o dedo e tente novamente.")
{
    public string Reason { get; } = reason;
}

public sealed record PairingResponse
{
    [JsonRequired] public int Protocolo { get; init; }
    [JsonRequired] public string Token { get; init; } = "";
    [JsonRequired] public Guid DispositivoId { get; init; }
    public override string ToString() => "Resposta de pareamento protegida";
}
public sealed record SyncResponse
{
    [JsonRequired] public int Protocolo { get; init; }
    public DateTimeOffset? ServidorEm { get; init; }
    [JsonRequired] public bool Revogado { get; init; }
    [JsonRequired] public Guid[] Cadastros { get; init; } = [];
    [JsonRequired] public EnrollmentCommand? Comando { get; init; }
}
public sealed record EnrollmentCommand
{
    [JsonRequired] public Guid Id { get; init; }
    [JsonRequired] public string Tipo { get; init; } = "";
    [JsonRequired] public Guid CadastroId { get; init; }
    [JsonRequired] public string Nome { get; init; } = "";
    [JsonRequired] public DateTimeOffset ExpiraEm { get; init; }
}
public sealed record DecisionResponse
{
    [JsonRequired] public bool Autorizado { get; init; }
    [JsonRequired] public string Motivo { get; init; } = "";
    [JsonRequired] public string Nome { get; init; } = "";
    [JsonRequired] public bool PortaAcionada { get; init; }
}

public interface ICentralClient : IDisposable
{
    Task<SyncResponse> SynchronizeAsync(CancellationToken cancellationToken);
    Task CompleteAsync(Guid command, string result, string? reason, CancellationToken cancellationToken);
    Task<DecisionResponse> IdentifyAsync(Guid operation, Guid enrollment, DateTimeOffset capturedAt, CancellationToken cancellationToken);
    Task ConfirmRemovalsAsync(IReadOnlyCollection<Guid> ids, bool all, CancellationToken cancellationToken);
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
    double Score(byte[] probe, byte[] candidate);
}

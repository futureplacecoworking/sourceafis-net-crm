using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Futureplace.AgenteFS80H;

public static class CentralDiscovery
{
    public static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 1024 * 1024 };

    internal static Uri ValidateCentral(string input)
    {
        if (!Uri.TryCreate(input.Trim().TrimEnd('/'), UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/" ||
            uri.HostNameType != UriHostNameType.Dns || !uri.Host.Contains('.') || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || uri.IsLoopback)
            throw new CentralException("Informe o endereço HTTPS público da Central, sem caminhos ou credenciais.");
        return uri;
    }

    internal static Uri ValidateProject(string input)
    {
        var uri = ValidateCentral(input);
        if (!uri.Host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase))
            throw new CentralException("A configuração da Central não aponta para um projeto Supabase público válido.");
        return uri;
    }

    public static async Task<AgentConfiguration> DiscoverAsync(string centralUrl, HttpClient http, CancellationToken cancellationToken)
    {
        var central = ValidateCentral(centralUrl);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var requestToken = deadline.Token;
        using var response = await http.GetAsync(new Uri(central, "config.js"), HttpCompletionOption.ResponseHeadersRead, requestToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 65536)
            throw new CentralException("Não foi possível obter a configuração pública da Central.");
        var content = await CentralClient.ReadBoundedAsync(response.Content, 65536, requestToken).ConfigureAwait(false);
        try
        {
            var match = Regex.Match(Encoding.UTF8.GetString(content), @"(?:window\.)?CENTRAL_CONFIG\s*=\s*(\{[^;]*?\})\s*;?", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (!match.Success) throw new JsonException();
            using var doc = JsonDocument.Parse(match.Groups[1].Value);
            var project = ValidateProject(doc.RootElement.GetProperty("url").GetString() ?? "");
            var key = doc.RootElement.GetProperty("chave").GetString() ?? "";
            if (!IsPublicKey(key)) throw new JsonException();
            return new AgentConfiguration { CentralUrl = central.GetLeftPart(UriPartial.Authority), ProjectUrl = project.GetLeftPart(UriPartial.Authority), PublicApiKey = key };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or RegexMatchTimeoutException or FormatException)
        {
            throw new CentralException("A configuração pública da Central é inválida. Confirme o endereço com a administração.");
        }
    }

    private static bool IsPublicKey(string key)
    {
        if (key.Length is < 10 or > 8192 || key.Any(char.IsWhiteSpace)) return false;
        if (key.StartsWith("sb_publishable_", StringComparison.Ordinal)) return true;
        var parts = key.Split('.');
        if (parts.Length != 3) return false;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            return json.RootElement.GetProperty("role").GetString() == "anon";
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException) { return false; }
    }
}

public sealed class CentralClient : ICentralClient
{
    // 500 UUIDs excedem o limite HTTP de 16 KiB do Edge. 400 deixam margem para o envelope JSON.
    public const int RemovalBatchSize = 400;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly AgentConfiguration configuration;
    private readonly HttpClient http;
    private readonly Uri endpoint;
    private readonly TimeSpan requestTimeout;

    public CentralClient(AgentConfiguration configuration, HttpMessageHandler? handler = null, TimeSpan? requestTimeout = null)
    {
        this.configuration = configuration;
        this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(15);
        endpoint = new Uri(CentralDiscovery.ValidateProject(configuration.ProjectUrl), "functions/v1/biometria");
        http = handler is null ? CentralDiscovery.CreateHttpClient() : new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<PairingResponse> PairAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 128 || code.Any(char.IsControl)) throw new CentralException("Informe o código de pareamento gerado na Central.");
        var result = await SendAsync<PairingResponse>("parear", new { Codigo = code.Trim() }, cancellationToken, authenticated: false).ConfigureAwait(false);
        if (result.Protocolo != 1 || result.DispositivoId == Guid.Empty || result.Token.Length is < 20 or > 4096 || result.Token.Any(char.IsWhiteSpace))
            throw new CentralException("A Central devolveu um pareamento inválido.");
        return result;
    }

    public async Task<SyncResponse> SynchronizeAsync(CancellationToken cancellationToken)
    {
        var sync = await SendAsync<SyncResponse>("sincronizar", new { }, cancellationToken).ConfigureAwait(false);
        if (sync.Protocolo != 1 || sync.Cadastros is null || sync.Cadastros.Length > 10000 || sync.Cadastros.Any(id => id == Guid.Empty) || sync.Cadastros.Distinct().Count() != sync.Cadastros.Length ||
            (!sync.Revogado && sync.ServidorEm is null) || (sync.Revogado && (sync.Cadastros.Length != 0 || sync.Comando is not null)))
            throw new CentralException("A Central devolveu uma sincronização incompatível. Nenhum acesso foi autorizado.");
        if (sync.Comando is { } command && (command.Id == Guid.Empty || command.CadastroId == Guid.Empty || command.Tipo != "cadastrar" || !sync.Cadastros.Contains(command.CadastroId) ||
            command.Nome is null || command.Nome.Length > 300 || command.ExpiraEm > sync.ServidorEm!.Value.AddMinutes(5).AddSeconds(5)))
            throw new CentralException("A Central devolveu uma operação de cadastro inválida.");
        return sync;
    }

    public async Task CompleteAsync(Guid command, string result, string? reason, CancellationToken cancellationToken) =>
        _ = await SendAsync<JsonElement>("concluir", new { ComandoId = command, Resultado = result, Motivo = reason }, cancellationToken).ConfigureAwait(false);

    public async Task<DecisionResponse> IdentifyAsync(Guid operation, Guid enrollment, DateTimeOffset capturedAt, CancellationToken cancellationToken)
    {
        var decision = await SendAsync<DecisionResponse>("identificar", new { OperacaoId = operation, CadastroId = enrollment, CapturadoEm = capturedAt.ToUniversalTime() }, cancellationToken).ConfigureAwait(false);
        if (decision.PortaAcionada || decision.Nome is null || decision.Nome.Length > 300 || decision.Motivo is null || decision.Motivo.Length > 128)
            throw new CentralException("A Central devolveu uma decisão incompatível. Nenhum acesso foi autorizado.");
        return decision;
    }

    public async Task ConfirmRemovalsAsync(IReadOnlyCollection<Guid> ids, bool all, CancellationToken cancellationToken)
    {
        object data = all ? new { Todos = true } : new { Cadastros = ids };
        _ = await SendAsync<JsonElement>("confirmar_remocao", data, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> SendAsync<T>(string action, object data, CancellationToken cancellationToken, bool authenticated = true)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(requestTimeout);
        var requestToken = deadline.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("apikey", configuration.PublicApiKey);
        if (authenticated)
        {
            if (!configuration.IsPaired) throw new CentralException("O leitor precisa ser pareado na Central.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.DeviceToken);
        }
        request.Content = JsonContent.Create(new { Acao = action, Dados = data }, options: Json);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new CentralException(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? "A Central recusou a credencial deste leitor. Confira o pareamento ou a revogação." : "A Central não confirmou a operação. Nenhum acesso foi autorizado.");
        try
        {
            var bytes = await ReadBoundedAsync(response.Content, 1024 * 1024, requestToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.GetProperty("ok").ValueKind != JsonValueKind.True) throw new JsonException();
            return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new JsonException();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // Nunca refletir corpo remoto ou mensagens arbitrárias: podem conter dados privados.
            throw new CentralException("A Central não confirmou a operação. Confira a conexão e tente novamente.");
        }
    }

    internal static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > limit) throw new CentralException("A resposta da Central excedeu o limite permitido.");
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > limit) throw new CentralException("A resposta da Central excedeu o limite permitido.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
    public void Dispose() => http.Dispose();
}

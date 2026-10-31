using System.Net;
using System.Text;
using System.Text.Json;
using Futureplace.AgenteFS80H;
using NUnit.Framework;

namespace AgenteFS80H.Tests;

public class ProtocolTests
{
    private static AgentConfiguration Configuration => new()
    {
        ProjectUrl = "https://project.supabase.co",
        PublicApiKey = "sb_publishable_test",
        DeviceId = Guid.NewGuid(),
        DeviceToken = "device-secret"
    };

    [Test]
    public async Task IdentificationSendsOnlyOpaqueIdsAndCaptureTime()
    {
        string? body = null;
        string? token = null;
        using var client = new CentralClient(Configuration, new Handler(async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            token = request.Headers.Authorization?.ToString();
            return Json("{\"ok\":true,\"autorizado\":false,\"motivo\":\"horario\",\"nome\":\"Pessoa\",\"porta_acionada\":false}");
        }));
        var operation = Guid.NewGuid();
        var enrollment = Guid.NewGuid();
        await client.IdentifyAsync(operation, enrollment, DateTimeOffset.UtcNow, CancellationToken.None);
        using var document = JsonDocument.Parse(body!);
        var root = document.RootElement;
        Assert.That(root.GetProperty("acao").GetString(), Is.EqualTo("identificar"));
        var data = root.GetProperty("dados");
        Assert.That(data.EnumerateObject().Select(p => p.Name), Is.EquivalentTo(new[] { "operacao_id", "cadastro_id", "capturado_em" }));
        Assert.That(data.GetProperty("operacao_id").GetGuid(), Is.EqualTo(operation));
        Assert.That(token, Is.EqualTo("Bearer device-secret"));
        Assert.That(body, Does.Not.Contain("template").And.Not.Contain("pixels").And.Not.Contain("device-secret"));
    }

    [TestCase("{\"ok\":true,\"autorizado\":true,\"motivo\":\"ok\",\"nome\":\"Pessoa\",\"porta_acionada\":true}")]
    [TestCase("{\"ok\":true,\"autorizado\":true,\"motivo\":\"ok\",\"nome\":\"Pessoa\"}")]
    [TestCase("{\"ok\":false,\"erro\":\"device-secret\"}")]
    public void InvalidDecisionsFailClosedWithoutReflectingResponse(string response)
    {
        using var client = new CentralClient(Configuration, new Handler(_ => Task.FromResult(Json(response))));
        var error = Assert.ThrowsAsync<CentralException>(async () => await client.IdentifyAsync(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.That(error!.Message, Does.Not.Contain("device-secret"));
    }

    [Test]
    public async Task SuccessfulCompletionOmitsOptionalFailureReason()
    {
        string? payload = null;
        using var client = new CentralClient(Configuration, new Handler(async request =>
        {
            payload = await request.Content!.ReadAsStringAsync();
            return Json("{\"ok\":true}");
        }));
        await client.CompleteAsync(new EnrollmentCompletion(Guid.NewGuid(), "cadastrado"), CancellationToken.None);
        using var json = JsonDocument.Parse(payload!);
        var data = json.RootElement.GetProperty("dados");
        Assert.That(data.GetProperty("resultado").GetString(), Is.EqualTo("cadastrado"));
        Assert.That(data.TryGetProperty("motivo", out _), Is.False);
    }

    private const string OldSync = "{\"ok\":true,\"protocolo\":1,\"servidor_em\":\"SERVER\",\"revogado\":false,\"cadastros\":[],\"comando\":null}";
    private const string NewSync = "{\"ok\":true,\"protocolo\":1,\"servidor_em\":\"SERVER\",\"revogado\":false,\"cadastros\":[],\"comando\":null," +
        "\"recursos\":[\"versao\",\"leituras_multiplas\",\"exibir\",\"desparear\",\"duplicada_de\"],\"dispositivo\":{\"id\":\"10000000-0000-0000-0000-000000000001\",\"nome\":\"Recepção — FS80H\"},\"fechadura_instalada\":false,\"campo_futuro\":{\"x\":1}}";
    private static string Now(string body) => body.Replace("SERVER", DateTimeOffset.UtcNow.ToString("O"));

    private static (CentralClient Client, List<(string Action, JsonElement Data)> Requests) Recording(Func<string, string> respond)
    {
        var requests = new List<(string, JsonElement)>();
        var client = new CentralClient(Configuration, new Handler(async request =>
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var action = document.RootElement.GetProperty("acao").GetString()!;
            requests.Add((action, document.RootElement.GetProperty("dados").Clone()));
            return Json(respond(action));
        }), appVersion: "1.4.0");
        return (client, requests);
    }

    [Test]
    public async Task OldCentralNeverReceivesFieldsItWouldRejectWithHttp400()
    {
        var (client, requests) = Recording(action => action == "sincronizar" ? Now(OldSync) : "{\"ok\":true}");
        using (client)
        {
            await client.SynchronizeAsync(CancellationToken.None);
            await client.SynchronizeAsync(CancellationToken.None);
            await client.CompleteAsync(new EnrollmentCompletion(Guid.NewGuid(), "falhou", "duplicada", Guid.NewGuid(), 4), CancellationToken.None);
            Assert.That(await client.UnpairAsync(CancellationToken.None), Is.False);
        }
        Assert.That(requests.Select(r => r.Action), Is.EqualTo(new[] { "sincronizar", "sincronizar", "concluir" }), "Sem `desparear` para a Central antiga.");
        Assert.That(requests[0].Data.EnumerateObject(), Is.Empty);
        Assert.That(requests[1].Data.EnumerateObject(), Is.Empty);
        Assert.That(requests[2].Data.EnumerateObject().Select(p => p.Name), Is.EquivalentTo(new[] { "comando_id", "resultado", "motivo" }));
        Assert.That(client.Features, Is.Empty);
    }

    [Test]
    public async Task NewCentralGetsVersionReadingsAndUnpairOnlyAfterAnnouncingThem()
    {
        var other = Guid.NewGuid();
        var (client, requests) = Recording(action => action == "sincronizar" ? Now(NewSync) : "{\"ok\":true}");
        using (client)
        {
            var first = await client.SynchronizeAsync(CancellationToken.None);
            Assert.That(first.Dispositivo?.Nome, Is.EqualTo("Recepção — FS80H"));
            Assert.That(first.FechaduraInstalada, Is.False);
            await client.SynchronizeAsync(CancellationToken.None);
            await client.CompleteAsync(new EnrollmentCompletion(Guid.NewGuid(), "falhou", "duplicada", other), CancellationToken.None);
            await client.CompleteAsync(new EnrollmentCompletion(Guid.NewGuid(), "cadastrado", Readings: 4), CancellationToken.None);
            Assert.That(await client.UnpairAsync(CancellationToken.None), Is.True);
        }
        Assert.That(requests.Select(r => r.Action), Is.EqualTo(new[] { "sincronizar", "sincronizar", "concluir", "concluir", "desparear" }));
        Assert.That(requests[0].Data.EnumerateObject(), Is.Empty, "A primeira sincronização ainda não sabe o que a Central entende.");
        Assert.That(requests[1].Data.GetProperty("versao").GetString(), Is.EqualTo("1.4.0"));
        Assert.That(requests[1].Data.EnumerateObject().Count(), Is.EqualTo(1));
        Assert.That(requests[2].Data.GetProperty("duplicada_de").GetGuid(), Is.EqualTo(other));
        Assert.That(requests[2].Data.TryGetProperty("leituras", out _), Is.False);
        Assert.That(requests[3].Data.GetProperty("leituras").GetInt32(), Is.EqualTo(4));
        Assert.That(requests[3].Data.TryGetProperty("duplicada_de", out _), Is.False);
        Assert.That(requests[4].Data.EnumerateObject(), Is.Empty);
    }

    [Test]
    public async Task AFailedSynchronizationForgetsTheAnnouncedFeatures()
    {
        var fail = false;
        var requests = new List<string>();
        using var client = new CentralClient(Configuration, new Handler(async request =>
        {
            requests.Add(await request.Content!.ReadAsStringAsync());
            return fail ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"ok\":false,\"erro\":\"campo desconhecido\"}") } : Json(Now(NewSync));
        }), appVersion: "1.4.0");
        await client.SynchronizeAsync(CancellationToken.None);
        fail = true;
        Assert.ThrowsAsync<CentralException>(async () => await client.SynchronizeAsync(CancellationToken.None));
        fail = false;
        await client.SynchronizeAsync(CancellationToken.None);
        Assert.That(requests[1], Does.Contain("\"versao\":\"1.4.0\""));
        Assert.That(requests[2], Does.Not.Contain("versao"), "Depois de uma recusa, volta a mandar só o que a Central antiga aceita.");
    }

    [Test]
    public async Task EnrollmentCommandMayLastTenMinutesAndCarriesCompanyAndReadings()
    {
        var server = DateTimeOffset.UtcNow;
        var cadastro = Guid.NewGuid();
        var body = "{\"ok\":true,\"protocolo\":1,\"servidor_em\":\"" + server.ToString("O") + "\",\"revogado\":false,\"cadastros\":[\"" + cadastro + "\"],"
            + "\"comando\":{\"id\":\"" + Guid.NewGuid() + "\",\"tipo\":\"cadastrar\",\"cadastro_id\":\"" + cadastro + "\",\"nome\":\"Ana Souza\",\"empresa\":\"Inova Tecnologia\",\"leituras\":4,"
            + "\"expira_em\":\"" + server.AddMinutes(10).ToString("O") + "\",\"desconhecido\":true}}";
        using var client = new CentralClient(Configuration, new Handler(_ => Task.FromResult(Json(body))));
        var sync = await client.SynchronizeAsync(CancellationToken.None);
        Assert.That(sync.Comando!.Empresa, Is.EqualTo("Inova Tecnologia"));
        Assert.That(sync.Comando.RequiredReadings, Is.EqualTo(4));
        Assert.That(new EnrollmentCommand { Leituras = null }.RequiredReadings, Is.EqualTo(2));
        Assert.That(new EnrollmentCommand { Leituras = 1 }.RequiredReadings, Is.EqualTo(2));
        Assert.That(new EnrollmentCommand { Leituras = 50 }.RequiredReadings, Is.EqualTo(6));
    }

    [Test]
    public void EnrollmentCommandLongerThanTenMinutesIsRejected()
    {
        var server = DateTimeOffset.UtcNow;
        var cadastro = Guid.NewGuid();
        var body = "{\"ok\":true,\"protocolo\":1,\"servidor_em\":\"" + server.ToString("O") + "\",\"revogado\":false,\"cadastros\":[\"" + cadastro + "\"],"
            + "\"comando\":{\"id\":\"" + Guid.NewGuid() + "\",\"tipo\":\"cadastrar\",\"cadastro_id\":\"" + cadastro + "\",\"nome\":\"Ana\",\"expira_em\":\"" + server.AddMinutes(11).ToString("O") + "\"}}";
        using var client = new CentralClient(Configuration, new Handler(_ => Task.FromResult(Json(body))));
        Assert.ThrowsAsync<CentralException>(async () => await client.SynchronizeAsync(CancellationToken.None));
    }

    [Test]
    public async Task IdentificationReadsTheOptionalCardAndIgnoresUnknownOrInvalidFields()
    {
        const string card = "{\"ok\":true,\"autorizado\":true,\"motivo\":\"permitido\",\"nome\":\"Ana Souza\",\"porta_acionada\":false,\"novo\":1,"
            + "\"exibir\":{\"nome\":\"Ana Souza\",\"primeiro_nome\":\"Ana\",\"empresa\":\"Inova Tecnologia\",\"sala\":\"Milão\",\"codigo\":128,"
            + "\"validade\":\"2026-12-31\",\"validade_texto\":null,\"foto\":\"https://storage.googleapis.com/fotos/ana.jpg\",\"horario_texto\":\"segunda a sexta, 8h às 20h\",\"extra\":[1]}}";
        using (var client = new CentralClient(Configuration, new Handler(_ => Task.FromResult(Json(card)))))
        {
            var decision = await client.IdentifyAsync(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, CancellationToken.None);
            Assert.That(decision.Display, Is.EqualTo(new DisplayInfo
            {
                Nome = "Ana Souza", PrimeiroNome = "Ana", Empresa = "Inova Tecnologia", Sala = "Milão", Codigo = "128",
                Validade = "2026-12-31", Foto = "https://storage.googleapis.com/fotos/ana.jpg", HorarioTexto = "segunda a sexta, 8h às 20h"
            }));
        }
        foreach (var response in new[]
        {
            "{\"ok\":true,\"autorizado\":false,\"motivo\":\"horario\",\"nome\":\"Pessoa\",\"porta_acionada\":false}",
            "{\"ok\":true,\"autorizado\":false,\"motivo\":\"horario\",\"nome\":\"Pessoa\",\"porta_acionada\":false,\"exibir\":null}",
            "{\"ok\":true,\"autorizado\":false,\"motivo\":\"horario\",\"nome\":\"Pessoa\",\"porta_acionada\":false,\"exibir\":\"texto\"}",
            "{\"ok\":true,\"autorizado\":false,\"motivo\":\"horario\",\"nome\":\"Pessoa\",\"porta_acionada\":false,\"exibir\":{\"nome\":7,\"foto\":true}}"
        })
        {
            using var client = new CentralClient(Configuration, new Handler(_ => Task.FromResult(Json(response))));
            var decision = await client.IdentifyAsync(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, CancellationToken.None);
            Assert.That(decision.Display?.Foto, Is.Null);
            Assert.That(decision.Autorizado, Is.False);
        }
    }

    [Test]
    public async Task PairingReturnsTheReaderNameWhenTheCentralSendsIt()
    {
        const string body = "{\"ok\":true,\"protocolo\":1,\"token\":\"opaque-device-token-0123456789\",\"dispositivo_id\":\"10000000-0000-0000-0000-000000000001\",\"nome\":\"Recepção — FS80H\"}";
        using var client = new CentralClient(new AgentConfiguration { ProjectUrl = "https://project.supabase.co", PublicApiKey = "sb_publishable_test" }, new Handler(_ => Task.FromResult(Json(body))));
        var result = await client.PairAsync("codigo", CancellationToken.None);
        Assert.That(result.Nome, Is.EqualTo("Recepção — FS80H"));
        using var old = new CentralClient(new AgentConfiguration { ProjectUrl = "https://project.supabase.co", PublicApiKey = "sb_publishable_test" },
            new Handler(_ => Task.FromResult(Json(body.Replace(",\"nome\":\"Recepção — FS80H\"", "")))));
        Assert.That((await old.PairAsync("codigo", CancellationToken.None)).Nome, Is.Null);
    }

    [Test]
    public async Task RemovalBatchFitsTheEdges16KiBRequestLimit()
    {
        var bytes = 0;
        using var client = new CentralClient(Configuration, new Handler(async request =>
        {
            bytes = (await request.Content!.ReadAsByteArrayAsync()).Length;
            return Json("{\"ok\":true}");
        }));
        await client.ConfirmRemovalsAsync(Enumerable.Range(0, CentralClient.RemovalBatchSize).Select(_ => Guid.NewGuid()).ToArray(), false, CancellationToken.None);
        Assert.That(bytes, Is.LessThanOrEqualTo(16384));
    }

    [Test]
    public void RequestDeadlineIncludesAStalledResponseBody()
    {
        using var client = new CentralClient(Configuration, new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StalledStream())
        })), TimeSpan.FromMilliseconds(50));
        Assert.CatchAsync<OperationCanceledException>(async () => await client.SynchronizeAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)));
    }

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    [Test]
    public void ProtocolVersionMustMatchBeforeAnyLocalCapture()
    {
        using var client = new CentralClient(Configuration, new Handler(_ => Task.FromResult(Json("{\"ok\":true,\"protocolo\":2,\"revogado\":false,\"cadastros\":[],\"comando\":null,\"servidor_em\":\"2026-10-29T10:00:00Z\"}"))));
        Assert.ThrowsAsync<CentralException>(async () => await client.SynchronizeAsync(CancellationToken.None));
    }

    [Test]
    public void RedirectIsRejectedWithoutSendingSecondRequest()
    {
        var calls = 0;
        using var client = new CentralClient(Configuration, new Handler(_ =>
        {
            calls++;
            var result = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            result.Headers.Location = new Uri("https://untrusted.example/collect");
            return Task.FromResult(result);
        }));
        Assert.ThrowsAsync<CentralException>(async () => await client.SynchronizeAsync(CancellationToken.None));
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public async Task DiscoveryParsesPublicConfigAsJsonWithoutEvaluatingJavascript()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Json("window.CENTRAL_CONFIG = {\"url\":\"https://project.supabase.co\",\"chave\":\"sb_publishable_test\"}; throw new Error('never execute');"))));
        var config = await CentralDiscovery.DiscoverAsync("https://central.futureplacecoworking.com.br", http, CancellationToken.None);
        Assert.That(config.ProjectUrl, Is.EqualTo("https://project.supabase.co"));
        Assert.That(config.PublicApiKey, Is.EqualTo("sb_publishable_test"));
    }

    [TestCase("http://central.example")]
    [TestCase("https://127.0.0.1")]
    [TestCase("https://localhost")]
    [TestCase("https://user:password@central.example")]
    public void DiscoveryRejectsUnsafeCentralAddressesBeforeNetwork(string url)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ => { calls++; return Task.FromResult(Json("{}")); }));
        Assert.ThrowsAsync<CentralException>(async () => await CentralDiscovery.DiscoverAsync(url, http, CancellationToken.None));
        Assert.That(calls, Is.Zero);
    }

    [Test]
    public void DiscoveryRejectsPrivateProjectAndAdministrativeKeys()
    {
        var jwt = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"role\":\"service_role\"}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Json("window.CENTRAL_CONFIG={\"url\":\"https://project.supabase.co\",\"chave\":\"header." + jwt + ".signature\"};"))));
        Assert.ThrowsAsync<CentralException>(async () => await CentralDiscovery.DiscoverAsync("https://central.example", http, CancellationToken.None));
    }

    internal static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    internal sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}

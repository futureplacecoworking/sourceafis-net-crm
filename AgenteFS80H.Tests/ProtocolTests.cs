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
        await client.CompleteAsync(Guid.NewGuid(), "cadastrado", null, CancellationToken.None);
        using var json = JsonDocument.Parse(payload!);
        var data = json.RootElement.GetProperty("dados");
        Assert.That(data.GetProperty("resultado").GetString(), Is.EqualTo("cadastrado"));
        Assert.That(data.TryGetProperty("motivo", out _), Is.False);
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

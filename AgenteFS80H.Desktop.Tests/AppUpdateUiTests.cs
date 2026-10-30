using System.Diagnostics;
using System.Drawing.Imaging;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using Futureplace.AgenteFS80H;
using NUnit.Framework;

namespace AgenteFS80H.Desktop.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class AppUpdateUiTests
{
    [Test]
    public void ReceptionistCanPostponeAndManuallyRedisplayAnUpdateWithoutDownloadingOrChangingAccess()
    {
        using var server = new AvailableUpdateHandler();
        using var ui = new UiFixture(updates: new AppUpdateClient(new HttpClient(server)));
        ui.Snapshot(AgentStatus.Authorized, "Autorizado pela Central. Nenhuma porta foi acionada.");
        ui.Click("navigationSettings");
        ui.Click("checkUpdatesButton");
        PumpUntil(() => ui.Find<Control>("updateBanner").Visible);
        Assert.That(ui.Find<Control>("updateMessage").Text, Does.Contain("999.0.0"));
        Assert.That(ui.Find<Button>("installUpdateButton").Visible, Is.True);
        Assert.That(server.InstallerRequests, Is.Zero, "Consultar ou exibir a barra nunca baixa/executa instalador.");
        ui.Click("postponeUpdateButton");
        Assert.That(ui.Find<Control>("updateBanner").Visible, Is.False);
        ui.Click("checkUpdatesButton");
        PumpUntil(() => ui.Find<Control>("updateBanner").Visible);
        Assert.That(ui.Find<Control>("readingInstruction").Text, Does.Contain("Autorizado pela Central"));
        Assert.That(ui.Store.Writes, Is.Zero);
        Assert.That(server.InstallerRequests, Is.Zero);
    }

    [Test]
    public void BackgroundStartupFindsAnUpdateWithoutOpeningTheReceptionistWindow()
    {
        using var server = new AvailableUpdateHandler();
        using var ui = new UiFixture(updates: new AppUpdateClient(new HttpClient(server)));
        ui.Form.Hide();
        ui.Form.StartUpdateChecks();
        PumpUntil(() => server.Requests >= 2);
        Assert.That(ui.Form.Visible, Is.False);
        ui.Form.Show();
        UiFixture.Pump();
        Assert.That(ui.Find<Control>("updateBanner").Visible, Is.True);
        Assert.That(server.InstallerRequests, Is.Zero);
        Assert.That(ui.Store.Writes, Is.Zero);
    }

    [Test]
    public void FailedUpdateServicePreservesReadingAndAllowsRetry()
    {
        using var server = new AvailableUpdateHandler { Fail = true };
        using var ui = new UiFixture(updates: new AppUpdateClient(new HttpClient(server)));
        ui.Snapshot(AgentStatus.Ready, "Coloque o dedo cadastrado no leitor.");
        ui.Click("navigationSettings");
        ui.Click("checkUpdatesButton");
        PumpUntil(() => ui.Find<Control>("actionMessage").Text.Contains("Não foi possível consultar"));
        Assert.That(ui.Find<Control>("updateBanner").Visible, Is.False);
        Assert.That(ui.Find<Control>("readingInstruction").Text, Is.EqualTo("Coloque o dedo cadastrado no leitor."));
        server.Fail = false;
        ui.Click("checkUpdatesButton");
        PumpUntil(() => ui.Find<Control>("updateBanner").Visible);
        Assert.That(ui.Store.Writes, Is.Zero);
    }

    [Test]
    public void ShutdownCancelsThePendingUpdateRequestWithoutTouchingTheLocalStore()
    {
        using var server = new PendingUpdateHandler();
        using var ui = new UiFixture(updates: new AppUpdateClient(new HttpClient(server)));
        ui.Form.StartUpdateChecks();
        PumpUntil(() => server.Started);
        ui.Form.SetExiting();
        PumpUntil(() => server.Cancelled);
        Assert.That(ui.Find<Button>("installUpdateButton").Enabled, Is.False);
        Assert.That(ui.Find<Button>("postponeUpdateButton").Enabled, Is.False);
        Assert.That(ui.Store.Writes, Is.Zero);
    }

    [Test]
    public void UpdateButtonShowsDownloadFailureWithoutStartingAnUnverifiedInstaller()
    {
        using var server = new AvailableUpdateHandler { AllowInstallerDownload = true };
        using var ui = new UiFixture(updates: new AppUpdateClient(new HttpClient(server)));
        ui.Snapshot(AgentStatus.Ready, "Coloque o dedo cadastrado no leitor.");
        ui.Click("navigationSettings");
        ui.Click("checkUpdatesButton");
        PumpUntil(() => ui.Find<Control>("updateBanner").Visible);
        ui.Click("installUpdateButton");
        PumpUntil(() => ui.Find<Control>("updateMessage").Text.Contains("Não foi possível instalar")
            && ui.Find<Button>("installUpdateButton").Enabled);
        Assert.That(server.InstallerRequests, Is.EqualTo(1));
        Assert.That(ui.Find<Control>("updateProgress").Visible, Is.True);
        Assert.That(ui.Find<Button>("installUpdateButton").Enabled, Is.True);
        Assert.That(ui.Find<Control>("readingInstruction").Text, Is.EqualTo("Coloque o dedo cadastrado no leitor."));
        Assert.That(ui.Store.Writes, Is.Zero);
    }

    [Test]
    public void NativeUpdatePreviewUsesOnlySyntheticPublicReleaseMetadata()
    {
        using var server = new AvailableUpdateHandler();
        using var ui = new UiFixture(updates: new AppUpdateClient(new HttpClient(server)));
        ui.Click("navigationSettings");
        ui.Click("checkUpdatesButton");
        PumpUntil(() => ui.Find<Control>("updateBanner").Visible);
        var output = Environment.GetEnvironmentVariable("FS80H_UI_OUTPUT") ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "ui");
        Directory.CreateDirectory(output);
        var path = Path.Combine(output, "update-available.png");
        using var bitmap = new Bitmap(ui.Form.Width, ui.Form.Height);
        ui.Form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(path, ImageFormat.Png);
        TestContext.AddTestAttachment(path, "Barra de atualização; versão futura sintética, sem rede ou download real.");
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(3)) { UiFixture.Pump(); Thread.Sleep(10); }
        Assert.That(condition(), Is.True, "O estado da interface deve chegar sem bloquear o leitor.");
    }

    private sealed class AvailableUpdateHandler : HttpMessageHandler
    {
        private const string Base = "https://github.com/futureplacecoworking/sourceafis-net-crm/releases/download/central-biometrica-v999.0.0/";
        public bool Fail;
        public bool AllowInstallerDownload;
        public int Requests;
        public int InstallerRequests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Fail) throw new HttpRequestException("Synthetic update service unavailable.");
            if (request.RequestUri!.AbsolutePath.EndsWith("Setup.exe"))
            {
                InstallerRequests++;
                if (!AllowInstallerDownload) throw new AssertionException("A consulta não pode baixar instalador.");
                // Wrong hash is deliberate: exercising the real button must never launch a test payload.
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([77, 90, 1, 2, 3]) });
            }
            object data = request.RequestUri.AbsolutePath.EndsWith("update.json")
                ? new { version = "999.0.0", installerUrl = Base + "Central-Biometrica-Setup.exe", sha256 = new string('a', 64) }
                : new { tag_name = "central-biometrica-v999.0.0", draft = false, prerelease = false, assets = new[] { new { name = "update.json", browser_download_url = Base + "update.json" }, new { name = "Central-Biometrica-Setup.exe", browser_download_url = Base + "Central-Biometrica-Setup.exe" } } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json") });
        }
    }

    private sealed class PendingUpdateHandler : HttpMessageHandler
    {
        public volatile bool Started;
        public volatile bool Cancelled;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started = true;
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            finally { Cancelled = cancellationToken.IsCancellationRequested; }
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
    }
}

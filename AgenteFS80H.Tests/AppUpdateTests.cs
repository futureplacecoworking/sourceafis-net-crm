using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Futureplace.AgenteFS80H;
using NUnit.Framework;

namespace AgenteFS80H.Tests;

public class AppUpdateTests
{
    private const string Release = "https://api.github.com/repos/futureplacecoworking/sourceafis-net-crm/releases/latest";
    private const string Base = "https://github.com/futureplacecoworking/sourceafis-net-crm/releases/download/central-biometrica-v1.4.0/";
    private static readonly byte[] Installer = [77, 90, 1, 2, 3, 4, 5]; // Synthetic MZ payload; never executed.
    private static string Hash => Convert.ToHexString(SHA256.HashData(Installer)).ToLowerInvariant();

    [Test]
    public async Task PublishedNewerVersionUsesOnlyTheCanonicalInstaller()
    {
        using var fake = Server();
        using var client = new AppUpdateClient(new HttpClient(fake));
        var update = await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None);
        Assert.That(update?.Version, Is.EqualTo(new Version(1, 4, 0)));
        Assert.That(update?.InstallerUri.AbsoluteUri, Is.EqualTo(Base + "Central-Biometrica-Setup.exe"));
        Assert.That(update?.Sha256, Is.EqualTo(Hash));
    }

    [TestCase("1.4.0")]
    [TestCase("1.5.0")]
    public async Task CurrentOrOlderReleasesNeverOfferADowngrade(string installed)
    {
        using var fake = Server();
        using var client = new AppUpdateClient(new HttpClient(fake));
        Assert.That(await client.CheckAsync(Version.Parse(installed), CancellationToken.None), Is.Null);
        Assert.That(fake.Requests, Has.Count.EqualTo(1), "Não buscar manifesto/instalador de uma versão antiga.");
    }

    [TestCase("https://evil.example/setup.exe")]
    [TestCase("http://github.com/futureplacecoworking/sourceafis-net-crm/releases/download/central-biometrica-v1.4.0/Central-Biometrica-Setup.exe")]
    [TestCase("https://github.com@evil.example/setup.exe")]
    public void ManifestCannotRedirectTheInstallerToAnotherOrigin(string address)
    {
        using var fake = Server(installerUrl: address);
        using var client = new AppUpdateClient(new HttpClient(fake));
        Assert.ThrowsAsync<AppUpdateException>(async () => await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None));
    }

    [TestCase("1.4")]
    [TestCase("1.4.0.1")]
    [TestCase("1.4.0-beta")]
    public void InvalidManifestVersionsAreRejected(string version)
    {
        using var fake = Server(manifestVersion: version);
        using var client = new AppUpdateClient(new HttpClient(fake));
        Assert.ThrowsAsync<AppUpdateException>(async () => await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None));
    }

    [Test]
    public async Task VerifiedDownloadReportsProgressAndRechecksTheFileBeforeLaunch()
    {
        using var fake = Server();
        using var client = new AppUpdateClient(new HttpClient(fake));
        var update = (await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None))!;
        var directory = Path.Combine(Path.GetTempPath(), "central-update-test-" + Guid.NewGuid());
        try
        {
            var progress = new RecordingProgress();
            var path = await client.DownloadAsync(update, directory, progress, CancellationToken.None);
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(Installer));
            Assert.That(progress.Values.Last(), Is.EqualTo(100));
            await AppUpdateClient.VerifyInstallerAsync(path, update, CancellationToken.None);
            await File.WriteAllBytesAsync(path, [77, 90, 99]);
            Assert.ThrowsAsync<AppUpdateException>(async () => await AppUpdateClient.VerifyInstallerAsync(path, update, CancellationToken.None));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public async Task WrongHashNeverLeavesAnInstallerToExecute()
    {
        using var fake = Server(hash: new string('0', 64));
        using var client = new AppUpdateClient(new HttpClient(fake));
        var update = (await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None))!;
        var directory = Path.Combine(Path.GetTempPath(), "central-update-test-" + Guid.NewGuid());
        try
        {
            Assert.ThrowsAsync<AppUpdateException>(async () => await client.DownloadAsync(update, directory, null, CancellationToken.None));
            Assert.That(Directory.GetFiles(directory), Is.Empty);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public async Task OfficialGitHubAssetRedirectIsAllowedAndStillCheckedByHash()
    {
        using var fake = Server(redirect: "https://release-assets.githubusercontent.com/github-production-release-asset/1/setup?signature=synthetic", allowCdn: true);
        using var client = new AppUpdateClient(new HttpClient(fake));
        var update = (await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None))!;
        var directory = Path.Combine(Path.GetTempPath(), "central-update-test-" + Guid.NewGuid());
        try
        {
            var path = await client.DownloadAsync(update, directory, null, CancellationToken.None);
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(Installer));
            Assert.That(fake.Requests.Any(u => u.Host == "release-assets.githubusercontent.com"), Is.True);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestCase("bad")]
    [TestCase("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [TestCase("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void MalformedChecksumsAreRejectedBeforeOfferingAnUpdate(string hash)
    {
        using var fake = Server(hash: hash);
        using var client = new AppUpdateClient(new HttpClient(fake));
        Assert.ThrowsAsync<AppUpdateException>(async () => await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None));
    }

    [Test]
    public async Task InstallerCannotEscapeToAnUnapprovedRedirectHost()
    {
        using var fake = Server(redirect: "https://evil.example/setup.exe");
        using var client = new AppUpdateClient(new HttpClient(fake));
        var update = (await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None))!;
        var directory = Path.Combine(Path.GetTempPath(), "central-update-test-" + Guid.NewGuid());
        try
        {
            Assert.ThrowsAsync<AppUpdateException>(async () => await client.DownloadAsync(update, directory, null, CancellationToken.None));
            Assert.That(fake.Requests.Any(u => u.Host == "evil.example"), Is.False);
            Assert.That(Directory.GetFiles(directory), Is.Empty);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public async Task ExcessiveInstallerLengthIsRejectedBeforeReadingTheBody()
    {
        using var fake = Server(oversized: true);
        using var client = new AppUpdateClient(new HttpClient(fake));
        var update = (await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None))!;
        var directory = Path.Combine(Path.GetTempPath(), "central-update-test-" + Guid.NewGuid());
        try
        {
            Assert.ThrowsAsync<AppUpdateException>(async () => await client.DownloadAsync(update, directory, null, CancellationToken.None));
            Assert.That(Directory.GetFiles(directory), Is.Empty);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public async Task NoPublishedReleaseIsANormalUpToDateResult()
    {
        using var fake = new ServerHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new AppUpdateClient(new HttpClient(fake));
        Assert.That(await client.CheckAsync(new Version(1, 3, 0), CancellationToken.None), Is.Null);
    }

    private static ServerHandler Server(string? installerUrl = null, string? manifestVersion = null, string? hash = null, string? redirect = null, bool oversized = false, bool allowCdn = false) => new(uri =>
    {
        if (allowCdn && uri.Host == "release-assets.githubusercontent.com") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Installer) };
        if (uri.AbsoluteUri == Release)
            return Json(new { tag_name = "central-biometrica-v1.4.0", draft = false, prerelease = false, assets = new[] { new { name = "update.json", browser_download_url = Base + "update.json" }, new { name = "Central-Biometrica-Setup.exe", browser_download_url = Base + "Central-Biometrica-Setup.exe" } } });
        if (uri.AbsoluteUri == Base + "update.json") return Json(new { version = manifestVersion ?? "1.4.0", installerUrl = installerUrl ?? Base + "Central-Biometrica-Setup.exe", sha256 = hash ?? Hash });
        if (uri.AbsoluteUri != Base + "Central-Biometrica-Setup.exe") throw new AssertionException("Unexpected URL: " + uri);
        if (redirect is not null)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri(redirect);
            return response;
        }
        var result = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Installer) };
        if (oversized) result.Content.Headers.ContentLength = 128L * 1024 * 1024 + 1;
        return result;
    });

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private sealed class RecordingProgress : IProgress<int>
    {
        public List<int> Values { get; } = [];
        public void Report(int value) => Values.Add(value);
    }
    private sealed class ServerHandler(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request.RequestUri!));
        }
    }
}

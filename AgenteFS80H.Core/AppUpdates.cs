using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Futureplace.AgenteFS80H;

public sealed record AppUpdate(Version Version, Uri InstallerUri, string Sha256);
public sealed class AppUpdateException() : Exception("Não foi possível verificar a atualização oficial. Tente novamente mais tarde.");

/// <summary>Opt-in installer updates from the project's public GitHub releases. No device credentials are used.</summary>
public sealed class AppUpdateClient : IDisposable
{
    public const long MaximumInstallerBytes = 128L * 1024 * 1024;
    private const int MaximumMetadataBytes = 64 * 1024;
    private const string Repository = "https://github.com/futureplacecoworking/sourceafis-net-crm/releases/download/";
    private static readonly Uri Latest = new("https://api.github.com/repos/futureplacecoworking/sourceafis-net-crm/releases/latest");
    private readonly HttpClient http;

    public AppUpdateClient() : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan }) { }

    // The injected client must leave redirects to this class; production always uses the handler above.
    internal AppUpdateClient(HttpClient http) => this.http = http;

    public async Task<AppUpdate?> CheckAsync(Version installed, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var releaseResponse = await GetAsync(Latest, deadline.Token).ConfigureAwait(false);
        if (releaseResponse.StatusCode == HttpStatusCode.NotFound) return null;
        using var release = await ReadJsonAsync(releaseResponse, deadline.Token).ConfigureAwait(false);
        var root = release.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        const string prefix = "central-biometrica-v";
        if (!tag.StartsWith(prefix, StringComparison.Ordinal) || !StableVersion(tag[prefix.Length..], out var version)) throw new AppUpdateException();
        if (version <= installed) return null;
        var baseUrl = Repository + tag + "/";
        var manifestUri = Asset(root, "update.json", baseUrl + "update.json");
        var installerUri = Asset(root, "Central-Biometrica-Setup.exe", baseUrl + "Central-Biometrica-Setup.exe");
        using var manifestResponse = await GetAsync(manifestUri, deadline.Token).ConfigureAwait(false);
        using var manifest = await ReadJsonAsync(manifestResponse, deadline.Token).ConfigureAwait(false);
        var value = manifest.RootElement;
        if (!StableVersion(value.GetProperty("version").GetString(), out var manifestVersion)
            || manifestVersion != version || value.GetProperty("installerUrl").GetString() != installerUri.AbsoluteUri)
            throw new AppUpdateException();
        var hash = value.GetProperty("sha256").GetString() ?? "";
        var update = new AppUpdate(version, installerUri, hash.ToLowerInvariant());
        Validate(update);
        return update;
    }

    public async Task<string> DownloadAsync(AppUpdate update, string directory, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        Validate(update);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"Central-Biometrica-{update.Version}-{Guid.NewGuid():N}-Setup.exe");
        var partial = path + ".download";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            using var response = await GetAsync(update.InstallerUri, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > MaximumInstallerBytes) throw new AppUpdateException();
            await using (var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long written = 0;
                int count;
                progress?.Report(0);
                while ((count = await input.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) != 0)
                {
                    written += count;
                    if (written > MaximumInstallerBytes) throw new AppUpdateException();
                    await output.WriteAsync(buffer.AsMemory(0, count), deadline.Token).ConfigureAwait(false);
                    if (response.Content.Headers.ContentLength is long length && length > 0)
                        progress?.Report((int)Math.Min(99, written * 100 / length));
                }
            }
            await VerifyInstallerAsync(partial, update, deadline.Token).ConfigureAwait(false);
            File.Move(partial, path);
            progress?.Report(100);
            return path;
        }
        finally
        {
            try { if (File.Exists(partial)) File.Delete(partial); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static async Task VerifyInstallerAsync(string path, AppUpdate update, CancellationToken cancellationToken)
    {
        Validate(update);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (stream.Length is < 2 or > MaximumInstallerBytes) throw new AppUpdateException();
        var signature = new byte[2];
        await stream.ReadExactlyAsync(signature, cancellationToken).ConfigureAwait(false);
        if (signature[0] != 'M' || signature[1] != 'Z') throw new AppUpdateException();
        stream.Position = 0;
        var actual = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(update.Sha256))) throw new AppUpdateException();
    }

    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            if (!AllowedTransport(uri)) throw new AppUpdateException();
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Central-Biometrica-Updater/1.0");
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null || redirects == 5) throw new AppUpdateException();
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
        }
        throw new AppUpdateException();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > MaximumMetadataBytes) throw new AppUpdateException();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var bytes = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + count > MaximumMetadataBytes) throw new AppUpdateException();
            buffer.Write(bytes, 0, count);
        }
        try { return JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 12 }); }
        catch (JsonException) { throw new AppUpdateException(); }
    }

    private static Uri Asset(JsonElement release, string name, string expected)
    {
        var matches = release.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == name).ToArray();
        if (matches.Length != 1 || matches[0].GetProperty("browser_download_url").GetString() != expected) throw new AppUpdateException();
        return new Uri(expected);
    }

    private static bool StableVersion(string? value, out Version version)
    {
        version = null!;
        if (value is null || value.Split('.').Length != 3 || value.Any(c => c != '.' && c is not (>= '0' and <= '9'))
            || !Version.TryParse(value, out var parsed) || parsed.Build < 0 || parsed.Revision != -1 || parsed.ToString() != value) return false;
        version = parsed;
        return true;
    }

    private static void Validate(AppUpdate update)
    {
        if (!StableVersion(update.Version.ToString(), out _) || update.InstallerUri.AbsoluteUri != Repository + "central-biometrica-v" + update.Version + "/Central-Biometrica-Setup.exe"
            || update.Sha256.Length != 64 || update.Sha256.Any(c => !Uri.IsHexDigit(c))) throw new AppUpdateException();
    }

    private static bool AllowedTransport(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo)
        && uri.Host is "api.github.com" or "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";

    public void Dispose() => http.Dispose();
}

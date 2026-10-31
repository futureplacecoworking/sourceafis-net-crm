using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Futureplace.AgenteFS80H.Desktop;

/// <summary>Página da tela do cliente dentro da janela. Os testes usam uma implementação falsa, sem WebView2.</summary>
internal interface IClientWebHost : IDisposable
{
    Control View { get; }
    /// <summary>Fica pronto ou lança <see cref="ClientWebUnavailableException"/>.</summary>
    Task InitializeAsync();
    /// <summary>Entrega o estado (JSON) à página. Antes de a página abrir, guarda o último para enviar depois.</summary>
    void Show(string json);
    /// <summary>O navegador interno parou depois de aberto: a janela volta para a tela simples.</summary>
    event Action? Failed;
}

internal sealed class ClientWebUnavailableException(bool runtimeMissing) : Exception("A tela nova do cliente não pôde ser aberta.")
{
    public bool RuntimeMissing { get; } = runtimeMissing;
}

/// <summary>
/// Microsoft Edge WebView2 mostrando ClientPage/index.html da pasta do aplicativo, por um endereço virtual.
/// Sem internet para a página: só a foto do cadastro (storage.googleapis.com) pode ser baixada pela própria página.
/// </summary>
internal sealed class WebView2ClientHost : IClientWebHost
{
    public const string HostName = "tela-cliente.central.example";
    private static readonly string PageAddress = $"https://{HostName}/index.html";
    private readonly WebView2 view = new()
    {
        Name = "clientWebView",
        Dock = DockStyle.Fill,
        DefaultBackgroundColor = Color.Black,
        AccessibleName = "Tela do cliente",
        TabStop = false,
        // Ninguém digita nem clica na tela do cliente; Esc e F11 continuam com a janela.
        Enabled = false
    };
    private string? pending;
    private bool pageReady;
    private bool disposed;

    public Control View => view;
    public event Action? Failed;

    public async Task InitializeAsync()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "ClientPage");
        if (!File.Exists(Path.Combine(folder, "index.html"))) throw new ClientWebUnavailableException(false);
        try
        {
            _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
            // Os dados do navegador ficam fora da pasta de digitais e fora da pasta do programa.
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Futureplace", "AgenteFS80H", "WebView2");
            Directory.CreateDirectory(data);
            var environment = await CoreWebView2Environment.CreateAsync(null, data);
            await view.EnsureCoreWebView2Async(environment);
            var core = view.CoreWebView2 ?? throw new ClientWebUnavailableException(false);
            var settings = core.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDefaultScriptDialogsEnabled = false;
            settings.AreHostObjectsAllowed = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsPinchZoomEnabled = false;
            settings.IsSwipeNavigationEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsBuiltInErrorPageEnabled = false;
            settings.IsScriptEnabled = true;
            settings.IsWebMessageEnabled = true;
            core.SetVirtualHostNameToFolderMapping(HostName, folder, CoreWebView2HostResourceAccessKind.Deny);
            core.NavigationStarting += (_, e) => { if (!IsOwnPage(e.Uri)) e.Cancel = true; };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.WebMessageReceived += (_, e) =>
            {
                string? message;
                try { message = e.TryGetWebMessageAsString(); }
                catch (ArgumentException) { return; }
                if (message != "pronto") return;
                pageReady = true;
                Flush();
            };
            core.ProcessFailed += (_, e) =>
            {
                pageReady = false;
                if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited) { Failed?.Invoke(); return; }
                if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                {
                    try { core.Reload(); }
                    catch (Exception ex) when (ex is InvalidOperationException or COMException) { Failed?.Invoke(); }
                }
            };
            core.Navigate(PageAddress);
        }
        catch (WebView2RuntimeNotFoundException) { throw new ClientWebUnavailableException(true); }
        catch (ClientWebUnavailableException) { throw; }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException
            or BadImageFormatException or DllNotFoundException or EntryPointNotFoundException or NotSupportedException)
        {
            throw new ClientWebUnavailableException(false);
        }
    }

    public void Show(string json)
    {
        pending = json;
        Flush();
    }

    private void Flush()
    {
        if (disposed || !pageReady || pending is null || view.CoreWebView2 is not { } core) return;
        try { core.PostWebMessageAsJson(pending); }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or ArgumentException) { pageReady = false; }
    }

    private static bool IsOwnPage(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, HostName, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        view.Dispose();
    }
}

using System.Security.Cryptography;

namespace Futureplace.AgenteFS80H;

public sealed class AgentRuntime : IDisposable
{
    private readonly ILocalStore store;
    private readonly Func<AgentConfiguration, ICentralClient> clientFactory;
    private readonly Func<string, IFingerprintScanner> scannerFactory;
    private readonly IFingerprintRecognizer recognizer;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private AgentState state;
    private ICentralClient? client;
    private IFingerprintScanner? scanner;
    private CancellationTokenSource? cycleCancellation;
    private bool localDataFailed;
    private bool mustLift = true;
    private bool disposed;
    private DateTimeOffset keepResultUntil;
    public AgentConfiguration Configuration => state.Configuration;
    public AgentSnapshot Snapshot { get; private set; } = new(AgentStatus.Unconfigured, "Pareie este computador na Central e selecione a DLL oficial de captura.");
    public event Action<AgentSnapshot>? Changed;

    public static AgentRuntime CreateDefault() => new(
        new ProtectedLocalStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Futureplace", "AgenteFS80H", "data")),
        config => new CentralClient(config), path => new FutronicScanner(path), new SourceAfisRecognizer(), TimeProvider.System);

    internal AgentRuntime(ILocalStore store, Func<AgentConfiguration, ICentralClient> clientFactory, Func<string, IFingerprintScanner> scannerFactory, IFingerprintRecognizer recognizer, TimeProvider clock)
    {
        this.store = store;
        this.clientFactory = clientFactory;
        this.scannerFactory = scannerFactory;
        this.recognizer = recognizer;
        this.clock = clock;
        try
        {
            state = store.LoadState();
            if (state.Configuration is null || state.PendingRemovals is null) throw new LocalDataException();
        }
        catch (LocalDataException)
        {
            state = new();
            localDataFailed = true;
            SetStatus(AgentStatus.Error, new LocalDataException().Message);
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromMilliseconds(600);
            try { await RunCycleAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (OperationCanceledException)
            {
                SetStatus(AgentStatus.Offline, "Operação interrompida ou sem resposta da Central. Nenhum acesso foi autorizado.");
                delay = TimeSpan.FromSeconds(3);
            }
            catch (LocalDataException)
            {
                SetStatus(AgentStatus.Error, new LocalDataException().Message);
                delay = TimeSpan.FromSeconds(3);
            }
            catch (CaptureException)
            {
                ResetScanner();
                SetStatus(AgentStatus.Error, "Confira o cabo USB, o driver e a DLL oficial x64 do FS80H. Acesso não autorizado.");
                delay = TimeSpan.FromSeconds(3);
            }
            catch (Exception ex) when (ex is CentralException or HttpRequestException or TaskCanceledException)
            {
                SetStatus(AgentStatus.Offline, ex is CentralException safe ? safe.Message : "Sem conexão com a Central. Nenhum acesso será autorizado offline.");
                delay = TimeSpan.FromSeconds(3);
            }
            catch
            {
                ResetScanner();
                SetStatus(AgentStatus.Error, "Não foi possível concluir a leitura. Confira as configurações. Nenhum acesso foi autorizado.");
                delay = TimeSpan.FromSeconds(3);
            }
            try { await Task.Delay(delay, clock, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cycleCancellation = operation;
            if (localDataFailed) throw new LocalDataException();
            if (!Configuration.IsPaired)
            {
                SetStatus(AgentStatus.Unconfigured, "Gere um código na área Digitais da Central para parear este computador.");
                return;
            }
            client ??= clientFactory(Configuration);
            var sync = await SynchronizeAsync(operation.Token).ConfigureAwait(false);
            if (sync.Revogado) return;
            if (sync.Comando is { } command)
            {
                await EnrollAsync(command, operation.Token).ConfigureAwait(false);
                return;
            }
            if (string.IsNullOrWhiteSpace(Configuration.ScannerDllPath))
            {
                SetStatus(AgentStatus.Unconfigured, "Selecione a DLL oficial x64 e instale o driver Futronic neste computador.");
                return;
            }
            scanner ??= scannerFactory(Configuration.ScannerDllPath);
            if (mustLift)
            {
                if (scanner.IsFingerPresent())
                {
                    SetStatus(AgentStatus.Ready, "Retire o dedo do leitor antes de começar uma nova leitura.");
                    return;
                }
                mustLift = false;
            }
            if (!scanner.IsFingerPresent())
            {
                if (clock.GetUtcNow() >= keepResultUntil) SetStatus(AgentStatus.Ready, "Coloque o dedo cadastrado no leitor. A decisão depende da Central conectada.");
                return;
            }
            mustLift = true;
            byte[]? probe = null;
            try
            {
                var captured = await CaptureAsync(null, operation.Token).ConfigureAwait(false);
                probe = captured.Template;
                await GuardAsync(null, operation.Token).ConfigureAwait(false);
                var templates = store.LoadTemplates();
                Guid? match;
                try { match = MatchingPolicy.Select(templates.Select(t => (t.CadastroId, recognizer.Score(probe, t.Template)))); }
                finally { ClearTemplates(templates); }
                if (match is null)
                {
                    SetResult(AgentStatus.Denied, "Digital não reconhecida ou correspondência ambígua. Nenhum acesso foi autorizado.");
                    return;
                }
                // Nunca guardar/repetir esta chamada após falha: só uma captura recente gera nova operação.
                if (clock.GetUtcNow() - captured.CapturedAt > TimeSpan.FromSeconds(20))
                {
                    SetResult(AgentStatus.Denied, "A confirmação demorou demais. Faça uma nova leitura para consultar a Central.");
                    return;
                }
                var decision = await client.IdentifyAsync(Guid.NewGuid(), match.Value, captured.CapturedAt, operation.Token).ConfigureAwait(false);
                SetResult(decision.Autorizado ? AgentStatus.Authorized : AgentStatus.Denied,
                    decision.Autorizado ? "Autorizado pela Central. Nenhuma porta foi acionada." : "A Central não autorizou o acesso. Confira o cadastro e o horário na Central.");
            }
            catch (CaptureException)
            {
                SetResult(AgentStatus.Denied, "As duas leituras não confirmaram a digital. Retire o dedo e tente novamente.");
            }
            finally { if (probe is not null) CryptographicOperations.ZeroMemory(probe); }
        }
        finally { cycleCancellation = null; gate.Release(); }
    }

    private async Task<SyncResponse> SynchronizeAsync(CancellationToken token)
    {
        var sync = await client!.SynchronizeAsync(token).ConfigureAwait(false);
        if (sync.Revogado)
        {
            if (!state.DeviceRevoked)
            {
                state = state with { DeviceRevoked = true, TotalRemovalPending = true };
                store.SaveState(state);
            }
            // Remover sem decifrar: até um template corrompido precisa sair quando o leitor é revogado.
            store.DeleteAllTemplates();
            ResetScanner();
            SetStatus(AgentStatus.Revoked, "Leitor bloqueado pela Central. As digitais locais foram removidas.");
            if (state.TotalRemovalPending)
            {
                await client.ConfirmRemovalsAsync([], all: true, token).ConfigureAwait(false);
                state = state with { TotalRemovalPending = false, PendingRemovals = [] };
                store.SaveState(state);
            }
            return sync;
        }
        if (state.DeviceRevoked) throw new CentralException("Este pareamento foi revogado. Gere outro código na Central para voltar a usar este computador.");
        if (sync.ServidorEm is null || (clock.GetUtcNow() - sync.ServidorEm.Value).Duration() > TimeSpan.FromMinutes(2))
            throw new CentralException("Ajuste a data e a hora do Windows para sincronizar com a Central. Acesso bloqueado.");
        var removals = store.ListTemplateIds().Except(sync.Cadastros).Concat(state.PendingRemovals).Distinct().ToList();
        if (removals.Count > 0)
        {
            // Primeiro tornar a remoção pendente durável. Depois apagar; só então confirmar no servidor.
            state = state with { PendingRemovals = removals };
            store.SaveState(state);
            foreach (var id in removals) store.DeleteTemplate(id);
            foreach (var batch in removals.Chunk(CentralClient.RemovalBatchSize))
            {
                await client.ConfirmRemovalsAsync(batch, all: false, token).ConfigureAwait(false);
                state = state with { PendingRemovals = state.PendingRemovals.Except(batch).ToList() };
                store.SaveState(state);
            }
        }
        return sync;
    }

    private async Task GuardAsync(EnrollmentCommand? expected, CancellationToken token)
    {
        var sync = await SynchronizeAsync(token).ConfigureAwait(false);
        if (sync.Revogado) throw new CentralException("Leitor revogado pela Central. Acesso bloqueado.");
        if (expected is null)
        {
            if (sync.Comando is not null) throw new CentralException("Há um novo cadastro aguardando na Central. Inicie uma nova leitura.");
        }
        else
        {
            if (clock.GetUtcNow() >= expected.ExpiraEm) throw new CaptureException("expirado");
            if (sync.Comando?.Id != expected.Id || !sync.Cadastros.Contains(expected.CadastroId))
                throw new CentralException("O cadastro foi cancelado ou expirou na Central. Nenhuma digital será gravada.");
        }
    }

    private sealed record CapturedProbe(byte[] Template, DateTimeOffset CapturedAt);

    private async Task<CapturedProbe> CaptureAsync(EnrollmentCommand? command, CancellationToken token)
    {
        scanner ??= scannerFactory(Configuration.ScannerDllPath);
        var capture = new CaptureCoordinator(scanner, recognizer, clock);
        capture.Instruction += instruction => SetStatus(AgentStatus.Capturing, instruction);
        var template = await capture.CapturePairAsync(ct => GuardAsync(command, ct), token).ConfigureAwait(false);
        return new CapturedProbe(template, capture.CapturedAt);
    }

    private async Task EnrollAsync(EnrollmentCommand command, CancellationToken token)
    {
        if (clock.GetUtcNow() >= command.ExpiraEm)
        {
            await client!.CompleteAsync(command.Id, "falhou", "expirado", token).ConfigureAwait(false);
            return;
        }
        var existing = store.LoadTemplates();
        try
        {
            if (existing.Any(t => t.CadastroId == command.CadastroId && t.ComandoId == command.Id))
            {
                await client!.CompleteAsync(command.Id, "cadastrado", null, token).ConfigureAwait(false);
                SetResult(AgentStatus.Ready, "Cadastro confirmado na Central. O acesso precisa ser habilitado pela administração.");
                return;
            }
        }
        finally { ClearTemplates(existing); }
        if (string.IsNullOrWhiteSpace(Configuration.ScannerDllPath))
        {
            SetStatus(AgentStatus.Unconfigured, "Há um cadastro aguardando. Selecione a DLL oficial x64 para iniciar a captura.");
            return;
        }
        byte[]? template = null;
        mustLift = true;
        try
        {
            scanner ??= scannerFactory(Configuration.ScannerDllPath);
            if (scanner.IsFingerPresent())
            {
                SetStatus(AgentStatus.Capturing, "Retire o dedo do leitor antes de iniciar o cadastro.");
                return;
            }
            template = (await CaptureAsync(command, token).ConfigureAwait(false)).Template;
            await GuardAsync(command, token).ConfigureAwait(false);
            existing = store.LoadTemplates();
            try
            {
                if (existing.Any(t => recognizer.Score(template, t.Template) >= MatchingPolicy.Threshold))
                    throw new CaptureException("duplicada");
            }
            finally { ClearTemplates(existing); }
            // A gravação local precede concluir. O mesmo comando pode ser retomado sem coletar outra digital.
            store.SaveTemplate(new StoredTemplate(command.CadastroId, command.Id, template));
            await client!.CompleteAsync(command.Id, "cadastrado", null, token).ConfigureAwait(false);
            SetResult(AgentStatus.Ready, "Digital cadastrada na Central. O cadastro ainda não concede autorização de acesso.");
        }
        catch (CaptureException ex)
        {
            await client!.CompleteAsync(command.Id, "falhou", ex.Reason, token).ConfigureAwait(false);
            SetResult(AgentStatus.Denied, ex.Reason == "duplicada" ? "Esta digital já está cadastrada neste leitor. Confira o cartão na Central." : "Cadastro não concluído. Gere uma nova operação na Central e tente novamente.");
        }
        finally { if (template is not null) CryptographicOperations.ZeroMemory(template); }
    }

    public async Task PairAsync(string centralUrl, string code, CancellationToken cancellationToken)
    {
        CancelCycle();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (localDataFailed) throw new LocalDataException();
            SetStatus(AgentStatus.Connecting, "Validando o código com a Central.");
            using var http = CentralDiscovery.CreateHttpClient();
            var discovered = await CentralDiscovery.DiscoverAsync(centralUrl, http, cancellationToken).ConfigureAwait(false);
            using var pairing = new CentralClient(discovered);
            var response = await pairing.PairAsync(code, cancellationToken).ConfigureAwait(false);
            var config = discovered with { DeviceId = response.DispositivoId, DeviceToken = response.Token, ScannerDllPath = Configuration.ScannerDllPath };
            ReplacePairing(config);
            SetStatus(AgentStatus.Connecting, "Pareamento salvo. Aguardando sincronização com a Central.");
        }
        finally { gate.Release(); }
    }

    internal void ReplacePairing(AgentConfiguration configuration)
    {
        // O token novo já foi consumido no servidor. Após esse ponto nunca retomar o cliente antigo.
        ResetConnections();
        localDataFailed = true;
        state = new AgentState { Configuration = new AgentConfiguration { CentralUrl = configuration.CentralUrl, ScannerDllPath = configuration.ScannerDllPath } };
        try
        {
            store.Clear();
            var replacement = new AgentState { Configuration = configuration };
            store.SaveState(replacement);
            state = replacement;
            localDataFailed = false;
        }
        catch
        {
            SetStatus(AgentStatus.Error, "Não foi possível guardar o pareamento. Limpe os dados locais e revogue a tentativa na Central antes de gerar outro código.");
            throw new LocalDataException();
        }
    }

    public async Task ConfigureScannerAsync(string path, CancellationToken cancellationToken)
    {
        FutronicScanner.ValidateLibrary(path);
        CancelCycle();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (localDataFailed) throw new LocalDataException();
            var updated = state with { Configuration = Configuration with { ScannerDllPath = Path.GetFullPath(path) } };
            store.SaveState(updated);
            state = updated;
            ResetScanner();
            SetStatus(AgentStatus.Connecting, "DLL selecionada. Verificando o leitor e a conexão com a Central.");
        }
        finally { gate.Release(); }
    }

    public async Task ResetPairingAsync(CancellationToken cancellationToken)
    {
        CancelCycle();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var config = new AgentConfiguration { CentralUrl = Configuration.CentralUrl, ScannerDllPath = Configuration.ScannerDllPath };
            ResetConnections();
            localDataFailed = true;
            state = new AgentState { Configuration = config };
            store.Clear();
            store.SaveState(state);
            localDataFailed = false;
            SetStatus(AgentStatus.Unconfigured, "Pareamento e digitais locais removidos. Revogue também o leitor antigo na Central.");
        }
        finally { gate.Release(); }
    }

    private void CancelCycle()
    {
        try { cycleCancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }
    private void SetResult(AgentStatus status, string message)
    {
        keepResultUntil = clock.GetUtcNow().AddSeconds(5);
        SetStatus(status, message);
    }
    private void SetStatus(AgentStatus status, string message)
    {
        Snapshot = new(status, message);
        Changed?.Invoke(Snapshot);
    }
    private static void ClearTemplates(IEnumerable<StoredTemplate> templates)
    {
        foreach (var template in templates) CryptographicOperations.ZeroMemory(template.Template);
    }
    private void ResetScanner() { scanner?.Dispose(); scanner = null; mustLift = true; }
    private void ResetConnections() { client?.Dispose(); client = null; ResetScanner(); }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        CancelCycle();
        ResetConnections();
        gate.Dispose();
    }
}

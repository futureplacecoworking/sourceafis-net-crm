using System.Security.Cryptography;

namespace Futureplace.AgenteFS80H;

/// <summary>Como ficou o aviso à Central ao desparear este computador.</summary>
public enum UnpairNotice
{
    /// <summary>Este computador não estava pareado.</summary>
    NotPaired,
    /// <summary>A Central confirmou: o leitor antigo foi desligado lá também.</summary>
    Confirmed,
    /// <summary>Sem resposta ou Central antiga: o leitor antigo precisa ser revogado à mão na Central.</summary>
    Unavailable
}

public sealed record PairingResult(string DeviceName, UnpairNotice Previous);

public sealed class AgentRuntime : IDisposable
{
    private static readonly TimeSpan AuthorizedDisplay = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan ResultDisplay = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan MaximumCaptureAge = TimeSpan.FromSeconds(20);
    // Durante o cadastro, uma queda curta da rede não derruba as leituras já feitas.
    private static readonly TimeSpan EnrollmentNetworkTolerance = TimeSpan.FromSeconds(20);
    private readonly ILocalStore store;
    private readonly Func<AgentConfiguration, ICentralClient> clientFactory;
    private readonly Func<string, IFingerprintScanner> scannerFactory;
    private readonly IFingerprintRecognizer recognizer;
    private readonly TimeProvider clock;
    private readonly TimeSpan? pollInterval;
    private readonly string? appVersion;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object stateLock = new();
    private AgentState state;
    private ICentralClient? client;
    private IFingerprintScanner? scanner;
    private CancellationTokenSource? cycleCancellation;
    private bool localDataFailed;
    private bool mustLift = true;
    private bool disposed;
    private bool lockInstalled;
    private DateTimeOffset keepResultUntil;
    private DateTimeOffset? lastContact;
    private EnrollmentCompletion? pendingFailure;
    public AgentConfiguration Configuration => state.Configuration;
    public AgentSnapshot Snapshot { get; private set; } = new(AgentStatus.Unconfigured, "Pareie este computador na Central e selecione a DLL oficial de captura.");
    public event Action<AgentSnapshot>? Changed;
    /// <summary>Resultado de uma leitura ou de um cadastro, para o som curto do leitor.</summary>
    public event Action<AgentSignal>? Signaled;

    public static AgentRuntime CreateDefault(string? appVersion = null) => new(
        new ProtectedLocalStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Futureplace", "AgenteFS80H", "data")),
        config => new CentralClient(config, appVersion: appVersion), path => new FutronicScanner(path), new SourceAfisRecognizer(), TimeProvider.System,
        appVersion: appVersion);

    internal AgentRuntime(ILocalStore store, Func<AgentConfiguration, ICentralClient> clientFactory, Func<string, IFingerprintScanner> scannerFactory, IFingerprintRecognizer recognizer, TimeProvider clock,
        TimeSpan? pollInterval = null, string? appVersion = null)
    {
        this.store = store;
        this.clientFactory = clientFactory;
        this.scannerFactory = scannerFactory;
        this.recognizer = recognizer;
        this.clock = clock;
        this.pollInterval = pollInterval;
        this.appVersion = appVersion;
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
                SetStatus(AgentStatus.Unconfigured, "Gere um código em Digitais › Central biométrica na Central para parear este computador.");
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
            pendingFailure = null;
            if (string.IsNullOrWhiteSpace(Configuration.ScannerDllPath))
            {
                SetStatus(AgentStatus.Unconfigured, "Selecione a DLL oficial x64 e instale o driver Futronic neste computador.");
                return;
            }
            scanner ??= scannerFactory(Configuration.ScannerDllPath);
            // O resultado anterior continua na tela pelo tempo combinado, mesmo com o dedo ainda no leitor.
            var showingResult = clock.GetUtcNow() < keepResultUntil;
            if (mustLift)
            {
                if (scanner.IsFingerPresent())
                {
                    if (!showingResult) SetStatus(AgentStatus.Ready, "Retire o dedo do leitor antes de começar uma nova leitura.", ReaderHint.LiftFinger);
                    return;
                }
                mustLift = false;
            }
            if (!scanner.IsFingerPresent())
            {
                if (!showingResult) SetStatus(AgentStatus.Ready, "Coloque o dedo cadastrado no leitor. A decisão depende da Central conectada.", ReaderHint.PlaceFinger);
                return;
            }
            mustLift = true;
            await IdentifyAsync(operation.Token).ConfigureAwait(false);
        }
        finally { cycleCancellation = null; gate.Release(); }
    }

    private async Task IdentifyAsync(CancellationToken token)
    {
        var capture = new CaptureCoordinator(scanner!, recognizer, clock);
        ReportIdentification(0, "Lendo a digital. Peça para manter o dedo parado.");
        byte[]? probe = null;
        try
        {
            try { probe = capture.ReadTemplate(); }
            catch (CaptureException)
            {
                // Leitura ruim não vira recusa da Central: pede para tentar de novo.
                FinishIdentification(AgentStatus.Denied, new IdentificationProgress { Completed = 0, FailedStep = 1, Result = IdentificationResult.BadReading, LockInstalled = lockInstalled },
                    "Não deu para ler a digital. Levante o dedo e coloque de novo, sem mexer. Nenhum acesso foi autorizado.", ResultDisplay);
                return;
            }
            var capturedAt = capture.CapturedAt;
            ReportIdentification(1, "Digital lida. Reconhecendo a pessoa neste computador.");
            var sync = await SynchronizeAsync(token).ConfigureAwait(false);
            if (sync.Revogado) throw new CentralException("Leitor revogado pela Central. Acesso bloqueado.");
            if (sync.Comando is not null)
            {
                // O cadastro pedido na Central vem antes: a leitura é descartada sem consulta.
                SetStatus(AgentStatus.Ready, "Há um cadastro de digital aguardando. Esta leitura foi descartada.");
                return;
            }
            var templates = store.LoadTemplates();
            Guid? match;
            try
            {
                var matcher = recognizer.Matcher(probe);
                match = MatchingPolicy.Select(templates.SelectMany(stored => stored.AllTemplates().Select(sample => (stored.CadastroId, matcher.Score(sample)))).ToArray());
            }
            finally { ClearTemplates(templates); }
            if (match is null)
            {
                FinishIdentification(AgentStatus.Denied, new IdentificationProgress { Completed = 1, FailedStep = 2, Result = IdentificationResult.NotRecognized, LockInstalled = lockInstalled },
                    "Digital não reconhecida ou correspondência ambígua. Nenhum acesso foi autorizado.", ResultDisplay);
                return;
            }
            // Nunca guardar/repetir esta chamada após falha: só uma captura recente gera nova operação.
            if (clock.GetUtcNow() - capturedAt > MaximumCaptureAge)
            {
                FinishIdentification(AgentStatus.Denied, new IdentificationProgress { Completed = 2, FailedStep = 3, Result = IdentificationResult.Denied, Motivo = "demorou", LockInstalled = lockInstalled },
                    "A confirmação demorou demais. Faça uma nova leitura para consultar a Central.", ResultDisplay);
                return;
            }
            ReportIdentification(2, "Pessoa reconhecida. Consultando a Central.");
            var decision = await client!.IdentifyAsync(Guid.NewGuid(), match.Value, capturedAt, token).ConfigureAwait(false);
            lastContact = clock.GetUtcNow();
            var progress = new IdentificationProgress
            {
                Completed = decision.Autorizado ? IdentificationProgress.Steps : 2,
                FailedStep = decision.Autorizado ? null : 3,
                Result = decision.Autorizado ? IdentificationResult.Authorized : IdentificationResult.Denied,
                Motivo = decision.Motivo,
                Name = decision.Nome,
                Display = decision.Display,
                LockInstalled = lockInstalled
            };
            FinishIdentification(decision.Autorizado ? AgentStatus.Authorized : AgentStatus.Denied, progress,
                decision.Autorizado ? "Autorizado pela Central. Nenhuma porta foi acionada." : "A Central não autorizou o acesso. Confira o cadastro e o horário na Central.",
                decision.Autorizado ? AuthorizedDisplay : ResultDisplay);
        }
        finally { if (probe is not null) CryptographicOperations.ZeroMemory(probe); }
    }

    private void ReportIdentification(int completed, string message) =>
        SetStatus(AgentStatus.Capturing, message, identification: new IdentificationProgress { Completed = completed, Result = IdentificationResult.InProgress, LockInstalled = lockInstalled });

    private void FinishIdentification(AgentStatus status, IdentificationProgress progress, string message, TimeSpan display) =>
        SetResult(status, message, display, progress.Result == IdentificationResult.Authorized ? AgentSignal.Positive : AgentSignal.Negative, identification: progress);

    private async Task<SyncResponse> SynchronizeAsync(CancellationToken token)
    {
        var sync = await client!.SynchronizeAsync(token).ConfigureAwait(false);
        lastContact = clock.GetUtcNow();
        if (sync.Revogado)
        {
            if (!state.DeviceRevoked) UpdateState(current => current with { DeviceRevoked = true, TotalRemovalPending = true });
            // Remover sem decifrar: até um template corrompido precisa sair quando o leitor é revogado.
            store.DeleteAllTemplates();
            ResetScanner();
            SetStatus(AgentStatus.Revoked, "Leitor bloqueado pela Central. As digitais locais foram removidas.");
            if (state.TotalRemovalPending)
            {
                await client.ConfirmRemovalsAsync([], all: true, token).ConfigureAwait(false);
                UpdateState(current => current with { TotalRemovalPending = false, PendingRemovals = [] });
            }
            return sync;
        }
        if (state.DeviceRevoked) throw new CentralException("Este pareamento foi revogado. Gere outro código na Central para voltar a usar este computador.");
        if (sync.ServidorEm is null || (clock.GetUtcNow() - sync.ServidorEm.Value).Duration() > TimeSpan.FromMinutes(2))
            throw new CentralException("Ajuste a data e a hora do Windows para sincronizar com a Central. Acesso bloqueado.");
        lockInstalled = sync.FechaduraInstalada ?? false;
        if (sync.Dispositivo?.Nome is { Length: > 0 } name && name != Configuration.DeviceName) RememberDeviceName(name);
        var removals = store.ListTemplateIds().Except(sync.Cadastros).Concat(state.PendingRemovals).Distinct().ToList();
        if (removals.Count > 0)
        {
            // Primeiro tornar a remoção pendente durável. Depois apagar; só então confirmar no servidor.
            UpdateState(current => current with { PendingRemovals = removals });
            foreach (var id in removals) store.DeleteTemplate(id);
            foreach (var batch in removals.Chunk(CentralClient.RemovalBatchSize))
            {
                await client.ConfirmRemovalsAsync(batch, all: false, token).ConfigureAwait(false);
                UpdateState(current => current with { PendingRemovals = current.PendingRemovals.Except(batch).ToList() });
            }
        }
        return sync;
    }

    private void RememberDeviceName(string name)
    {
        // O nome só é exibido: uma falha ao gravá-lo não interrompe a leitura.
        try { UpdateState(current => current with { Configuration = current.Configuration with { DeviceName = name } }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or LocalDataException) { }
    }

    private async Task EnrollAsync(EnrollmentCommand command, CancellationToken token)
    {
        var total = command.RequiredReadings;
        var progress = new EnrollmentProgress { Name = command.Nome, Company = command.Empresa, Total = total, Reading = 1 };
        if (pendingFailure is { } failure && failure.CommandId == command.Id)
        {
            // A falha já foi decidida; só faltou avisar a Central. A pessoa não precisa ler o dedo de novo.
            await client!.CompleteAsync(failure, token).ConfigureAwait(false);
            pendingFailure = null;
            return;
        }
        pendingFailure = null;
        if (clock.GetUtcNow() >= command.ExpiraEm)
        {
            await FailEnrollmentAsync(command, progress, "expirado", null, token).ConfigureAwait(false);
            return;
        }
        var existing = store.LoadTemplates();
        try
        {
            if (existing.FirstOrDefault(t => t.CadastroId == command.CadastroId && t.ComandoId == command.Id) is { } saved)
            {
                // A digital já está guardada neste computador: só falta a Central confirmar.
                await client!.CompleteAsync(new EnrollmentCompletion(command.Id, "cadastrado", Readings: saved.Count), token).ConfigureAwait(false);
                SetResult(AgentStatus.Ready, "Cadastro confirmado na Central. As regras de acesso continuam sendo decididas pela Central.", ResultDisplay, null,
                    enrollment: progress with { Phase = EnrollmentPhase.Completed, Good = total, Reading = total });
                return;
            }
        }
        finally { ClearTemplates(existing); }
        if (string.IsNullOrWhiteSpace(Configuration.ScannerDllPath))
        {
            SetStatus(AgentStatus.Unconfigured, "Há um cadastro aguardando. Selecione a DLL oficial x64 para iniciar a captura.");
            return;
        }
        scanner ??= scannerFactory(Configuration.ScannerDllPath);
        mustLift = true;
        var collector = new EnrollmentCollector(scanner, recognizer, clock, pollInterval);
        DateTimeOffset? failingSince = null;
        async Task GuardAsync(CancellationToken guardToken)
        {
            if (clock.GetUtcNow() >= command.ExpiraEm) throw new EnrollmentFailedException("expirado");
            SyncResponse sync;
            try
            {
                sync = await SynchronizeAsync(guardToken).ConfigureAwait(false);
                failingSince = null;
            }
            catch (Exception ex) when (ex is CentralException or HttpRequestException || (ex is OperationCanceledException && !guardToken.IsCancellationRequested))
            {
                failingSince ??= clock.GetUtcNow();
                if (clock.GetUtcNow() - failingSince.Value > EnrollmentNetworkTolerance) throw;
                return;
            }
            if (sync.Revogado) throw new CentralException("Leitor revogado pela Central. Acesso bloqueado.");
            if (sync.Comando?.Id != command.Id || !sync.Cadastros.Contains(command.CadastroId)) throw new EnrollmentCancelledException();
        }
        List<byte[]>? samples = null;
        var current = progress;
        try
        {
            samples = await collector.CollectAsync(total, GuardAsync, sample => FindDuplicate(sample, command.CadastroId),
                (phase, good, reading) => ReportEnrollment(current = progress with { Phase = phase, Good = good, Reading = reading }), token).ConfigureAwait(false);
            ReportEnrollment(progress with { Phase = EnrollmentPhase.Checking, Good = total, Reading = total });
            // Última conferência sem tolerância: o pedido precisa continuar valendo na Central.
            if (clock.GetUtcNow() >= command.ExpiraEm) throw new EnrollmentFailedException("expirado");
            var sync = await SynchronizeAsync(token).ConfigureAwait(false);
            if (sync.Revogado) throw new CentralException("Leitor revogado pela Central. Acesso bloqueado.");
            if (sync.Comando?.Id != command.Id || !sync.Cadastros.Contains(command.CadastroId)) throw new EnrollmentCancelledException();
            // A gravação local precede concluir. O mesmo comando pode ser retomado sem coletar outra digital.
            store.SaveTemplate(new StoredTemplate(command.CadastroId, command.Id, samples[0]) { ExtraTemplates = samples.Skip(1).ToArray() });
            await client!.CompleteAsync(new EnrollmentCompletion(command.Id, "cadastrado", Readings: samples.Count), token).ConfigureAwait(false);
            SetResult(AgentStatus.Ready, $"Digital cadastrada na Central com {samples.Count} leituras boas. As regras de acesso continuam sendo decididas pela Central.",
                ResultDisplay, AgentSignal.Positive, enrollment: progress with { Phase = EnrollmentPhase.Completed, Good = total, Reading = total });
        }
        catch (EnrollmentFailedException failed)
        {
            await FailEnrollmentAsync(command, current, failed.Reason, failed.DuplicateOf, token).ConfigureAwait(false);
        }
        catch (EnrollmentCancelledException)
        {
            SetResult(AgentStatus.Ready, "Cadastro cancelado na Central. Nenhuma digital foi gravada.", ResultDisplay, null,
                enrollment: current with { Phase = EnrollmentPhase.Cancelled });
        }
        finally
        {
            if (samples is not null)
                foreach (var sample in samples) CryptographicOperations.ZeroMemory(sample);
        }
    }

    private async Task FailEnrollmentAsync(EnrollmentCommand command, EnrollmentProgress progress, string reason, Guid? duplicateOf, CancellationToken token)
    {
        var completion = new EnrollmentCompletion(command.Id, "falhou", reason, reason == "duplicada" ? duplicateOf : null);
        SetResult(AgentStatus.Denied, reason switch
        {
            "duplicada" => "Esta digital já está cadastrada neste leitor. Confira o cartão na Central.",
            "expirado" => "O tempo do pedido de cadastro acabou. Gere um novo pedido na Central.",
            _ => "Não foi possível ler bem a digital depois de várias tentativas. Gere um novo pedido na Central e tente de novo."
        }, ResultDisplay, AgentSignal.Negative, enrollment: progress with { Phase = EnrollmentPhase.Failed, Motivo = reason });
        // Se a Central não responder agora, a falha é reenviada no próximo ciclo, sem pedir o dedo de novo.
        pendingFailure = completion;
        await client!.CompleteAsync(completion, token).ConfigureAwait(false);
        pendingFailure = null;
    }

    private void ReportEnrollment(EnrollmentProgress progress)
    {
        var who = string.IsNullOrWhiteSpace(progress.Name) ? "da pessoa" : "de " + progress.Name;
        var reading = $"leitura {progress.Reading} de {progress.Total}";
        var message = progress.Phase switch
        {
            EnrollmentPhase.Place when progress.Good == 0 => $"Cadastro {who}: peça para colocar o dedo no leitor ({reading}).",
            EnrollmentPhase.Place => $"Cadastro {who}: peça para colocar o mesmo dedo de novo ({reading}).",
            EnrollmentPhase.Reading => $"Cadastro {who}: lendo a {reading}. O dedo precisa ficar parado.",
            EnrollmentPhase.Lift when progress.Good == 0 => $"Cadastro {who}: peça para retirar o dedo do leitor antes de começar.",
            EnrollmentPhase.Lift => $"Cadastro {who}: {reading} boa. Peça para levantar o dedo.",
            EnrollmentPhase.BadReading => $"Cadastro {who}: não deu para ler bem. Peça para levantar o dedo e colocar de novo, sem mexer.",
            _ => $"Cadastro {who}: conferindo as leituras e guardando a digital neste computador."
        };
        SetStatus(AgentStatus.Capturing, message, enrollment: progress);
    }

    private Guid? FindDuplicate(byte[] sample, Guid ownCadastro)
    {
        var templates = store.LoadTemplates();
        try
        {
            var matcher = recognizer.Matcher(sample);
            foreach (var stored in templates)
            {
                if (stored.CadastroId == ownCadastro) continue;
                foreach (var candidate in stored.AllTemplates())
                    if (MatchingPolicy.IsMatch(matcher.Score(candidate))) return stored.CadastroId;
            }
            return null;
        }
        finally { ClearTemplates(templates); }
    }

    public async Task<PairingResult> PairAsync(string centralUrl, string code, CancellationToken cancellationToken)
    {
        CancelCycle();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (localDataFailed) throw new LocalDataException();
            SetStatus(AgentStatus.Connecting, "Validando o código com a Central.");
            using var http = CentralDiscovery.CreateHttpClient();
            // Primeiro confere o endereço (sem efeito nenhum). Só depois mexe no pareamento atual.
            var discovered = await CentralDiscovery.DiscoverAsync(centralUrl, http, cancellationToken).ConfigureAwait(false);
            var previous = Configuration.IsPaired ? await UnpairCoreAsync(cancellationToken).ConfigureAwait(false) : UnpairNotice.NotPaired;
            using var pairing = new CentralClient(discovered, appVersion: appVersion);
            var response = await pairing.PairAsync(code, cancellationToken).ConfigureAwait(false);
            var name = response.Nome ?? "";
            var config = discovered with
            {
                DeviceId = response.DispositivoId, DeviceToken = response.Token, DeviceName = name,
                ScannerDllPath = Configuration.ScannerDllPath, SoundEnabled = Configuration.SoundEnabled
            };
            ReplacePairing(config);
            SetStatus(AgentStatus.Connecting, "Pareamento salvo. Aguardando sincronização com a Central.");
            return new PairingResult(name, previous);
        }
        finally { gate.Release(); }
    }

    internal void ReplacePairing(AgentConfiguration configuration)
    {
        // O token novo já foi consumido no servidor. Após esse ponto nunca retomar o cliente antigo.
        ResetConnections();
        lock (stateLock)
        {
            localDataFailed = true;
            state = new AgentState { Configuration = LocalPreferences(configuration) };
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
    }

    public async Task ConfigureScannerAsync(string path, CancellationToken cancellationToken)
    {
        FutronicScanner.ValidateLibrary(path);
        CancelCycle();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (localDataFailed) throw new LocalDataException();
            UpdateState(current => current with { Configuration = current.Configuration with { ScannerDllPath = Path.GetFullPath(path) } });
            ResetScanner();
            SetStatus(AgentStatus.Connecting, "DLL selecionada. Verificando o leitor e a conexão com a Central.");
        }
        finally { gate.Release(); }
    }

    /// <summary>Liga ou desliga o som do leitor. Não interrompe uma leitura ou um cadastro em andamento.</summary>
    public void SetSoundEnabled(bool enabled)
    {
        if (localDataFailed) throw new LocalDataException();
        UpdateState(current => current with { Configuration = current.Configuration with { SoundEnabled = enabled } });
    }

    /// <summary>
    /// Apaga as digitais deste computador, avisa a Central (quando ela oferece `desparear`) com o crachá antigo
    /// e limpa o pareamento local.
    /// </summary>
    public async Task<UnpairNotice> ResetPairingAsync(CancellationToken cancellationToken)
    {
        CancelCycle();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var notice = await UnpairCoreAsync(cancellationToken).ConfigureAwait(false);
            SetStatus(AgentStatus.Unconfigured, notice == UnpairNotice.Confirmed
                ? "Pareamento e digitais locais removidos. A Central também desligou este leitor."
                : "Pareamento e digitais locais removidos. Revogue o leitor antigo na Central em Digitais › Central biométrica.");
            return notice;
        }
        finally { gate.Release(); }
    }

    private async Task<UnpairNotice> UnpairCoreAsync(CancellationToken token)
    {
        var old = Configuration;
        var wasRevoked = state.DeviceRevoked;
        var oldClient = client;
        client = null;
        ResetScanner();
        // 1) As digitais saem antes de qualquer aviso: a Central só marca a remoção depois que ela aconteceu.
        store.DeleteAllTemplates();
        var notice = old.IsPaired ? UnpairNotice.Unavailable : UnpairNotice.NotPaired;
        if (old.IsPaired && wasRevoked) notice = UnpairNotice.Confirmed;
        else if (old.IsPaired)
        {
            try
            {
                oldClient ??= clientFactory(old);
                // Os recursos vêm da sincronização: sem ela não se sabe se a Central entende `desparear`.
                if (oldClient.Features.Count == 0) await oldClient.SynchronizeAsync(token).ConfigureAwait(false);
                if (await oldClient.UnpairAsync(token).ConfigureAwait(false)) notice = UnpairNotice.Confirmed;
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                // Melhor esforço: sem resposta, a recepção revoga o leitor antigo na Central.
            }
        }
        oldClient?.Dispose();
        // 2) Limpa o pareamento local, preservando endereço, DLL e som.
        lock (stateLock)
        {
            localDataFailed = true;
            state = new AgentState { Configuration = LocalPreferences(old) };
            store.Clear();
            store.SaveState(state);
            localDataFailed = false;
        }
        lastContact = null;
        pendingFailure = null;
        return notice;
    }

    private static AgentConfiguration LocalPreferences(AgentConfiguration configuration) => new()
    {
        CentralUrl = configuration.CentralUrl, ScannerDllPath = configuration.ScannerDllPath, SoundEnabled = configuration.SoundEnabled
    };

    private void UpdateState(Func<AgentState, AgentState> change)
    {
        lock (stateLock)
        {
            var next = change(state);
            store.SaveState(next);
            state = next;
        }
    }

    private void CancelCycle()
    {
        try { cycleCancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }
    private void SetResult(AgentStatus status, string message, TimeSpan display, AgentSignal? signal,
        IdentificationProgress? identification = null, EnrollmentProgress? enrollment = null)
    {
        keepResultUntil = clock.GetUtcNow() + display;
        SetStatus(status, message, identification: identification, enrollment: enrollment);
        if (signal is { } value) Signaled?.Invoke(value);
    }
    private void SetStatus(AgentStatus status, string message, ReaderHint hint = ReaderHint.None,
        IdentificationProgress? identification = null, EnrollmentProgress? enrollment = null)
    {
        Snapshot = new(status, message) { Hint = hint, Identification = identification, Enrollment = enrollment, LastContactAt = lastContact };
        Changed?.Invoke(Snapshot);
    }
    private static void ClearTemplates(IEnumerable<StoredTemplate> templates)
    {
        foreach (var template in templates) template.Clear();
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

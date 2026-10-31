namespace Futureplace.AgenteFS80H.Desktop;

internal enum DiagnosticTone { Neutral, Healthy, Attention, Error }
internal sealed record DiagnosticEntry(string Message, DiagnosticTone Tone);
internal sealed record ReaderDiagnosticSummary(DiagnosticEntry Pairing, DiagnosticEntry Library,
    DiagnosticEntry Connection, DiagnosticEntry Reader, string Guidance);

/// <summary>
/// Passive checks only. Inspecting a file never loads native code or opens a second scanner.
/// Hardware and network evidence comes from the existing reading operation.
/// </summary>
internal static class ReaderDiagnostics
{
    public static DiagnosticEntry InspectLibrary(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new("Selecione o arquivo de captura nas configurações abaixo.", DiagnosticTone.Attention);
        try
        {
            if (!Path.IsPathFullyQualified(path)
                || !string.Equals(Path.GetFileName(path), "ftrScanAPI.dll", StringComparison.OrdinalIgnoreCase))
                return InvalidLibrary();
            if (!File.Exists(path))
                return new("O arquivo selecionado não foi encontrado ou está sem acesso. Selecione-o novamente.", DiagnosticTone.Attention);

            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new BinaryReader(file);
            if (file.Length < 64 || reader.ReadUInt16() != 0x5a4d) return InvalidLibrary();
            file.Position = 0x3c;
            var peOffset = reader.ReadInt32();
            if (peOffset < 64 || peOffset > file.Length - 26) return InvalidLibrary();
            file.Position = peOffset;
            if (reader.ReadUInt32() != 0x4550) return InvalidLibrary();
            var machine = reader.ReadUInt16();
            file.Position = peOffset + 20;
            var optionalSize = reader.ReadUInt16();
            var characteristics = reader.ReadUInt16();
            if (optionalSize < 2 || (characteristics & 0x2000) == 0) return InvalidLibrary();
            var magic = reader.ReadUInt16();
            if (machine == 0x014c && magic == 0x10b)
                return new("O arquivo selecionado é de 32 bits. Selecione a versão oficial de 64 bits.", DiagnosticTone.Attention);
            if (machine != 0x8664 || magic != 0x20b) return InvalidLibrary();
            return new("Arquivo de captura de 64 bits encontrado. A operação de leitura confirma seu funcionamento.", DiagnosticTone.Healthy);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return new("Não foi possível conferir o arquivo. Verifique o acesso à pasta e selecione-o novamente.", DiagnosticTone.Attention);
        }
    }

    private static DiagnosticEntry InvalidLibrary() => new("O arquivo selecionado não é compatível com o leitor de 64 bits. Selecione o arquivo oficial do fabricante.", DiagnosticTone.Attention);

    public static ReaderDiagnosticSummary Create(AgentConfiguration configuration, AgentSnapshot snapshot, DiagnosticEntry library)
    {
        var pairing = configuration.IsPaired
            ? new DiagnosticEntry("Computador pareado. Isso não confirma a conexão atual.", DiagnosticTone.Healthy)
            : new DiagnosticEntry("Pareamento pendente. Gere o código na Central e conecte este computador.", DiagnosticTone.Attention);

        var connection = snapshot.Status switch
        {
            AgentStatus.Ready or AgentStatus.Capturing or AgentStatus.Authorized or AgentStatus.Denied => new DiagnosticEntry("A Central respondeu na última operação.", DiagnosticTone.Healthy),
            AgentStatus.Connecting => new("Conectando à Central. Aguarde a confirmação.", DiagnosticTone.Neutral),
            AgentStatus.Offline => new("Sem resposta da Central. O aplicativo tenta reconectar automaticamente.", DiagnosticTone.Attention),
            AgentStatus.Revoked => new("Leitor bloqueado pela Central. Confira em Digitais › Central biométrica.", DiagnosticTone.Error),
            AgentStatus.Error => new("A operação precisa de atenção. Não há confirmação atual da conexão.", DiagnosticTone.Attention),
            _ => new("Aguardando configuração e confirmação da Central.", DiagnosticTone.Neutral)
        };

        // Só conta como resposta do aparelho o que passou por ele: espera do dedo, leitura ou cadastro em andamento.
        bool readerResponded = snapshot.Status is AgentStatus.Capturing or AgentStatus.Authorized
            || snapshot.Identification is not null
            || snapshot.Enrollment is { Phase: EnrollmentPhase.Place or EnrollmentPhase.Reading or EnrollmentPhase.Lift or EnrollmentPhase.BadReading or EnrollmentPhase.Checking }
            || snapshot.Status == AgentStatus.Ready && snapshot.Hint is ReaderHint.PlaceFinger or ReaderHint.LiftFinger
            || snapshot.Status == AgentStatus.Ready && (snapshot.Message.StartsWith("Coloque o dedo cadastrado no leitor.", StringComparison.Ordinal)
                || snapshot.Message.StartsWith("Retire o dedo do leitor antes de começar", StringComparison.Ordinal)
                || snapshot.Message.StartsWith("Digital cadastrada na Central.", StringComparison.Ordinal));
        var hardwareFailure = snapshot.Status == AgentStatus.Error
            && snapshot.Message.StartsWith("Confira o cabo USB, o driver e a DLL oficial x64", StringComparison.Ordinal);
        var hardware = readerResponded
            ? new DiagnosticEntry("O leitor respondeu na última operação.", DiagnosticTone.Healthy)
            : hardwareFailure
                ? new("Não foi possível usar o leitor. Confira o cabo USB, o driver e o arquivo selecionado.", DiagnosticTone.Attention)
                : new("Aguardando confirmação do leitor na operação de leitura.", DiagnosticTone.Neutral);

        var guidance = snapshot.Status switch
        {
            AgentStatus.Error when hardwareFailure => "Reconecte o cabo USB e confira o driver Futronic e o arquivo de captura selecionado abaixo. Depois, acompanhe uma nova leitura.",
            AgentStatus.Error => "Confira a orientação da operação na tela Leitura. Este aviso não identifica sozinho a causa da falha.",
            AgentStatus.Offline => "Confira a internet deste computador e aguarde a reconexão. O acesso depende da Central.",
            AgentStatus.Revoked => "Revise o leitor na Central em Digitais › Central biométrica. Um novo pareamento precisa ser autorizado pela administração.",
            _ when !configuration.IsPaired => "Gere o código na Central em Digitais › Central biométrica › Parear leitor e informe-o abaixo.",
            _ when library.Tone != DiagnosticTone.Healthy => "Confira o arquivo de captura nas configurações abaixo. O driver e o FS80H precisam estar preparados para a leitura.",
            _ => "Acompanhe as instruções na tela Leitura. Cadastros, permissões e horários são administrados pela Central."
        };
        return new(pairing, library, connection, hardware, guidance);
    }
}

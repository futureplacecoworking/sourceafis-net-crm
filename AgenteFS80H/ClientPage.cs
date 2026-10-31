using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Futureplace.AgenteFS80H.Desktop;

/// <summary>Cartão de quem entra, já pronto para a tela do cliente.</summary>
internal sealed record ClientPageCard
{
    public string Nome { get; init; } = "";
    public string Iniciais { get; init; } = "";
    public string? Empresa { get; init; }
    public string? Codigo { get; init; }
    public string? Sala { get; init; }
    public string? Validade { get; init; }
    public string? Foto { get; init; }
    public string Selo { get; init; } = "Identificação confirmada";
    public bool Alerta { get; init; }
}

/// <summary>
/// Estado que a página da tela do cliente desenha (ClientPage/cliente.js). Os nomes seguem o protótipo da opção A.
/// Só textos escolhidos aqui chegam ao cliente: mensagens internas do leitor nunca são repassadas.
/// </summary>
internal sealed record ClientPageState
{
    public string Fase { get; init; } = "espera";
    public string? Ped { get; init; }
    public bool Falhou { get; init; }
    public string Rot { get; init; } = "";
    public string Tit { get; init; } = "";
    public string? Destaque { get; init; }
    public string Txt { get; init; } = "";
    public string? Sala { get; init; }
    public string[] Passos { get; init; } = ["", "", "", ""];
    public string[] Etapas { get; init; } = ClientPage.Steps(false);
    public string? ErroTxt { get; init; }
    public string[] Leit { get; init; } = [];
    public string? LeitN { get; init; }
    public double Enche { get; init; }
    public ClientPageCard? Cartao { get; init; }
}

internal static class ClientPage
{
    public const string PhotoHost = "storage.googleapis.com";
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly HashSet<string> OutsideHours = new(StringComparer.Ordinal) { "fora_do_horario", "horario" };
    private static readonly HashSet<string> NotReleased = new(StringComparer.Ordinal)
    {
        "desabilitado", "arquivada", "pessoa_arquivada", "pessoa_inativa", "revogado", "horario_nao_definido"
    };
    private static readonly HashSet<string> Expired = new(StringComparer.Ordinal) { "sem_contrato", "contrato_encerrado", "acesso_expirado" };

    public static string Serialize(ClientPageState state) => JsonSerializer.Serialize(state, Json);

    public static string[] Steps(bool lockInstalled) =>
        ["Digital lida", "Pessoa reconhecida", "Acesso confirmado pela Central", lockInstalled ? "Porta liberada" : "Recepção avisada"];

    public static ClientPageState From(AgentSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.Enrollment is { } enrollment) return Enrollment(enrollment);
        if (snapshot.Identification is { } identification) return Identification(identification, now);
        return snapshot.Status switch
        {
            AgentStatus.Ready when snapshot.Hint == ReaderHint.LiftFinger
                || snapshot.Hint == ReaderHint.None && snapshot.Message.StartsWith("Retire o dedo", StringComparison.Ordinal) => Lift(),
            AgentStatus.Ready => Waiting(),
            AgentStatus.Capturing => Reading(0, false),
            AgentStatus.Authorized => Authorized(new IdentificationProgress { Completed = IdentificationProgress.Steps, Result = IdentificationResult.Authorized }, now),
            AgentStatus.Denied => Denied(new IdentificationProgress { Completed = 2, FailedStep = 3, Result = IdentificationResult.Denied }),
            AgentStatus.Connecting => new ClientPageState
            {
                Fase = "espera", Rot = "Só um instante", Tit = "Preparando o leitor", Txt = "Em alguns segundos você já pode usar o leitor."
            },
            _ => new ClientPageState
            {
                Fase = "indisponivel", Rot = "Leitor indisponível", Tit = "Fale com a recepção",
                Txt = "O leitor não está pronto agora. A recepção pode ajudar você a entrar.", Enche = 1
            }
        };
    }

    private static ClientPageState Waiting() => new()
    {
        Fase = "espera", Rot = "Bem-vindo à Future Place", Tit = "Coloque o dedo no leitor", Txt = "Use o dedo cadastrado e apoie bem no centro."
    };

    private static ClientPageState Lift() => new()
    {
        Fase = "espera", Rot = "Bem-vindo à Future Place", Tit = "Retire o dedo do leitor", Txt = "Levante o dedo e coloque de novo para uma nova leitura."
    };

    private static ClientPageState Identification(IdentificationProgress progress, DateTimeOffset now) => progress.Result switch
    {
        IdentificationResult.InProgress => Reading(progress.Completed, progress.LockInstalled),
        IdentificationResult.Authorized => Authorized(progress, now),
        IdentificationResult.NotRecognized => new ClientPageState
        {
            Fase = "negado", Passos = ["feito", "erro", "", ""], Etapas = Steps(progress.LockInstalled), ErroTxt = "Digital não reconhecida",
            Rot = "Não reconhecemos sua digital", Tit = "Vamos tentar de novo?",
            Txt = "Use o mesmo dedo do cadastro e apoie bem no centro do leitor. Se não der, fale com a recepção.", Enche = 0.55
        },
        IdentificationResult.BadReading => new ClientPageState
        {
            Fase = "negado", Passos = ["erro", "", "", ""], Etapas = Steps(progress.LockInstalled), ErroTxt = "Não deu para ler",
            Rot = "Não deu para ler sua digital", Tit = "Vamos tentar de novo?",
            Txt = "Levante o dedo e coloque de novo, apoiando bem no centro, sem mexer.", Enche = 0.3
        },
        _ => Denied(progress)
    };

    private static ClientPageState Reading(int completed, bool lockInstalled)
    {
        completed = Math.Clamp(completed, 0, IdentificationProgress.Steps - 1);
        var (title, text) = completed switch
        {
            0 => ("Lendo sua digital…", "Mantenha o dedo parado."),
            1 => ("Reconhecendo…", "Pode tirar o dedo."),
            2 => ("Consultando a Central…", "Conferindo seus horários de acesso."),
            _ => (lockInstalled ? "Liberando a porta…" : "Avisando a recepção…", "Quase lá.")
        };
        return new ClientPageState
        {
            Fase = "lendo", Rot = "Só um instante", Tit = title, Txt = text, Etapas = Steps(lockInstalled),
            Passos = Enumerable.Range(0, IdentificationProgress.Steps).Select(i => i < completed ? "feito" : i == completed ? "ativo" : "").ToArray(),
            Enche = completed == 0 ? 0.5 : 1
        };
    }

    private static ClientPageState Authorized(IdentificationProgress progress, DateTimeOffset now)
    {
        var first = FirstName(progress);
        var greeting = Greeting(now);
        var room = progress.Display?.Sala;
        return new ClientPageState
        {
            Fase = "ok",
            Rot = progress.LockInstalled ? "Acesso liberado" : "Identificação confirmada",
            Tit = first is null ? greeting + "!" : $"{greeting}, {first}!",
            Destaque = first,
            Txt = progress.LockInstalled ? "Pode entrar: a porta está liberada." : "A recepção vai abrir a porta para você.",
            Sala = room is null ? null : "Sua sala: " + room,
            Passos = ["feito", "feito", "feito", "feito"],
            Etapas = Steps(progress.LockInstalled),
            Enche = 1,
            Cartao = Card(progress, "Identificação confirmada", alert: false)
        };
    }

    private static ClientPageState Denied(IdentificationProgress progress)
    {
        var motivo = progress.Motivo?.Trim().ToLowerInvariant() ?? "";
        var first = FirstName(progress);
        if (OutsideHours.Contains(motivo))
            return new ClientPageState
            {
                Fase = "horario", Passos = ["feito", "feito", "erro", ""], Etapas = Steps(progress.LockInstalled), ErroTxt = "Fora do seu horário",
                Rot = "Acesso fora do horário", Tit = first is null ? "Olá!" : $"Olá, {first}", Destaque = first,
                Txt = progress.Display?.HorarioTexto is { } hours
                    ? $"Seu horário de acesso: {hours.TrimEnd('.')}. Fale com a recepção."
                    : "Agora está fora do seu horário de acesso. Fale com a recepção.",
                Enche = 1,
                Cartao = Card(progress, "Fora do horário", alert: true)
            };
        var (error, caption, text) = motivo switch
        {
            _ when NotReleased.Contains(motivo) => ("Acesso não liberado", "Acesso não liberado", "Seu acesso não está liberado agora. Fale com a recepção."),
            _ when Expired.Contains(motivo) => ("Acesso vencido", "Acesso vencido", "Seu acesso venceu. Fale com a recepção."),
            "demorou" => ("A leitura demorou", "Vamos tentar de novo?", "A confirmação demorou. Levante o dedo e coloque de novo."),
            _ => ("Acesso não confirmado", "Acesso não confirmado", "Fale com a recepção.")
        };
        return new ClientPageState
        {
            Fase = "negado", Passos = ["feito", "feito", "erro", ""], Etapas = Steps(progress.LockInstalled), ErroTxt = error,
            Rot = caption, Tit = first is null ? "Fale com a recepção" : $"Olá, {first}", Destaque = first, Txt = text, Enche = 1
        };
    }

    internal static ClientPageState Enrollment(EnrollmentProgress progress)
    {
        var total = Math.Clamp(progress.Total, 1, 10);
        var good = Math.Clamp(progress.Good, 0, total);
        var reading = Math.Clamp(progress.Reading, 1, total);
        var finished = progress.Phase is EnrollmentPhase.Checking or EnrollmentPhase.Completed;
        var marks = Enumerable.Range(0, total).Select(i =>
            finished || i < good ? "ok"
            : i != good ? ""
            : progress.Phase switch
            {
                EnrollmentPhase.Place or EnrollmentPhase.Reading => "agora",
                EnrollmentPhase.BadReading or EnrollmentPhase.Failed => "erro",
                _ => ""
            }).ToArray();
        var name = progress.Name.Trim();
        var first = FirstToken(name);
        var state = new ClientPageState
        {
            Fase = progress.Phase == EnrollmentPhase.Completed ? "cadastro-ok" : "cadastro",
            Rot = "Cadastro da digital",
            Leit = marks,
            LeitN = finished ? $"{total} de {total} leituras" : $"Leitura {reading} de {total}",
            Enche = progress.Phase switch
            {
                EnrollmentPhase.Completed => 1,
                EnrollmentPhase.Reading => Math.Min(1, (good + 0.6) / total),
                _ => (double)good / total
            }
        };
        return progress.Phase switch
        {
            EnrollmentPhase.Place when good == 0 => state with
            {
                Ped = "poe", Tit = name.Length == 0 ? "Coloque o dedo no leitor" : $"Olá, {name}",
                Txt = $"Vamos ler o mesmo dedo {total} vezes para a leitura ficar perfeita. Coloque o dedo no leitor."
            },
            EnrollmentPhase.Place => state with { Ped = "poe", Tit = "Coloque o mesmo dedo", Txt = "Apoie bem e mantenha parado." },
            EnrollmentPhase.Reading => state with { Tit = "Lendo…", Txt = "Mantenha o dedo parado." },
            EnrollmentPhase.Lift when good == 0 => state with { Tit = "Retire o dedo do leitor", Txt = "Levante o dedo para começar o cadastro." },
            EnrollmentPhase.Lift => good < total
                ? state with { Tit = "Boa leitura! Levante o dedo", Txt = "Retire o dedo completamente." }
                : state with { Tit = "Última leitura feita!", Txt = "Retire o dedo." },
            EnrollmentPhase.BadReading => state with { Tit = "Não deu para ler", Txt = "Não deu para ler bem. Levante o dedo e coloque de novo, sem mexer." },
            EnrollmentPhase.Checking => state with { Tit = "Conferindo as leituras…", Txt = "Só um instante." },
            EnrollmentPhase.Completed => state with
            {
                Rot = "Cadastro concluído", Tit = "Digital cadastrada!",
                Txt = first is null ? "Pronto! A recepção vai confirmar o seu acesso." : $"Pronto, {first}. A recepção vai confirmar o seu acesso."
            },
            EnrollmentPhase.Cancelled => state with { Falhou = true, Tit = "Cadastro cancelado na Central", Txt = "Fale com a recepção se precisar." },
            _ => state with
            {
                Falhou = true, Rot = "Cadastro não concluído",
                Tit = progress.Motivo switch
                {
                    "duplicada" => "Esta digital já está cadastrada",
                    "expirado" => "O tempo do cadastro acabou",
                    _ => "Não conseguimos ler bem"
                },
                Txt = progress.Motivo switch
                {
                    "duplicada" => "Fale com a recepção para conferir o seu cadastro.",
                    "expirado" => "Fale com a recepção para começar de novo.",
                    _ => "Fale com a recepção para tentar de novo."
                }
            }
        };
    }

    private static ClientPageCard? Card(IdentificationProgress progress, string seal, bool alert)
    {
        var display = progress.Display;
        var name = Clean(display?.Nome) ?? Clean(progress.Name);
        if (name is null) return null;
        return new ClientPageCard
        {
            Nome = name,
            Iniciais = Initials(name),
            Empresa = Clean(display?.Empresa),
            Codigo = Clean(display?.Codigo),
            Sala = Clean(display?.Sala),
            Validade = Clean(display?.ValidadeTexto) ?? FormatDate(display?.Validade),
            Foto = SafePhoto(display?.Foto),
            Selo = seal,
            Alerta = alert
        };
    }

    /// <summary>Só endereços https do armazenamento de fotos do cadastro; qualquer outro vira as iniciais.</summary>
    internal static string? SafePhoto(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Length > 2048) return null;
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.HostNameType != UriHostNameType.Dns
            || !string.Equals(uri.Host, PhotoHost, StringComparison.OrdinalIgnoreCase))
            return null;
        return uri.AbsoluteUri;
    }

    internal static string Greeting(DateTimeOffset now)
    {
        var hour = TimeZoneInfo.ConvertTime(now, Brasilia).Hour;
        return hour is >= 6 and < 12 ? "Bom dia" : hour is >= 12 and < 18 ? "Boa tarde" : "Boa noite";
    }

    internal static string Initials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => word.FirstOrDefault(char.IsLetterOrDigit)).Where(letter => letter != default).ToArray();
        if (words.Length == 0) return "";
        var initials = words.Length == 1 ? words[0].ToString() : $"{words[0]}{words[^1]}";
        return initials.ToUpper(Portuguese);
    }

    private static string? FirstName(IdentificationProgress progress) =>
        Clean(progress.Display?.PrimeiroNome) ?? FirstToken(progress.Display?.Nome) ?? FirstToken(progress.Name);

    private static string? FirstToken(string? name) =>
        Clean(name)?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

    private static string? Clean(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) || text.Any(char.IsControl) ? null : text;
    }

    private static string? FormatDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : null;
}

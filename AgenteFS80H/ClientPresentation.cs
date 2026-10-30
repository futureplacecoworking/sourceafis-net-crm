namespace Futureplace.AgenteFS80H.Desktop;

internal enum ClientMotion { Waiting, Reading, Confirmed, Attention, Unavailable }

/// <summary>Public copy is allowlisted: runtime messages, names and configuration never reach the customer display.</summary>
internal sealed record ClientPresentation(string Caption, string Title, string Instruction,
    Color Accent, ClientMotion Motion, string? Welcome = null)
{
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");

    public static ClientPresentation From(AgentSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.Status == AgentStatus.Authorized)
        {
            var hour = TimeZoneInfo.ConvertTime(now, Brasilia).Hour;
            var greeting = hour is >= 6 and < 12 ? "Bom dia!" : hour is >= 12 and < 18 ? "Boa tarde!" : "Boa noite!";
            return new("IDENTIFICAÇÃO AUTORIZADA", greeting, "Aguarde a recepcionista para entrar.",
                CentralTheme.Success, ClientMotion.Confirmed, "Seja bem-vindo ao Future Place!");
        }
        if (snapshot.Status == AgentStatus.Ready)
        {
            if (snapshot.Message.StartsWith("Retire o dedo", StringComparison.Ordinal)) return Lift();
            if (snapshot.Message.StartsWith("Digital cadastrada", StringComparison.Ordinal)
                || snapshot.Message.StartsWith("Cadastro confirmado", StringComparison.Ordinal))
                return new("CADASTRO CONCLUÍDO", "Digital cadastrada", "A recepcionista vai orientar os próximos passos.", CentralTheme.Primary, ClientMotion.Waiting);
            return Place();
        }
        if (snapshot.Status == AgentStatus.Capturing)
        {
            if (snapshot.Message.StartsWith("Retire o dedo", StringComparison.Ordinal)) return Lift();
            if (snapshot.Message.StartsWith("Coloque o mesmo dedo", StringComparison.Ordinal))
                return new("CONFIRME SUA DIGITAL", "Repita com o mesmo dedo", "Coloque o mesmo dedo no leitor mais uma vez.", CentralTheme.Primary, ClientMotion.Reading);
            if (snapshot.Message.StartsWith("Coloque o dedo", StringComparison.Ordinal)) return Place();
            return new("LEITURA EM ANDAMENTO", "Aguarde um instante", "Acompanhe a próxima orientação na tela.", CentralTheme.Primary, ClientMotion.Reading);
        }
        return snapshot.Status switch
        {
            AgentStatus.Connecting => new("SÓ UM INSTANTE", "Preparando o leitor", "Aguarde. A recepcionista pode ajudar se precisar.", CentralTheme.Primary, ClientMotion.Waiting),
            AgentStatus.Denied => new("PROCURE A RECEPÇÃO", "Acesso não autorizado", "Fale com a recepcionista para verificar seu acesso.", CentralTheme.Warning, ClientMotion.Attention),
            _ => new("PROCURE A RECEPÇÃO", "Leitor indisponível", "Fale com a recepcionista para continuar.", CentralTheme.Warning, ClientMotion.Unavailable)
        };
    }

    private static ClientPresentation Place() => new("IDENTIFIQUE-SE PARA CONTINUAR", "Coloque o dedo no leitor", "Use o dedo cadastrado e acompanhe as orientações.", CentralTheme.Primary, ClientMotion.Reading);
    private static ClientPresentation Lift() => new("ACOMPANHE A LEITURA", "Retire o dedo do leitor", "Levante o dedo completamente e aguarde a próxima orientação.", CentralTheme.Primary, ClientMotion.Reading);
}

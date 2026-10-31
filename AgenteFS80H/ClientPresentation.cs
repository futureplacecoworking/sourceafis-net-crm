namespace Futureplace.AgenteFS80H.Desktop;

internal enum ClientMotion { Waiting, Reading, Confirmed, Attention, Unavailable }

/// <summary>
/// Tela simples do cliente (sem WebView2). Os textos são escolhidos aqui: mensagens internas, nomes e configuração
/// nunca chegam a ela.
/// </summary>
internal sealed record ClientPresentation(string Caption, string Title, string Instruction,
    Color Accent, ClientMotion Motion, string? Welcome = null)
{
    public static ClientPresentation From(AgentSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.Enrollment is { } enrollment) return Enrollment(enrollment);
        if (snapshot.Identification is { } identification && identification.Result != IdentificationResult.Authorized)
        {
            var page = ClientPage.From(snapshot, now);
            return identification.Result switch
            {
                IdentificationResult.InProgress => new("ACOMPANHE A LEITURA", page.Tit, page.Txt, CentralTheme.Primary, ClientMotion.Reading),
                IdentificationResult.NotRecognized => new("PROCURE A RECEPÇÃO", "Digital não reconhecida", page.Txt, CentralTheme.Warning, ClientMotion.Attention),
                IdentificationResult.BadReading => new("TENTE DE NOVO", "Não deu para ler", page.Txt, CentralTheme.Warning, ClientMotion.Attention),
                _ => new("PROCURE A RECEPÇÃO", "Acesso não autorizado", page.Txt, CentralTheme.Warning, ClientMotion.Attention)
            };
        }
        if (snapshot.Status == AgentStatus.Authorized)
        {
            var greeting = ClientPage.Greeting(now) + "!";
            return new("IDENTIFICAÇÃO AUTORIZADA", greeting, "Aguarde a recepcionista para entrar.",
                CentralTheme.Success, ClientMotion.Confirmed, "Seja bem-vindo ao Future Place!");
        }
        if (snapshot.Status == AgentStatus.Ready)
        {
            if (snapshot.Hint == ReaderHint.LiftFinger) return Lift();
            if (snapshot.Hint == ReaderHint.PlaceFinger) return Place();
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

    private static ClientPresentation Enrollment(EnrollmentProgress progress)
    {
        var page = ClientPage.Enrollment(progress);
        // O nome de quem cadastra não aparece na tela simples; só o andamento.
        var title = progress.Phase == EnrollmentPhase.Place && progress.Good == 0 ? "Coloque o dedo no leitor" : page.Tit;
        return progress.Phase switch
        {
            EnrollmentPhase.Completed => new("CADASTRO CONCLUÍDO", "Digital cadastrada", "A recepcionista vai orientar os próximos passos.", CentralTheme.Success, ClientMotion.Confirmed, page.LeitN),
            EnrollmentPhase.Failed or EnrollmentPhase.Cancelled => new("CADASTRO DA DIGITAL", title, page.Txt, CentralTheme.Warning, ClientMotion.Attention),
            EnrollmentPhase.BadReading => new("CADASTRO DA DIGITAL", title, page.Txt, CentralTheme.Warning, ClientMotion.Reading, page.LeitN),
            _ => new("CADASTRO DA DIGITAL", title, page.Txt, CentralTheme.Primary, ClientMotion.Reading, page.LeitN)
        };
    }

    private static ClientPresentation Place() => new("IDENTIFIQUE-SE PARA CONTINUAR", "Coloque o dedo no leitor", "Use o dedo cadastrado e acompanhe as orientações.", CentralTheme.Primary, ClientMotion.Reading);
    private static ClientPresentation Lift() => new("ACOMPANHE A LEITURA", "Retire o dedo do leitor", "Levante o dedo completamente e aguarde a próxima orientação.", CentralTheme.Primary, ClientMotion.Reading);
}

namespace Futureplace.AgenteFS80H.Desktop;

/// <summary>
/// Larguras máximas dos textos que quebram linha. Dependem só do tamanho da janela, da escala da tela e da barra de rolagem:
/// os mesmos números dão sempre o mesmo resultado, então repetir o cálculo nunca muda nada.
/// </summary>
internal readonly record struct ContentWidths(int Action, int Full, int Checklist, int Hero)
{
    /// <param name="formWidth">Largura da área onde as páginas ficam, que é a largura útil da janela.</param>
    /// <param name="pageWidth">Largura de uma página (Leitura ou Configurações) sem a barra de rolagem.</param>
    /// <param name="scrollBarWidth">Largura da barra de rolagem vertical, sempre reservada.</param>
    /// <param name="scale">Escala da tela (DeviceDpi / 96): 1 a 100%, 1,5 a 150%.</param>
    public static ContentWidths Compute(int formWidth, int pageWidth, int scrollBarWidth, float scale)
    {
        int Px(int logical) => (int)Math.Round(logical * scale);
        var usable = pageWidth - scrollBarWidth;
        var full = Math.Max(Px(320), usable - Px(68));
        return new ContentWidths(
            Action: Math.Max(Px(240), formWidth - Px(64)),
            Full: full,
            Checklist: Math.Max(Px(200), full - Px(260)),
            Hero: Math.Max(Px(280), usable - Px(240)));
    }
}

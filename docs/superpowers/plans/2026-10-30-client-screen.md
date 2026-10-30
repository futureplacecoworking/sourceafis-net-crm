# Tela do cliente Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans para executar os passos nesta sessão, com revisão final.

**Goal:** Oferecer uma apresentação simples voltada ao cliente sem retirar os controles da recepcionista.

**Architecture:** Uma janela WinForms de leitura recebe o mesmo AgentSnapshot da janela existente. Uma projeção de mensagens fixas evita expor dados internos. MainForm gerencia sua instância e ciclo de vida; TrayContext acrescenta a entrada no menu.

**Tech Stack:** C#, .NET 10, WinForms, NUnit, GitHub Actions Windows.

**Spec:** docs/superpowers/specs/2026-10-30-client-screen-design.md

## Global Constraints

- Testes só no GitHub; estado sintético, sem USB, Central real ou alterações nos cadastros.
- Preservar pareamento, DPAPI, captura, protocolo e regras de acesso.
- Reusar logo e fontes locais. Não afirmar abertura de porta.
- Janela sem dados internos ou ações administrativas; F11 e Esc afetam só a apresentação.

## Review Focus

- Falha/revogação após autorização deve remover o resultado anterior imediatamente.
- Mensagens do runtime podem conter informações privadas; exibir só frases predefinidas.
- Fechar e reabrir não duplica janelas nem escreve no armazenamento.
- Abrir com estado salvo não inventa conexão ou prontidão do hardware.
- Tela pequena e monitor com escala devem manter a instrução principal legível.

### Task 1: apresentação, integração e entrega

**Files:** criar `AgenteFS80H/ClientScreenForm.cs`, `AgenteFS80H/ClientAnimation.cs`, `AgenteFS80H/ClientPresentation.cs`, `AgenteFS80H/MainForm.ClientScreen.cs`, `AgenteFS80H.Desktop.Tests/ClientScreenTests.cs`; modificar MainForm.cs, TrayContext.cs, AgenteFS80H.csproj e docs/AGENTE-FS80H.md.

**Interfaces:** consome `AgentSnapshot(AgentStatus Status, string Message)` e `CentralTheme`; produz `ClientPresentation.From(AgentSnapshot, DateTimeOffset)`, `ClientScreenForm.ShowSnapshot(AgentSnapshot)` e `MainForm.ShowClientScreen()`.

- [ ] Escrever testes de interface que abrem pelo botão, acompanham as instruções reais, suprimem dados privados e nunca afirmam abertura física.
- [ ] Enviar os testes antes da implementação e confirmar falha por ausência do botão no GitHub.
- [ ] Implementar projeção, janela responsiva, botão, bandeja, instância única e encerramento coordenado.
- [ ] Animar a marca localmente e apresentar saudação autorizada por horário de Brasília, cobrindo os limites 06:00, 12:00 e 18:00 nos testes.
- [ ] Gerar prévias nativas e testar fechamento/reabertura, atalhos e substituição de autorização por falha.
- [ ] Atualizar guia e versão para 1.2.0; compilar localmente, sem rodar testes locais.
- [ ] Confirmar a suíte completa no GitHub, revisar a diferença, mesclar e entregar ZIP e prévia verificados.

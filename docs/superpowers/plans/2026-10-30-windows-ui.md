# Redesenho do leitor Windows — plano

> **For agentic workers:** Use superpowers:executing-plans. Os trabalhos de tema e harness podem ser delegados; a janela e a integração ficam com o implementador principal.

**Goal:** Fazer a leitura automática ser compreensível e aplicar a identidade da Central ao app já instalado.

**Architecture:** Duas páginas nativas, Leitura/Configurações, sobre o mesmo AgentRuntime; controles existentes não são recriados a cada snapshot.

**Tech Stack:** .NET10, WinForms, NUnit, GitHub Actions Windows e fontes/arte oficial incorporadas.

**Spec:** `../specs/2026-10-30-windows-ui-design.md`.

## Global Constraints

- Todos os testes rodam somente no GitHub; compilar e inspecionar localmente é permitido.
- Captura, protocolo, DPAPI, caminhos e confirmação destrutiva permanecem; nenhuma alteração no CRM ou em dados reais.
- Nenhum binário Futronic no pacote. Fontes e logo funcionam sem acesso externo no uso.
- A aprovação anterior para mesclar/publicar persiste; executar o redesenho solicitado, sem nova rodada de autorização para escolhas rotineiras.

## Review Focus

- Campos e foco sobrevivem a snapshots e navegação.
- Estado Ready conserva a instrução real, inclusive retirar o dedo; Offline/Revoked apagam o resultado autorizado anterior.
- Primeira configuração é alcançável; pareamento não é mostrado como conexão comprovada.
- X mantém bandeja; encerrar aguarda tarefas; navegação não muda dados locais.
- Conteúdo e ações são alcançáveis no tamanho mínimo e em escala maior.

### Task1: Interface, recursos e entrega

**Files:** `AgenteFS80H/MainForm*.cs`, `CentralTheme.cs`, `Assets/*`, `TrayContext.cs`, `AgenteFS80H.csproj`, projeto `AgenteFS80H.Desktop.Tests`, workflow Windows, instalador e guia.

- [ ] Escrever testes desktop e observar falha na interface antiga pelo CI.
- [ ] Aplicar tema e recursos oficiais offline; implementar páginas Leitura/Configurações preservando os contratos públicos de MainForm e as mensagens reais.
- [ ] Compilar e executar todos os testes no CI Windows; gerar PNGs nativos de estados sintéticos.
- [ ] Revisar o resultado visual e o diff independentemente; corrigir problemas encontrados e concluir o CI.
- [ ] Atualizar o guia, integrar a mudança e disponibilizar o novo ZIP para Luiz, preservando os dados da instalação existente.

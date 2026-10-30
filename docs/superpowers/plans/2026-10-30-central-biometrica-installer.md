# Central Biométrica Installer Implementation Plan

> **For agentic workers:** executar com tarefas independentes e revisão final; testes só no GitHub.

**Goal:** instalar Central Biométrica 1.3.0 com um EXE gráfico, preservando a configuração atual.

**Architecture:** Inno Setup compila o app autocontido já produzido pelo CI. Mantém identificadores e armazenamento antigos; substitui a experiência de instalação e nomes visíveis. Versão vem do csproj.

**Tech Stack:** WinForms/.NET 10, Inno Setup, PowerShell de teste no CI, GitHub Actions.

**Spec:** docs/superpowers/specs/2026-10-30-central-biometrica-installer-design.md

## Global Constraints

Testes exclusivamente no GitHub. Sem biometria real, credenciais, comandos de porta ou alteração do CRM. Sem nova conta Windows/serviço de sistema: DPAPI continua na conta já usada. Não alterar dados existentes para reduzir etapas.

## Review Focus

- Instalação anterior deve migrar sem duplicar apps nem perder dados.
- Agente em uso deve encerrar graciosamente antes de trocar binários.
- Cancelar ou falhar não pode apagar dados ou fingir instalação concluída.
- A versão do instalador, arquivo e UI deve coincidir.
- Desinstalação remove executáveis/startup/atalhos e mantém dados por padrão.

### Task 1: EXE e verificação

**Files:** `installer/CentralBiometrica.iss`, `installer/Test-Setup.ps1`, `.github/workflows/fs80h-windows.yml`.

**Interfaces:** bundle em `artifacts/bundle/app`; produto interno `AgenteFS80H.exe`; saída `artifacts/setup/Central-Biometrica-Setup.exe`; versão do csproj passada por define AppVersion.

- [ ] Criar teste de instalação/migração antes do script; observar RED no CI.
- [ ] Implementar instalador, compilar no runner e testar instalação/atualização/desinstalação reais.
- [ ] Produzir EXE, checksum e preview nativo do assistente.

### Task 2: marca e versão visível

**Files:** `AgenteFS80H/MainForm.cs`, `Program.cs`, `TrayContext.cs`, `AgenteFS80H.csproj`, testes Desktop e `docs/ALTERACOES-CENTRAL-BIOMETRICA.md`.

**Interfaces:** manter nomes técnicos/identificadores compatíveis; Product e título Central Biométrica, Version 1.3.0.

- [ ] Garantir versão visível e metadados coerentes nos testes Desktop.
- [ ] Atualizar marca visível e histórico, sem tocar no core.

### Task 3: entrega

- [ ] Atualizar guia, revisar alterações, validar suíte completa Windows/Linux no GitHub.
- [ ] Mesclar com autorização existente, verificar árvore e entregar EXE bruto com versão e hash conferidos.

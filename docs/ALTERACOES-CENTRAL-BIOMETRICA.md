# Versões da Central Biométrica

## 1.3.0 — 30/10/2026

- Instalador Windows EXE com logo, progresso, atalhos e desinstalador gráfico.
- Nome Central Biométrica e versão visíveis no app e no Windows.
- Atualização oferecida pelo aplicativo, com download verificado e preservação da configuração.
- Diagnóstico de pareamento, biblioteca do leitor, comunicação e orientações de correção.
- Compatibilidade com pareamento, digitais, DLL e instalação anteriores.

## 1.2.0 — 30/10/2026

- Tela separada voltada ao cliente, animação e saudação pelo horário de Brasília.

## 1.1.0 — 30/10/2026

- Identidade visual da Central, logo escolhida e páginas Leitura e Configurações.

## 1.0.0

- Captura FS80H, comparação local e autorização pela Central, sem comando de porta.

## Publicar uma próxima versão

Altere `Version` em `AgenteFS80H/AgenteFS80H.csproj` e descreva a mudança acima.
Use `major.minor.patch`: correções aumentam patch; funcionalidades aumentam minor.
Depois dos testes e do merge, o workflow publica o instalador em uma release
`central-biometrica-vX.Y.Z`, com manifesto e SHA256. A mesma versão não deve ser
sobrescrita. A Central Biométrica verifica novas versões ao abrir e a cada seis
horas; a recepcionista também pode usar **Verificar atualizações**.

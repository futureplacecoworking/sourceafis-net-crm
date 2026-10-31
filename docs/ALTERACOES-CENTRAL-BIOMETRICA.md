# Versões da Central Biométrica

## 1.4.1 — 31/10/2026

Ajuste para a janela não travar mais ao abrir **Configurações**:

- **Corrigida a provável causa da janela "Não está respondendo" ao clicar em "Configurar leitor"** (também ao abrir
  Configurações pela bandeja ou pela lista de preparação), vista em computadores com a tela em zoom de 125% ou 150%. O
  tamanho dos textos agora é ajustado uma vez só, na escala certa da tela, e a janela só se redesenha quando alguma coisa
  de fato mudou (antes ela era redesenhada a cada 0,6 segundo, mesmo com o leitor parado). A página que está escondida
  também deixa de ser refeita a cada leitura. A tela do cliente ganhou o mesmo cuidado. A causa foi encontrada lendo o
  programa; se o travamento voltar, o registro local (abaixo) mostra o que aconteceu.
- **O programa guarda um registro local de erros e travamentos**, só neste computador, em
  `%LOCALAPPDATA%\Futureplace\AgenteFS80H\logs` (dois arquivos de até 512 KB). Se a janela ficar mais de 3 segundos sem
  responder, o registro anota quanto tempo durou e em que etapa estava. Não grava nomes, digitais, endereços, códigos nem
  a mensagem dos erros: só o tipo do erro e o caminho no programa. Serve para a equipe entender o que aconteceu se algo
  parecido voltar.
- Nada muda no pareamento, nas digitais, nas regras de acesso nem na Central.

## 1.4.0 — 31/10/2026

O que mudou depois do teste no FS80H (issue #265 da Central):

- **Cadastro com 4 leituras do mesmo dedo** (o número vem da Central; a Central antiga continua com 2). Cada leitura
  nova precisa bater com uma anterior. Leitura ruim (dedo mexeu, saiu cedo, erro do aparelho) pede de novo, **sem
  cancelar o cadastro**. O pedido só falha por tempo esgotado (até 10 minutos), digital que já é de outra pessoa
  (a Central recebe qual cadastro já tem essa digital) ou depois de 15 tentativas. Se a Central cancelar o pedido, a tela
  mostra "Cadastro cancelado na Central" e nada é gravado.
- **Identificação com uma leitura só**, comparando com todas as leituras boas guardadas de cada pessoa (vale a melhor
  nota de cada uma; a margem contra outra pessoa continua valendo). Continua sem autorização offline e sem enviar
  leituras com mais de 20 segundos.
- **Tela do cliente nova (opção A · Rastreio)** para o monitor em pé: as etapas ganham ✓ (digital lida, pessoa
  reconhecida, acesso confirmado pela Central, recepção avisada), o cartão branco de quem entra (foto ou iniciais, nome,
  empresa, ID do cliente, sala e validade), "Sua sala", a seta para o leitor e, no cadastro, a digital enchendo leitura
  por leitura. Textos próprios para fora do horário, acesso não liberado, acesso vencido e digital não reconhecida.
  Usa o Microsoft Edge WebView2 Runtime do Windows; sem ele, a tela simples continua funcionando e a recepção é avisada
  de como instalar.
- **Som curto** de ok (dois tons) e de não (tom grave), com a opção "Som no leitor" em Configurações (começa ligada).
- **Pareamento claro e protegido:** a janela mostra "Ligado à Central como «nome do leitor»", o último contato e a
  versão; depois de parear, uma mensagem confirma o nome do leitor. Limpar o pareamento pede para digitar **LIMPAR** e
  trocar o pareamento de um PC já pareado pede **TROCAR**. Nos dois casos as digitais saem deste computador e a Central
  é avisada para desligar o leitor antigo (sem leitor fantasma). Sem conexão, a janela pede para revogar o leitor em
  **Digitais › Central biométrica**.
- **Lista de preparação** com ✓ por etapa: Central conectada, pareado, arquivo do leitor (DLL), leitor FS80H e tela do
  cliente, cada uma com explicação e botão para resolver.
- O caminho na Central passou a ser **Digitais › Central biométrica**.
- Compatível com a Central antiga e com a nova: o programa só usa o que a Central anuncia em `recursos`.

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

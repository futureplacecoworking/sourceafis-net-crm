# Central Biométrica para Windows

Aplicativo para Windows 10/11 de 64 bits, com ícone na bandeja, que captura digitais no Futronic FS80H e compara os templates com o SourceAFIS neste computador. A Central cadastra pessoas, decide a autorização com as regras atuais e registra os eventos. **Esta versão não aciona fechadura.** Uma autorização exibida não significa que uma porta abriu.

## Usar no dia a dia

Abra **Central Biométrica** no menu Iniciar. A página **Leitura** mostra a orientação em destaque. **A leitura começa automaticamente no aparelho FS80H conectado por USB**: a pessoa coloca o dedo cadastrado, mantém parado até a tela dizer que pode tirar e aguarda a resposta da Central. Uma leitura basta para identificar.

Logo abaixo, a janela mostra se o computador está **"Ligado à Central como «nome do leitor»"** (ou **"Ainda não pareado"**), o horário do último contato com a Central e a versão do programa. A **Preparação do leitor** lista cinco etapas com ✓: Central conectada, pareado, arquivo do leitor (DLL) selecionado, leitor FS80H conectado e tela do cliente aberta. Cada etapa tem uma explicação curta e um botão para resolver o que falta.

Use **Configurações** para conectar o computador à Central, selecionar a DLL de captura, escolher o início com Windows e ligar ou desligar o **Som no leitor**. **Abrir Central** leva ao CRM; autorizações, cadastros e horários ficam em **Digitais › Central biométrica**. As duas páginas preservam a configuração e os campos digitados ao alternar.

O visual usa a marca, cores e fontes da Central. As fontes acompanham o aplicativo e não são baixadas durante o uso. A interface mantém texto junto das cores e acompanha a escala do Windows.

## Tela voltada ao cliente

A janela principal é usada pela **recepcionista**. Clique em **Tela do cliente** nela, no menu da bandeja ou na Preparação do leitor para abrir a apresentação separada, pensada para o **monitor de 15" em pé (9:16) atrás do leitor**. Em outro formato de tela, ela fica centralizada com faixas pretas. Não é necessário abrir outro programa ou parear o leitor novamente.

É a **opção A · Rastreio** aprovada para a Central: enquanto espera, a tela pede o dedo e uma seta mostra que **o leitor fica logo abaixo desta tela**. Na leitura, uma linha do tempo ganha ✓ a cada etapa: **digital lida → pessoa reconhecida → acesso confirmado pela Central → recepção avisada** (quando a fechadura existir, a última etapa vira **porta liberada**). Quando a Central confirma, aparecem a saudação pelo horário de Brasília (“Bom dia”, “Boa tarde” ou “Boa noite”, com o primeiro nome), **“Sua sala: …”** e o cartão branco de quem entra: foto do cadastro (só do armazenamento do Conexa, `storage.googleapis.com`; sem foto, as iniciais), nome, empresa, ID do cliente, sala, validade e o selo **Identificação confirmada**. Fora do horário, o cartão ganha o selo âmbar e a tela mostra o horário de acesso que a Central informou. Acesso não liberado, acesso vencido, digital não reconhecida e leitura ruim têm textos próprios e sempre terminam em "Fale com a recepção" quando a pessoa precisa de ajuda. Mensagens internas do programa nunca aparecem para o cliente.

No **cadastro**, a tela cumprimenta a pessoa pelo nome, explica que o mesmo dedo será lido algumas vezes e mostra a digital enchendo leitura por leitura, com os marcadores e "Leitura 2 de 4". Leitura ruim aparece como "Não deu para ler bem. Levante o dedo e coloque de novo, sem mexer." e o cadastro continua.

A tela nova usa o **Microsoft Edge WebView2 Runtime**, que já vem no Windows 11 e na maioria dos Windows 10 atualizados. A página, as fontes e a logo ficam na pasta do programa; nada é baixado da internet além da foto do cadastro. Se o WebView2 faltar ou falhar, a tela do cliente abre no **modo simples** (a apresentação anterior, em WinForms) e a recepção vê como resolver: em **Preparação do leitor › Baixar WebView2**, instale o “Evergreen Bootstrapper” gratuito da Microsoft e abra a Tela do cliente de novo.

Quando há outro monitor, a apresentação abre nele em tela cheia (o monitor em pé tem preferência); com apenas um, abre no monitor atual. **F11** alterna tela cheia e janela, permitindo arrastá-la para o monitor desejado. **Esc** fecha só a apresentação; o agente e a tela da recepcionista continuam disponíveis. Esta apresentação não bloqueia o uso do Windows.

O **som curto** acompanha o resultado: dois tons agradáveis para entrada confirmada ou digital cadastrada e um tom grave para não. Os sons são gerados pelo programa (não há arquivo de áudio) e podem ser desligados em **Configurações › Som no leitor**.

Uma falha, desconexão ou revogação remove imediatamente a apresentação de autorização anterior. Fechar a janela principal mantém a apresentação acompanhando as leituras; encerrar o agente fecha ambas. A atualização preserva o pareamento e as digitais já cadastradas.

## Baixar e instalar

1. Baixe [Central-Biometrica-Setup.exe](https://github.com/futureplacecoworking/sourceafis-net-crm/releases/latest/download/Central-Biometrica-Setup.exe).
2. Abra o arquivo e clique em **Instalar**. O assistente exibe a logo e o progresso.
3. Clique em **Concluir** para abrir a **Central Biométrica**.

Não é necessário extrair ZIP, abrir PowerShell ou instalar o .NET. O instalador
cria atalhos no menu Iniciar e na área de trabalho, registra o aplicativo nas
Configurações do Windows e ativa o início automático para uma instalação nova.
Ao fechar a janela, o aplicativo continua na bandeja. Funciona na conta Windows
conectada; o driver USB oficial continua sendo um componente separado.

Quem já instalou a versão anterior mantém o pareamento, as digitais e o caminho
da DLL nesta mesma conta. Atualizações também preservam a preferência de início
automático. A troca dos arquivos aguarda o encerramento seguro do app.

## Versões e atualização pelo app

A versão aparece no rodapé e em **Configurações**. O [histórico de alterações](ALTERACOES-CENTRAL-BIOMETRICA.md)
registra cada entrega. Uma nova versão publicada aparece em uma barra com
**Atualizar agora** e **Depois**; **Configurações → Verificar atualizações** permite
consultar manualmente. A consulta automática ocorre ao abrir e a cada seis horas,
inclusive enquanto o aplicativo está na bandeja. Publicações apenas do CRM não
exigem uma nova versão Windows.

Ao aceitar, o aplicativo baixa o instalador da release oficial, confere origem,
versão e SHA256 e mostra o progresso. O instalador encerra a versão atual,
substitui os arquivos e abre a nova, preservando a configuração. Uma falha na
consulta ou no download não interrompe as leituras. Os pacotes não possuem
assinatura Authenticode sem um certificado de publicação configurado.

## Diagnóstico automático

Em **Configurações**, o diagnóstico informa pareamento, presença e arquitetura
do arquivo de captura, resposta da Central e evidência de funcionamento do
leitor. O botão **Atualizar diagnóstico** repete a conferência passiva; não abre
uma segunda captura USB nem cadastra ou apaga digitais. Uma DLL encontrada não
confirma sozinha que o leitor/driver estão funcionando.

## Preparar o leitor

Os componentes proprietários Futronic **não acompanham o aplicativo nem este repositório**. Obtenha-os pela [página oficial de downloads](https://www.futronic-tech.com/download.php):

- [Driver USB assinado, Windows 8/8.1/10/11, 32 e 64 bits](https://www.futronic-tech.com/futronic/attachment/upload/futronic/download/ftrDriverSetup_win8_whql_3471.zip), publicado como versão 10.0.0.1.
- [Exemplo/API de captura para Windows](https://www.futronic-tech.com/futronic/attachment/upload/futronic/download/ftrScanApiEx_v4.5.zip), para testar o aparelho. A DLL incluída nesse demo é **x86 (32 bits)** e não serve no agente x64.
- A biblioteca `ftrScanAPI.dll` **x64/AMD64 (64 bits)** deve vir do SDK Windows do fabricante. O instalador [SDK 4.2.2084](https://www.futronic-tech.com/download/ftp/Windows_SDK_v4.2_2084.rar) contém as duas arquiteturas; selecione a de 64 bits. A arquitetura do instalador `.exe` não determina a arquitetura das DLLs que ele contém.

Instale o driver oficial, conecte o FS80H por USB e confirme que o dispositivo aparece sem erro no Gerenciador de Dispositivos. Confira a captura no programa de exemplo do fabricante. Em seguida, feche o exemplo para liberar o leitor.

Na Central Biométrica, abra **Configurações** e selecione a DLL oficial x64. Mantenha-a em uma pasta permanente da conta Windows; o aplicativo guarda a localização e valida a arquitetura antes de carregá-la. Bibliotecas de outros projetos ou sites não fazem parte da instalação suportada. As condições de uso e redistribuição desses componentes são as do fabricante; este projeto não concede licença sobre eles.

O [fabricante especifica](https://www.futronic-tech.com/pro-detail.php?pro_id=1543) imagem de 320 × 480 pixels, 500 DPI, 8 bits e Live Finger Detection para o **FS80H**. O FS81H não oferece LFD. Nesta entrega, o modelo suportado é FS80H com LFD habilitada.

## Parear e cadastrar

1. Na Central, um administrador humano abre **Digitais › Central biométrica**, cria o leitor e gera um código de pareamento temporário. Para trocar o PC de um leitor, o código pode dizer qual leitor ele substitui: o antigo é desligado sozinho quando o PC novo pareia.
2. No aplicativo Windows, abra **Configurações** e informe a **URL HTTPS da Central**, como `https://central.futureplacecoworking.com.br`, e o **código de pareamento**. O aplicativo obtém a configuração pública da Central automaticamente; não é necessário procurar ou digitar chaves técnicas. Confirme o pareamento. Use apenas a Central da sua organização; o código é temporário e de uso único. Ao terminar, uma mensagem confirma o nome do leitor ("Pronto! Este computador está ligado à Central como «…»") e a página Leitura passa a mostrar esse nome.
3. Se o computador **já estava pareado**, parear de novo pede para digitar **TROCAR**: as digitais deste computador são apagadas e a Central é avisada para desligar o leitor antigo. Assim não sobra leitor "fantasma" na Central.
4. Na Central, o Administrador autoriza a pessoa e, em **Digitais › Central biométrica**, manda o pedido de cadastro para o leitor. O pedido vale por até 10 minutos e pode ser cancelado na Central.
5. A pessoa coloca o **mesmo dedo** algumas vezes (4 na Central atual; 2 na Central antiga), levantando entre uma leitura e outra. Cada leitura nova precisa bater com uma anterior. Se o dedo mexer, sair cedo ou o aparelho falhar, a tela pede de novo, **sem cancelar o cadastro**. O pedido só falha por tempo esgotado, por digital que já é de outra pessoa neste leitor (a Central recebe qual cadastro já tem essa digital) ou depois de 15 tentativas. A recepção acompanha "Cadastrando Ana Souza — leitura 2 de 4" na janela principal.
6. As regras de acesso (horário e validade) são da pessoa, na Central. Cadastrar uma digital, sozinho, não autoriza acesso: quem decide é a Central, na hora.

Depois do cadastro, o aplicativo guarda todas as leituras boas da pessoa e compara cada nova leitura com elas, localmente: vale a melhor nota de cada pessoa. Só informa autorização quando a Central confirma a regra vigente. A comparação exige correspondência suficiente e margem em relação a **outra pessoa** (leituras da mesma pessoa não contam como ambiguidade); leituras ambíguas são recusadas. Retire o dedo entre apresentações. Nenhuma imagem ou template é enviado à Central; saem apenas os identificadores e dados de protocolo necessários ao cadastro, à remoção e à decisão.

Os parâmetros iniciais de comparação são score mínimo **40** e margem mínima **10** em relação ao segundo candidato. Esses scores não são percentuais de certeza. São valores de partida que precisam de calibração no leitor físico e de avaliação de falsas aceitações/recusas antes do uso operacional.

### Compatibilidade com a Central

O programa 1.4 funciona com a Central antiga e com a nova. Tudo o que é novo só é usado depois que a Central anuncia em `recursos`: a versão do programa na sincronização (`versao`), o cadastro com várias leituras (`leituras`), o cartão de quem entra (`exibir`), o aviso de despareamento (`desparear`) e o cadastro que já tem a digital (`duplicada_de`). Com a Central antiga, o programa manda exatamente o que a 1.3 mandava.

Se a rede cair, a Central ficar indisponível ou o leitor apresentar erro, o estado exibido orienta a correção. **Não há autorização offline**, nem envio posterior de identificações antigas. O aplicativo tenta restabelecer a comunicação. O PC e o agente precisam permanecer ligados e a conta Windows deve estar conectada.

## Bandeja, dados e revogação

Fechar a janela mantém o aplicativo na bandeja. Use o ícone para abrir o leitor, acessar as configurações ou encerrar o agente. A inicialização automática usa a chave de registro da conta atual `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valor `FutureplaceAgenteFS80H`.

Token de dispositivo e templates ficam cifrados com **DPAPI CurrentUser** em `%LOCALAPPDATA%\Futureplace\AgenteFS80H\data`. A proteção depende da conta Windows; os arquivos não são uma forma portável de backup e não devem ser enviados a suporte. Não copie imagens, templates, token ou pastas de dados para tickets, chats ou logs. Imagens de captura são transitórias.

Revogue um cadastro ou o leitor pela Central. O bloqueio no servidor é imediato; quando o aplicativo sincroniza, apaga os templates locais removidos e confirma a exclusão à Central. Aguarde essa confirmação antes de desligar ou desinstalar o leitor. Um computador desligado não pode confirmar remoção física dos seus arquivos.

**Limpar pareamento e digitais** (em Configurações) explica as consequências e pede para digitar **LIMPAR**. Primeiro as digitais saem deste computador; depois o programa avisa a Central, com o pareamento antigo, para desligar este leitor. Se a Central não responder (sem internet ou Central antiga), a limpeza local acontece do mesmo jeito e a janela pede para revogar o leitor em **Digitais › Central biométrica**.

Para atualizar manualmente, execute o novo `Central-Biometrica-Setup.exe`. O instalador pede ao aplicativo que encerre, substitui o aplicativo e preserva os dados cifrados. Se o agente não encerrar, a atualização é interrompida: feche-o pela bandeja e tente novamente. O instalador não encerra processos à força.

## Desinstalar

Use **Configurações do Windows → Aplicativos → Central Biométrica → Desinstalar**.
O desinstalador gráfico encerra o aplicativo, remove arquivos, atalhos e início
automático e preserva o pareamento/digitais desta conta para uma reinstalação.
O driver e a DLL externa do fabricante são preservados.

Para eliminar as digitais locais, use **Limpar pareamento e digitais** dentro do
aplicativo antes de desinstalar; a ação pede para digitar LIMPAR e avisa a Central
quando ela oferece esse aviso. Sem esse aviso, revogue o leitor pela Central em
Digitais › Central biométrica. Desinstalar localmente não revoga o dispositivo no servidor.
Os dados do navegador da tela do cliente ficam em `%LOCALAPPDATA%\Futureplace\AgenteFS80H\WebView2`,
separados das digitais.

## Validação da entrega

O workflow Windows executa os testes existentes do SourceAFIS, os testes do agente e os testes da janela, publica `win-x64` autocontido e fornece o ZIP instalável, checksum, resultados `.trx` e capturas de estados sintéticos da interface. Conforme a regra deste projeto, os testes são executados **somente no CI do GitHub**. Não inclua dados biométricos reais nos testes.

Uma release é opcional: um mantenedor deve executar o workflow manualmente com `create_draft_release` ativado e uma tag como `agente-fs80h-v1.0.0`. O fluxo cria apenas um **rascunho**, depois dos testes; a publicação exige revisão humana no GitHub.

O CI usa leitores simulados e verifica a janela em Windows, sem WebView2 nos testes da tela (a página é conferida pelo estado que recebe e pelos arquivos publicados). Antes de uso na recepção, é necessário instalar o pacote no PC do leitor e validar captura, LFD, cadastro com 4 leituras e leitura ruim repetida, duplicidade/ambiguidade, autorização e negação pela Central, cancelamento do pedido na Central, perda de conexão, revogação com confirmação, a tela nova no monitor em pé, o som e o LIMPAR/TROCAR com aviso à Central. Os testes da janela e suas capturas não validam o sensor físico.

O reconhecimento usa [SourceAFIS for .NET](https://github.com/robertvazan/sourceafis-net), sob Apache 2.0; a licença acompanha o pacote. O algoritmo e suas dependências existentes são preservados nesta integração.

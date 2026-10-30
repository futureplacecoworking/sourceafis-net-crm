# Agente FS80H para a Central

Aplicativo para Windows 10/11 de 64 bits, com ícone na bandeja, que captura digitais no Futronic FS80H e compara os templates com o SourceAFIS neste computador. A Central cadastra pessoas, decide a autorização com as regras atuais e registra os eventos. **Esta versão não aciona fechadura.** Uma autorização exibida não significa que uma porta abriu.

## Usar no dia a dia

Abra **Agente FS80H** no menu Iniciar. A página **Leitura** mostra a orientação em destaque. **A leitura começa automaticamente no aparelho FS80H conectado por USB**: coloque o dedo cadastrado, retire-o quando a janela pedir e apresente o mesmo dedo novamente. A instrução muda a cada etapa; aguarde o resultado da Central.

Use **Configurações** para conectar o computador à Central, selecionar a DLL de captura e escolher o início com Windows. **Abrir Central** leva ao CRM; cadastros e horários ficam em **Automação e sensores → Leitor da recepção**. As duas páginas preservam a configuração e os campos digitados ao alternar.

O visual usa a marca, cores e fontes da Central. As fontes acompanham o aplicativo e não são baixadas durante o uso. A interface mantém texto junto das cores e acompanha a escala do Windows.

## Tela voltada ao cliente

A janela principal é usada pela **recepcionista**. Clique em **Tela do cliente** nela ou no menu da bandeja para abrir a apresentação separada: marca animada, instrução grande e resultado, sem configurações, nomes ou dados internos. Não é necessário abrir outro programa ou parear o leitor novamente.

Quando há outro monitor, a apresentação abre nele em tela cheia; com apenas um, abre no monitor atual. **F11** alterna tela cheia e janela, permitindo arrastá-la para o monitor desejado. **Esc** fecha só a apresentação; o agente e a tela da recepcionista continuam disponíveis. Ela pode ser reaberta pelo botão ou pela bandeja. Esta apresentação não bloqueia o uso do Windows.

As orientações acompanham a captura real, inclusive retirar completamente e repetir o mesmo dedo. Quando a Central autoriza, a animação confirma a identificação e mostra **“Bom dia!”, “Boa tarde!” ou “Boa noite!”**, pelo horário de Brasília, e **“Seja bem-vindo ao Future Place!”**. A saudação usa manhã das 06:00 às 11:59, tarde das 12:00 às 17:59 e noite no restante. Como a fechadura ainda não está integrada, a pessoa deve aguardar a recepcionista para entrar. Cadastros concluídos, recusas e falhas têm mensagens próprias. Não há som nem vídeo baixado da internet.

Uma falha, desconexão ou revogação remove imediatamente a apresentação de autorização anterior. Fechar a janela principal mantém a apresentação acompanhando as leituras; encerrar o agente fecha ambas. A atualização preserva o pareamento e as digitais já cadastradas.

## Baixar e instalar

1. No GitHub deste fork, abra **Actions → Agente FS80H — Windows** e escolha uma execução concluída com sucesso da versão que será instalada.
2. Em **Artifacts**, baixe **`AgenteFS80H-instalacao-Windows`** para instalar com uma única extração. A opção `AgenteFS80H-win-x64` continua disponível com o ZIP interno e seu checksum `.sha256`; nessa opção, extraia também o ZIP interno.
3. Extraia **todo** o pacote de instalação para uma pasta. Devem aparecer `Install.ps1`, `Uninstall.ps1`, `LEIA-ME.md`, `LICENSE`, `build-info.json` e a pasta `app`. Não execute o instalador de dentro do ZIP.
4. Nessa pasta, abra o PowerShell da sua conta Windows e execute:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1
   ```

O instalador usa `%LOCALAPPDATA%\Programs\Futureplace\AgenteFS80H`, cria um atalho no menu Iniciar e configura a inicialização ao entrar nessa conta Windows. Não exige administrador nem uma instalação separada do .NET; o runtime .NET 10 acompanha o aplicativo. O driver USB pode exigir administrador. Para instalar sem iniciar agora, acrescente `-NoStart`; para desativar a inicialização automática, acrescente `-NoStartup`.

Para conferir o ZIP antes de instalar:

```powershell
Get-FileHash .\AgenteFS80H-win-x64.zip -Algorithm SHA256
Get-Content .\AgenteFS80H-win-x64.zip.sha256
```

Compare os valores com o checksum do mesmo download. Os pacotes deste fluxo não têm assinatura Authenticode; confira o repositório e o commit em `build-info.json` antes de executar. Não altere a política de execução global do computador.

## Preparar o leitor

Os componentes proprietários Futronic **não acompanham o aplicativo nem este repositório**. Obtenha-os pela [página oficial de downloads](https://www.futronic-tech.com/download.php):

- [Driver USB assinado, Windows 8/8.1/10/11, 32 e 64 bits](https://www.futronic-tech.com/futronic/attachment/upload/futronic/download/ftrDriverSetup_win8_whql_3471.zip), publicado como versão 10.0.0.1.
- [Exemplo/API de captura para Windows](https://www.futronic-tech.com/futronic/attachment/upload/futronic/download/ftrScanApiEx_v4.5.zip), para testar o aparelho. A DLL incluída nesse demo é **x86 (32 bits)** e não serve no agente x64.
- A biblioteca `ftrScanAPI.dll` **x64/AMD64 (64 bits)** deve vir do SDK Windows do fabricante. O instalador [SDK 4.2.2084](https://www.futronic-tech.com/download/ftp/Windows_SDK_v4.2_2084.rar) contém as duas arquiteturas; selecione a de 64 bits. A arquitetura do instalador `.exe` não determina a arquitetura das DLLs que ele contém.

Instale o driver oficial, conecte o FS80H por USB e confirme que o dispositivo aparece sem erro no Gerenciador de Dispositivos. Confira a captura no programa de exemplo do fabricante. Em seguida, feche o exemplo para liberar o leitor.

No Agente FS80H, abra **Configurações** e selecione a DLL oficial x64. Mantenha-a em uma pasta permanente da conta Windows; o aplicativo guarda a localização e valida a arquitetura antes de carregá-la. Bibliotecas de outros projetos ou sites não fazem parte da instalação suportada. As condições de uso e redistribuição desses componentes são as do fabricante; este projeto não concede licença sobre eles.

O [fabricante especifica](https://www.futronic-tech.com/pro-detail.php?pro_id=1543) imagem de 320 × 480 pixels, 500 DPI, 8 bits e Live Finger Detection para o **FS80H**. O FS81H não oferece LFD. Nesta entrega, o modelo suportado é FS80H com LFD habilitada.

## Parear e cadastrar

1. Na Central, um administrador humano abre **Automação e sensores › Leitor da recepção**, cria o leitor e gera um código de pareamento temporário.
2. No aplicativo Windows, abra **Configurações** e informe a **URL HTTPS da Central**, como `https://central.futureplacecoworking.com.br`, e o **código de pareamento**. O aplicativo obtém a configuração pública da Central automaticamente; não é necessário procurar ou digitar chaves técnicas. Confirme o pareamento. Use apenas a Central da sua organização; o código é temporário e de uso único.
3. Em **Automação e sensores**, solicite o cadastro no leitor para a pessoa escolhida. O cartão existente em Digitais é apenas consultado como vínculo; a tela, os controles e os registros manuais das fechaduras permanecem intactos.
4. Siga o estado exibido pelo aplicativo: apresente o dedo, retire-o completamente e apresente o mesmo dedo novamente. São necessárias duas leituras consistentes. O cadastro também recusa digital já associada a outro cadastro local.
5. Na Central, configure as permissões e horários do cadastro. Cadastrar uma digital, sozinho, não autoriza acesso.

Depois do cadastro, o aplicativo compara localmente. Só informa autorização quando a Central confirma a regra vigente. A comparação exige correspondência suficiente e margem em relação a outra pessoa; leituras ambíguas são recusadas. Retire o dedo entre apresentações. Nenhuma imagem ou template é enviado à Central; saem apenas os identificadores e dados de protocolo necessários ao cadastro, à remoção e à decisão.

Os parâmetros iniciais de comparação são score mínimo **40** e margem mínima **10** em relação ao segundo candidato. Esses scores não são percentuais de certeza. São valores de partida que precisam de calibração no leitor físico e de avaliação de falsas aceitações/recusas antes do uso operacional.

Se a rede cair, a Central ficar indisponível ou o leitor apresentar erro, o estado exibido orienta a correção. **Não há autorização offline**, nem envio posterior de identificações antigas. O aplicativo tenta restabelecer a comunicação. O PC e o agente precisam permanecer ligados e a conta Windows deve estar conectada.

## Bandeja, dados e revogação

Fechar a janela mantém o aplicativo na bandeja. Use o ícone para abrir o leitor, acessar as configurações ou encerrar o agente. A inicialização automática usa a chave de registro da conta atual `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valor `FutureplaceAgenteFS80H`.

Token de dispositivo e templates ficam cifrados com **DPAPI CurrentUser** em `%LOCALAPPDATA%\Futureplace\AgenteFS80H\data`. A proteção depende da conta Windows; os arquivos não são uma forma portável de backup e não devem ser enviados a suporte. Não copie imagens, templates, token ou pastas de dados para tickets, chats ou logs. Imagens de captura são transitórias.

Revogue um cadastro ou o leitor pela Central. O bloqueio no servidor é imediato; quando o aplicativo sincroniza, apaga os templates locais removidos e confirma a exclusão à Central. Aguarde essa confirmação antes de desligar ou desinstalar o leitor. Um computador desligado não pode confirmar remoção física dos seus arquivos.

Para atualizar, execute `Install.ps1` de um novo pacote completo. O instalador pede ao agente que encerre, substitui o aplicativo e preserva os dados cifrados. Se o agente não encerrar, a atualização é interrompida: feche-o pela bandeja e tente novamente. O instalador não encerra processos à força.

## Desinstalar

Use **Configurações → Aplicativos → Futureplace — Agente FS80H → Desinstalar**, ou execute:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\Futureplace\AgenteFS80H\Uninstall.ps1"
```

O desinstalador pergunta explicitamente se deve **manter** os dados cifrados para reinstalação nesta conta, **remover** todos os dados locais ou **cancelar**. Enter cancela. A remoção é definitiva e exige novo pareamento/cadastro. Para uma execução administrada com decisão já tomada, passe exatamente uma opção:

```powershell
# Preservar explicitamente os dados locais:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Uninstall.ps1 -KeepData

# Excluir explicitamente os dados locais:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Uninstall.ps1 -RemoveData
```

Revogue o leitor na Central e aguarde a confirmação de remoção com o agente conectado antes de desinstalar. A desinstalação local não revoga credenciais no servidor. O driver USB e a DLL externa do fabricante são preservados.

## Validação da entrega

O workflow Windows executa os testes existentes do SourceAFIS, os testes do agente e os testes da janela, publica `win-x64` autocontido e fornece o ZIP instalável, checksum, resultados `.trx` e capturas de estados sintéticos da interface. Conforme a regra deste projeto, os testes são executados **somente no CI do GitHub**. Não inclua dados biométricos reais nos testes.

Uma release é opcional: um mantenedor deve executar o workflow manualmente com `create_draft_release` ativado e uma tag como `agente-fs80h-v1.0.0`. O fluxo cria apenas um **rascunho**, depois dos testes; a publicação exige revisão humana no GitHub.

O CI usa leitores simulados e verifica a janela em Windows. Antes de uso na recepção, é necessário instalar o pacote no PC do leitor e validar captura, LFD, duas leituras com retirada, duplicidade/ambiguidade, autorização e negação pela Central, perda de conexão e revogação com confirmação. Os testes da janela e suas capturas não validam o sensor físico.

O reconhecimento usa [SourceAFIS for .NET](https://github.com/robertvazan/sourceafis-net), sob Apache 2.0; a licença acompanha o pacote. O algoritmo e suas dependências existentes são preservados nesta integração.

# Central Biométrica — instalador Windows

Luiz pediu um aplicativo com nome **Central Biométrica**, instalador `.exe`
com logo e progresso, poucos cliques e configuração reaproveitada. Pediu também
versões para acompanhar atualizações e autorizou executar todas as etapas.

## Escolha de implementação

Inno Setup, já disponível no runner Windows: assistente gráfico nativo, arquivos
autocontidos, atalhos, registro de aplicativo e desinstalador próprios. A escolha
evita distribuir scripts ou exigir a extração de ZIP. MSIX exigiria uma cadeia
de assinatura e instalação ainda não configurada; outro bootstrapper próprio
duplicaria funções consolidadas do instalador.

## Contrato

- `Central-Biometrica-Setup.exe`, versão 1.3.0 derivada do csproj, marca existente,
  botão Instalar direto, barra de progresso e concluir abrindo o aplicativo.
- Instalação por usuário em Windows 10/11 x64, sem depender de instalar .NET.
- Preservar diretório interno, DPAPI CurrentUser, mutex/eventos, cadastros,
  DLL selecionada e pareamento existentes; nenhuma credencial no instalador.
- Início com Windows ativo por padrão, app na bandeja ao fechar a janela.
- Atualização solicita encerramento gracioso, sem matar processos à força.
- Migrar o registro/atalho da instalação PowerShell para uma única entrada
  Central Biométrica; desinstalador gráfico preserva dados por padrão.
- Atualizar nome visível, ícone, versão no app/Windows e histórico de alterações.
- Não redistribuir driver/DLL Futronic nem mudar leitura, permissões, CRM,
  módulo Digitais ou acionar porta. No PC de Luiz, a configuração atual é mantida.
- Testes somente no GitHub: instalação real, atualização de instalação antiga,
  preservação de dados, atalho, startup, segundo plano, desinstalação e cancelamento.
- Entregar o `.exe` real em link para download; ZIP fica somente como artefato técnico.

O instalador não terá assinatura Authenticode sem certificado configurado. Não
desativar nem orientar desativação de proteções do Windows/Chrome.

## Adições autorizadas durante o trabalho

- Luiz pediu atualização pelo próprio app: consultar a release pública oficial
  na inicialização e a cada seis horas; oferecer Atualizar agora/Depois, com
  progresso. Só executar após ação da recepcionista e validação da versão,
  origem e SHA256; falhas não alteram as leituras ou decisões de acesso.
- Publicar EXE/manifesto/checksum automaticamente após os testes no master,
  com tag imutável derivada da versão do projeto. Sem tokens dentro do app.
- Luiz autorizou diagnóstico automático: conferência passiva de configuração,
  arquitetura da DLL, último retorno da Central e evidência real do leitor,
  sem abrir outra captura nem fingir detectar a causa de qualquer erro.
- Alertas de leitor offline e porta aberta são tratados no repositório do CRM,
  com dados reais; a integração física do hub permanece necessária para portas.

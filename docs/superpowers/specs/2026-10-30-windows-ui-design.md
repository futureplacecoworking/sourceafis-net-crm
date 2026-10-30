# Leitor Windows com a identidade da Central

Luiz pediu que o aplicativo Windows siga a UI e a UX da Central, depois de confundir a janela atual de configuração com a leitura do aparelho.

O aplicativo continua nativo em WinForms e Windows x64. Manter esta base permite atualizar a interface sem outro runtime de navegador, sem mudar o leitor, o pareamento, o armazenamento DPAPI ou a comparação SourceAFIS. Uma interface WebView reaproveitaria CSS, mas acrescentaria dependência e uma ponte de mensagens desnecessária para duas páginas.

A página **Leitura** é a inicial: marca oficial no cabeçalho azul profundo, navegação Leitura/Configurações, cartão com pictograma de digital, orientação grande e a mensagem real do runtime. A pessoa vê que precisa usar o aparelho FS80H e que a leitura começa automaticamente. As instruções de retirar e reapresentar o dedo nunca são substituídas por um progresso inventado. O estado muda completamente quando há bloqueio, revogação ou falta de conexão. O aviso de que esta etapa não abre uma porta permanece visível.

A página **Configurações** mantém URL, código mascarado, seleção da DLL oficial, início com Windows e limpeza confirmada. Pareamento e DLL têm resumos que descrevem configuração, sem transformar isso em sinal de conexão. O cadastro e os horários apontam para Automação e sensores → Leitor da recepção. Navegar e receber estados não apaga campos digitados. Fechar a janela mantém a bandeja, como hoje.

Usar os tokens da Central: #001a30 no cabeçalho; #003a6c nas ações; fundo #f4f5f7; cartões brancos com cantos12; botões/entradas com cantos6; Nunito nos títulos e botões e Raleway no corpo, embutidas com OFL para funcionar offline. Reutilizar o PNG e o ICO oficiais sem redesenhar a marca. Alvos de44px, foco visível, texto junto das cores, páginas roláveis e escala de DPI do Windows.

Não alterar captura, biometria, protocolo, autorização, dados reais, DLL do fabricante ou o módulo Digitais do CRM. Não registrar identidades ou imagens localmente para decorar a tela. A atualização conserva os caminhos e os dados existentes.

Validar no CI Windows: navegação, foco/rascunho, instruções, troca de autorização por bloqueio, encerramento e tamanhos. Publicar capturas nativas dos estados sintéticos e novo ZIP autocontido. O uso do sensor físico segue a validação feita no PC da recepção.

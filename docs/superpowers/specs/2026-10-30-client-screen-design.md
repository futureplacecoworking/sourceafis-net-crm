# Tela do cliente — leitor da recepção

Luiz aprovou e pediu a criação da visão simplificada proposta na conversa: logo,
“Coloque o dedo”, “Retire o dedo” e resultado, sem configurações nem informações
internas. A janela atual continua destinada à recepcionista.

## Abordagem

Adicionar uma janela de apresentação ao mesmo agente Windows é a escolha de
implementação: compartilha as notificações de leitura já existentes, não disputa
o USB e pode ocupar o monitor voltado ao cliente. Substituir a tela da
recepcionista dificultaria operar o cadastro; criar outro programa exigiria
sincronizar dois processos sem necessidade.

## Comportamento

- Botão **Tela do cliente** na janela da recepcionista e no menu da bandeja.
- Uma única janela do cliente por agente; abrir novamente traz a existente.
- Abre em tela cheia no outro monitor, quando houver; senão, no monitor atual.
  F11 alterna entre janela e tela cheia; Esc fecha só a apresentação.
- Logo da digital integrada escolhida por Luiz, identidade da Central, texto
  grande e ajuda “Precisa de ajuda? Fale com a recepcionista.”
- Luiz acrescentou animação fluida semelhante a um vídeo e saudação ao concluir.
  Implementação: animação nativa da marca com ondas e confirmação visual, sem
  vídeo remoto, som, novas dependências ou imagem de uma digital real.
- Apresentação somente de leitura: sem cadastro, controles administrativos,
  endereço, DLL, código de pareamento, credenciais, nomes ou mensagem técnica.
- Projeção de mensagens predefinidas a partir do estado real: colocar, retirar,
  repetir o mesmo dedo, cadastro concluído, autorização, recusa e indisponibilidade.
- Identificação autorizada mostra “Bom dia!” das 06:00 às 11:59, “Boa tarde!”
  das 12:00 às 17:59 e “Boa noite!” no restante, sempre em Brasília, junto de
  “Seja bem-vindo ao Future Place!” e pedido para aguardar a recepcionista.
  Cadastro, recusa e indisponibilidade não recebem a saudação de autorização.
  Nunca afirmar que a porta foi liberada: esta versão não comanda fechadura.
- Erro, desconexão, revogação e encerramento substituem imediatamente a
  autorização anterior; abrir a apresentação usa o último estado recebido.
- Fechar a apresentação não interrompe o agente. Encerrar o agente fecha ambas
  as janelas. Não é um bloqueio de acesso ao Windows ou modo quiosque protegido.

## Limites e verificação

Preservar pareamento, DPAPI, cadastros, permissões, captura e protocolo. Não mudar
o CRM nem seu módulo Digitais. Sem novo pacote ou chamada de rede para a UI.
Testes Windows somente no GitHub, com estado sintético, sem USB ou Central real.
Gerar imagens nativas para conferir as duas telas e os estados do cliente.

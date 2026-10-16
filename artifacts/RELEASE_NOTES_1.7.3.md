# PSiptv 1.7.3

## Favoritos e sincronização em segundo plano

- Adicionar ou remover podcasts, episódios, filmes, séries, rádios e canais dos favoritos agenda uma sincronização Bluetooth em segundo plano, sem aguardar a ligação na interface.
- Cada alteração local permite no máximo duas tentativas; o primeiro sucesso termina o lote. Se ambas falharem, as alterações ficam guardadas até outra alteração. Ações próximas são agrupadas durante 500 ms.
- Abrir/retomar a aplicação ou reativar o Bluetooth repõe apenas a escuta: não reinicia as tentativas de envio. As marcações visto/ouvido continuam a desencadear sincronização.
- O Android mantém a receção disponível com a aplicação minimizada ou o ecrã desligado através de um serviço de dispositivos ligados e de uma notificação persistente com opção Desativar.
- Bloquear a lista, desativar a sincronização ou fechar a aplicação nos recentes termina o serviço. São necessários Bluetooth e sincronização ativos, equipamentos emparelhados e a mesma lista/perfil desbloqueado em ambos. As restrições de bateria do sistema/fabricante podem interromper a execução.

## Continuar um podcast noutro equipamento

- Pausar ou parar um episódio de podcast favorito guarda e sincroniza o tempo de escuta, para retomar no outro equipamento a partir do último ponto recebido.
- O tempo mais recente prevalece mesmo quando se recuou no episódio. Cópias antigas não substituem um ponto mais recente.
- Um leitor já pausado no mesmo episódio recebe a posição sem começar a tocar automaticamente. Um leitor em reprodução mantém a sua posição.
- Pausa/stop usam o mesmo limite de duas tentativas. A gravação periódica local do progresso não inicia sincronizações.

## Validação

- 327 verificações automatizadas passaram, incluindo limites de tentativas, adição/remoção dos vários tipos de favoritos e sincronização do tempo nos dois sentidos.
- Código Android e compilação Windows sem erros; serviço Android e permissão connectedDevice verificados no manifesto gerado.
- Receção prolongada com o ecrã desligado, Doze e comportamento em equipamentos reais ainda requerem validação física, conforme `docs/validation.md`.

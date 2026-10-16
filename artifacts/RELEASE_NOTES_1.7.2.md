# PSiptv 1.7.2

## Controlos multimédia Android e HyperOS 3

- O leitor integrado passa a publicar título, capa, estado e posição da reprodução numa sessão multimédia Android, destinada ao Centro de Controlo, HyperIsland e ecrã bloqueado.
- Pausa, retoma, paragem e procura temporal controlam o mesmo leitor da aplicação. A procura só é anunciada nos conteúdos que a suportam.
- A janela de vídeo picture-in-picture inclui pausa e retoma.

## Sincronização ao concluir conteúdos

- Concluir um podcast ou marcar conteúdos como ouvidos/vistos permite uma tentativa Bluetooth com o equipamento conhecido, mesmo depois de a sincronização ao abrir ter falhado.
- Se o equipamento estiver indisponível, as alterações ficam guardadas. Não existem repetições periódicas: outra conclusão ou marcação permite uma nova tentativa.
- A ligação automática tem um limite de 10 segundos para estabelecer a conexão. Marcações em lote são agrupadas.
- A conclusão de um podcast em segundo plano também pode iniciar a tentativa. Requer sincronização Bluetooth ativa, permissão e equipamento previamente conhecido, com a aplicação recetora aberta na mesma lista/perfil.

## Validação

- 309 verificações automatizadas passaram, incluindo falha sem repetição, nova tentativa após outro conteúdo e marcação durante uma transferência.
- Código Android e compilação Windows sem erros.
- A apresentação no HyperOS 3 e a comunicação entre equipamentos reais ainda requerem validação física, conforme `docs/validation.md`.

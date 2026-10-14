# PSiptv 1.7.1

## Leitor e ações dos podcasts

- Tocar no título/tempo da barra de áudio ou no botão de regresso reabre o leitor do episódio ativo, conservando a posição e o estado de pausa, sem reiniciar a reprodução.
- Abrir o mesmo episódio pela lista reutiliza o leitor ativo. Escolher outro episódio continua a substituir a reprodução.
- Os botões de marcar como ouvido/por ouvir alteram apenas o estado e atualizam a lista, sem abrir o episódio. O toque para abrir fica limitado ao título/detalhes, mantendo a ativação do cartão pelo comando da TV.
- O ícone de marcar como por ouvir passa a representar um ouvido.
- Ao regressar ao leitor, são restabelecidos os eventos de bloqueio e picture-in-picture.

## Validação

- 302 verificações automatizadas passaram.
- Compilações Android e Windows sem erros.
- Os cenários de interação e reprodução em equipamentos reais estão documentados em `docs/validation.md` para confirmação.
- APK assinado com a mesma chave da 1.7.0; versão Android `298664761`.
- SHA-256 do APK: `8F994549AC8E2317809A9CC71350EE3967CD8E2BDF36F26F9F8FD0502B052FC3`.

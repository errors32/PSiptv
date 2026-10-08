# PSiptv 1.5.7

## Podcasts

- Posição de reprodução guardada por episódio e perfil nos podcasts favoritos. A página Continuar a ouvir permite retomar episódios incompletos e usa o ficheiro offline quando disponível.
- O progresso é guardado periodicamente e ao sair do leitor. Episódios marcados como ouvidos deixam de aparecer na continuação.
- Cache persistente dos feeds e da pesquisa, apresentação imediata de dados guardados e atualização quando necessário. A cache de podcasts está limitada a 100 entradas/64 MB.
- Listas apresentadas em blocos de 60 episódios, com carregamento ao fazer scroll e preservação da posição ao regressar. Dados inalterados não reconstroem a lista.
- Data da última visita por podcast, destaque de novos episódios e contadores de episódios novos e por ouvir nos favoritos.
- Downloads automáticos configuráveis por lista e perfil em Configurações → Definições dos podcasts, também acessíveis pela página de podcasts.
- Os downloads automáticos estão desligados por defeito. Opções para limitar a Wi-Fi, escolher podcasts, definir espaço disponível e apagar downloads de episódios ouvidos.
- Na primeira ativação é selecionado apenas o episódio mais recente de cada podcast escolhido. A aplicação verifica novidades enquanto está aberta, no arranque, quando a rede muda e periodicamente.
- Perda de uma rede permitida ou desativação da opção pausa downloads automáticos. Pausas feitas manualmente são respeitadas. O limite é verificado antes e durante o download.
- Os downloads de episódios continuam a exigir MP3 fornecido pelo feed; não há conversão de outros formatos.
- Velocidade de reprodução de 0,75× a 2× no leitor integrado Android/Windows, recuar 15 segundos, avançar 30 segundos e temporizador de 15/30/60 minutos.
- Alternância entre apresentação de áudio com capa e vídeo nos episódios que disponibilizam vídeo, preservando a posição. Suporta feeds com fontes de áudio e vídeo separadas e episódios apenas com vídeo.

## Sincronização e TV

- A sincronização na rede local inclui favoritos, episódios ouvidos, posição e últimas visitas. Mantém o âmbito da mesma lista e perfil e a comparação das datas.
- Botão Sincronizar agora e indicação do resultado/data em Definições dos podcasts. A outra aplicação deve estar aberta na mesma rede.
- Progresso e visitas incluídos nas cópias de segurança, com compatibilidade com formatos anteriores. As opções de downloads automáticos são locais ao equipamento.
- Acesso aos últimos 20 canais/rádios do perfil por botão na TV ao Vivo e nos Favoritos.
- Preservação da posição por perfil e grupo e restauro do foco no cartão do canal ao sair do ecrã inteiro na TV.

## Validação

- 167 verificações automatizadas concluídas, incluindo escolha de áudio/vídeo, progresso por perfil, serialização, sincronização, estados ouvidos, novidades e downloads automáticos desligados por defeito.
- Compilação Windows sem erros.
- Publicação Android Release assinada concluída sem erros. Mantém os quatro avisos existentes de APIs Android obsoletas.
- A interface Android, a mudança de rede durante um download e a sincronização entre equipamentos físicos ainda precisam de validação em equipamento.
- Leitores externos não comunicam a posição/fim à aplicação. A velocidade só é aplicada quando suportada pelo leitor e pelo conteúdo.
- A sincronização de progresso exige esta versão em ambos os equipamentos; versões anteriores continuam a trocar favoritos e estados ouvidos.

## Distribuição

- Versão e tag: `1.5.7`
- Pacote: `com.PS.PSiptv`
- Nome do APK: `PSiptv.v1.5.7.apk`
- Arquiteturas: ARM, ARM64 e x64.
- Código de versão Android: `298579481`.
- SHA-256 do APK: `CF4589EC0759BF37C02695AE24C05EF0C7980FE3A45FCEE02E1377657C8704BB`.
- Assinatura verificada e coincidente com a chave de distribuição existente.

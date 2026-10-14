# Validação

## Novidades da 1.6.5

- Atualize ambos os equipamentos. Selecione e ative o tablet/TV no comando Bluetooth; aguarde várias consultas de estado e confirme que o Android não apresenta ciclos de ligação/desligação.
- Troque repetidamente de canal e ajuste volume, incluindo um canal lento a iniciar. Confirme que consultas de estado não desativam os controlos durante o comando e que cada ação é executada uma vez.
- Recuse uma autorização de reprodução ou escolha um canal indisponível; confirme que outro canal continua a funcionar na mesma ligação. Interrompa Bluetooth e confirme reconexão nas consultas de estado.
- Troque de equipamento, feche a página e coloque cada aplicação em segundo plano; confirme encerramento das ligações e recuperação ao reabrir. Confirme também funcionamento com um recetor antigo, que mantém uma ligação por pedido.

## Novidades da 1.6.4

- Em **Comando remoto → Bluetooth → Procurar novamente**, confirme que todos os equipamentos emparelhados aparecem, incluindo acessórios e equipamentos com a aplicação fechada. A listagem não depende de uma consulta prévia ao equipamento nem da lista/perfil aberto.
- Escolha manualmente o tablet/TV com controlo Bluetooth ativo e a mesma lista/perfil; confirme estado, ativação, volume e troca de canal. Um acessório sem PSiptv deve mostrar erro de ligação sem bloquear o ecrã.
- Percorra os canais e aguarde as consultas periódicas; confirme que a posição se mantém e que uma falha temporária conserva os canais. Os cinco testes de atualização incremental da lista mantêm-se.

## Novidades da 1.6.2

- Emparelhe telemóvel e tablet/Android TV no Android, abra a mesma lista/perfil e ative o controlo Bluetooth em ambos. Use redes Wi-Fi diferentes e confirme ativação do tablet/TV, mudança dos canais favoritos e volume.
- Confirme que outro perfil/lista não permite controlo, que a autorização de reprodução continua a ser pedida no equipamento e que acessórios Bluetooth sem PSiptv apresentam erro sem executar comandos.
- Desligue o Bluetooth, revogue a permissão de dispositivos próximos, afaste os equipamentos e coloque cada aplicação em segundo plano. Confirme erros, cancelamento e recuperação ao reabrir/reativar.
- Alterne rapidamente entre Wi-Fi/Bluetooth e equipamentos, feche a página durante uma consulta e mantenha o comando aberto durante mais de 15 segundos em Wi-Fi diferentes. Confirme que respostas antigas não substituem o equipamento selecionado e que o tablet/TV permanece ativo.
- Confirme que a sincronização Bluetooth de favoritos/podcasts e o comando Wi-Fi continuam a funcionar.
- No comando remoto, percorra a lista e aguarde várias consultas automáticas. Altere o volume/canal no tablet e os favoritos; confirme que a posição não volta ao topo. Interrompa e restabeleça temporariamente o Bluetooth e confirme que os canais continuam visíveis durante a reconexão.
- Emparelhe também auscultadores e um equipamento com a PSiptv fechada. Em **Procurar novamente**, confirme que todos os equipamentos emparelhados aparecem; selecione o tablet/TV manualmente. Confirme que acessórios sem PSiptv apresentam erro de ligação e que fechar a página cancela a consulta em curso.

O protocolo do comando Bluetooth tem 11 novas verificações automatizadas. O rádio Bluetooth e os controlos de reprodução requerem validação física num tablet e numa Android TV com suporte a RFCOMM.

## Novidades da 1.6.1

- Reordene/oculte secções, fixe conteúdos e troque de perfil/lista. Confirme isolamento, reinício e reposição de backup antigo e atual.
- Compare a pré-visualização dos estilos/temas, texto grande e cartões compactos; navegue com teclado/comando e confirme foco visível e animações reduzidas.
- Adicione listas normais e protegidas ao catálogo unificado. Cancele o PIN, bloqueie durante a carga e abra resultados de origens diferentes; confirme pesquisa por tipo e troca de origem autorizada.
- Escolha versões HD/FHD/4K do mesmo canal, simule falhas após as reconexões e confirme substituição apenas quando ativada, excluindo canais ocultos/bloqueados.
- Configure duas fontes XMLTV, um identificador manual e correções horárias distintas. Confirme guia, pesquisa e gravações recorrentes, incluindo uma fonte indisponível com/sem cache.
- Crie/renomeie/elimine coleções, marque estados e conclua um filme. Confirme remoção da retoma, conservação do catálogo e isolamento entre perfis.
- Num Android TV físico, ative a integração, autorize a fila, atualize favoritos e abra a retoma pelo launcher com a aplicação fechada e aberta. Confirme que listas protegidas e conteúdos bloqueados/ocultos não aparecem, que um item ocultado pelo utilizador não reaparece em Watch Next e que desativar remove as entradas da aplicação.
- Agende programas sobrepostos e adjacentes, com margens e limites distintos; confirme conflitos e execução. Numa lista de teste, ative retenção e confirme que só gravações concluídas antigas são removidas, preservando agendadas, em curso e falhadas.
- Consulte resolução/codecs/bitrate/arranque/interrupções em reprodução real. Troque rapidamente entre conteúdos e confirme que os diagnósticos pertencem à sessão correta, sem URLs ou credenciais.
- Teste S/H/F/Espaço e F3/F2/F11, incluindo campos de texto, modais, regresso do leitor e reconstrução dos cartões. Confirme que mudar de sessão não restaura foco antigo.

As 229 verificações automatizadas passaram; Windows compilou e o código Android compilou. A execução destas verificações de dispositivo e a compilação iOS/Mac Catalyst não foram realizadas neste ambiente.

Execute `dotnet run --project PSiptv.Tests/PSiptv.Tests.csproj -c Release` para os testes do núcleo e as regressões. O processo termina com erro quando uma verificação falha. O workflow `.github/workflows/validate.yml` executa estes testes e compila Android e Windows em cada push e pull request. Não publica versões nem usa credenciais de assinatura.

As regressões abrangem pedidos de reprodução substituídos, cancelamento, escrita atómica de imagens, transferências partilhadas, pesquisa e ordenação num catálogo de 20 000 conteúdos, backups protegidos e antigos, sincronização por alteração individual, remoções e troca bidirecional pelo protocolo Bluetooth. A troca do protocolo é testada com streams locais; não substitui testes do rádio Bluetooth nos equipamentos.

## Aparência

- Em Perfil → Configurações → Aparência, alterne entre os oito estilos, com e sem histórico/favoritos. Confirme que a retoma, abertura de detalhes, favoritos e programação continuam funcionais.
- Combine cada estilo com Meia-noite, Floresta, Oceano, Terracota e Ameixa; altere também a cor de destaque. Verifique contraste, reinício e exportação/reposição das escolhas na cópia de segurança.
- Em Biblioteca, redimensione a janela acima e abaixo de 900 px para verificar navegação lateral/inferior. Teste também a rotação e o ecrã inteiro durante a reprodução; as sobreposições devem cobrir todo o ecrã.
- Em Guia TV, confirme a grelha na TV ao Vivo e que a alternância manual de vista continua disponível. Em Consola, teste o foco pelo comando remoto.

## Android TV

Com uma TV/emulador ligado ao ADB, abra uma pesquisa e mantenha o teclado visível. Execute:

```powershell
./scripts/Test-AndroidTvSearch.ps1 -Adb 'C:/Program Files (x86)/Android/android-sdk/platform-tools/adb.exe' -Serial '<serial>'
```

Repita em Podcasts, Pesquisa global e pesquisa de rádios. O script verifica Back, foco mantido, movimento para baixo e reentrada na pesquisa. Não inicia reprodução nem escreve no campo. Se o firmware não reportar a visibilidade do teclado ao `dumpsys input_method`, o teste recusa executar; valide manualmente nesse dispositivo.

Verifique também seleção de cartões, regresso do leitor ao cartão anterior, scroll e navegação em listas vazias.

## Reprodução e ciclo de vida

- Num fornecedor de teste com resolução de URLs atrasada, escolha A e depois B; faça a resposta de A chegar depois de B. B deve continuar a reproduzir. Repita fazendo A falhar.
- Saia do leitor enquanto o URL é resolvido. O pedido deve cancelar e nenhuma reprodução deve começar depois.
- Reproduza rádio/podcast, navegue para outra página e regresse: áudio e posição devem manter-se. Troque para vídeo e confirme que o áudio anterior para.
- Teste Home/retoma, bloqueio/desbloqueio e picture-in-picture: a reprodução deve seguir o comportamento definido para cada modo sem duplicar áudio.

## Bluetooth sem rede comum

1. Instale esta versão nos dois equipamentos Android e emparelhe-os nas definições Bluetooth do sistema.
2. Abra a mesma lista, com as mesmas credenciais quando existirem, e o mesmo nome de perfil nos dois equipamentos.
3. Ative **Sincronização por Bluetooth** em **Sincronização e cópia de segurança** ou nas definições dos podcasts. Autorize o acesso a dispositivos próximos.
4. Desligue o Wi-Fi num dos equipamentos. Adicione favoritos diferentes e altere progresso/estado ouvido em cada um.
5. Escolha o outro equipamento e sincronize. Confirme que ambos conservam os favoritos e convergem no estado dos podcasts.
6. Sem premir novamente sincronizar, marque podcasts como ouvidos/por ouvir e filmes/episódios como concluídos/por ver em ambos. Confirme atualização automática em ambos. Remova um favorito e confirme que a remoção só é enviada na próxima marcação ou troca manual.
7. Mude de perfil numa das aplicações: a troca deve ser recusada. Repita bloqueando a lista durante a transferência.
8. Interrompa o Bluetooth durante a troca e reative-o, sem mudar a opção na aplicação; confirme recuperação automática e ausência de dados parciais.
9. Escolha o equipamento com a outra aplicação fechada. Abra-a depois e confirme que a ligação é tentada automaticamente. Edite também no equipamento que recebeu a primeira sincronização.
10. Passe cada aplicação para segundo plano e confirme que as trocas param; reabra ambas e confirme retoma. Repita ativando a opção antes de ligar o rádio Bluetooth.
11. Mantenha ambas abertas sem marcar conteúdos durante vários minutos: não devem ocorrer tentativas automáticas. Ouça parcialmente um podcast, navegue e altere favoritos; confirme que não provocam envios. Conclua um conteúdo para provocar a troca.
12. Interrompa uma troca, deixe as aplicações abertas e confirme que não existem novas tentativas periódicas. Marque outro conteúdo ou desligue/ligue o rádio e confirme envio das alterações pendentes.
13. Teste com versões diferentes ou listas/perfis diferentes: confirme uma mensagem específica, conservada na página de sincronização, e ausência de importação indevida.

A sincronização é opcional, usa RFCOMM seguro e apenas dispositivos emparelhados. A escolha manual fica guardada antes da primeira tentativa; uma troca bem-sucedida também guarda o remetente no equipamento recetor. Apenas marcações ouvido/por ouvir e concluído/por ver desencadeiam envios, agrupados durante 500 ms. Alterações pendentes são recuperadas uma vez ao reabrir a aplicação ou ligar o rádio; sem pendências não há ligações de saída. Não existem consultas periódicas nem tentativas automáticas por temporizador, incluindo após falhas. A ligação de entrada aguarda passivamente, e o rádio usa eventos do Android para recuperar a escuta. O protocolo v2 confirma o snapshot final antes de fechar a ligação; ambos os equipamentos devem usar a versão 1.6.9 ou posterior compatível. Não liga o rádio nem emparelha equipamentos automaticamente. Os relógios devem estar acertados: conflitos no mesmo item usam a data da alteração e um desempate determinístico. Os estados de remoção são conservados para impedir que cópias antigas recuperem dados apagados. Os downloads de áudio não são transferidos. Equipamentos sem Bluetooth Classic não suportam esta ligação.

## Backups e espaço

- Exporte uma cópia protegida, restaure noutro equipamento e confirme listas, perfis, favoritos e progresso. Uma palavra-passe errada ou um ficheiro alterado deve falhar antes de modificar os dados locais.
- Restaure também um backup JSON antigo.
- Carregue podcasts, consulte o espaço de cache e limpe-o. Favoritos, estados ouvidos e downloads devem permanecer.
- Cancele downloads de logótipos durante scroll e confirme que não ficam imagens incompletas. Uma imagem ausente deve continuar sem impedir a navegação.

# Validação

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
6. Remova um favorito e sincronize. Volte a sincronizar: a remoção não deve ser revertida.
7. Mude de perfil numa das aplicações: a troca deve ser recusada. Repita bloqueando a lista durante a transferência.
8. Interrompa o Bluetooth durante a troca, reative-o e volte a ativar a opção na aplicação; confirme recuperação e ausência de dados parciais.

A sincronização é opcional, usa RFCOMM seguro e apenas dispositivos emparelhados. Depois de uma ligação manual bem-sucedida, volta a tentar o equipamento escolhido a cada minuto enquanto a aplicação estiver aberta. Não liga o rádio nem emparelha equipamentos automaticamente. Os relógios devem estar acertados: conflitos no mesmo item usam a data da alteração e um desempate determinístico. Os estados de remoção são conservados para impedir que cópias antigas recuperem dados apagados. Equipamentos sem Bluetooth Classic não suportam esta ligação. A integração é Android padrão e não depende da conta Xiaomi nem do serviço proprietário de partilha de ecrã.

## Backups e espaço

- Exporte uma cópia protegida, restaure noutro equipamento e confirme listas, perfis, favoritos e progresso. Uma palavra-passe errada ou um ficheiro alterado deve falhar antes de modificar os dados locais.
- Restaure também um backup JSON antigo.
- Carregue podcasts, consulte o espaço de cache e limpe-o. Favoritos, estados ouvidos e downloads devem permanecer.
- Cancele downloads de logótipos durante scroll e confirme que não ficam imagens incompletas. Uma imagem ausente deve continuar sem impedir a navegação.

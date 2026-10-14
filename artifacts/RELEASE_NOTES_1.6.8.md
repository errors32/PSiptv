# PSiptv 1.6.8

## Sincronização automática de podcasts por Bluetooth

- Os podcasts favoritos, episódios ouvidos/por ouvir e posição de reprodução sincronizam nos dois sentidos enquanto ambas as aplicações Android estão abertas.
- Alterações locais desencadeiam uma troca após cerca de dois segundos. A aplicação também volta a tentar a ligação a cada 17–22 segundos, além do tempo de transferência.
- O equipamento escolhido fica guardado mesmo quando a outra aplicação ainda está fechada. Depois da primeira troca, o equipamento recetor também pode iniciar sincronizações.
- A ligação recupera automaticamente após uma interrupção ou depois de ligar o Bluetooth, sem precisar de desativar e reativar a opção.
- As remoções de podcasts e marcações como «por ouvir» propagam-se; alterações independentes são preservadas e conflitos usam a alteração mais recente por item.

## Como utilizar

Emparelhe os equipamentos nas definições Bluetooth do Android. Ative **Sincronização por Bluetooth** nas definições dos podcasts em ambos, abra a mesma lista com o mesmo nome de perfil e escolha o dispositivo uma vez. Mantenha ambas as aplicações abertas. Não é necessária uma rede Wi-Fi; os ficheiros de áudio descarregados ficam em cada equipamento.

## Validação

- 252 verificações automatizadas: 214 verificações do núcleo e 38 regressões/verificações adicionais.
- As trocas do protocolo são testadas com streams locais, incluindo subscrições, estados ouvidos/por ouvir, progresso e remoções. A ligação de rádio requer validação em dois equipamentos reais.
- Android e Windows compilados em Release, sem erros; permanecem os avisos existentes.
- iOS e Mac Catalyst não foram compilados neste ambiente Windows.

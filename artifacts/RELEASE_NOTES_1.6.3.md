# PSiptv 1.6.3

## Correções e melhorias

- **Posição da lista de canais preservada**: as consultas periódicas do comando remoto atualizam a coleção existente sem voltar a criar a listagem. Canais inalterados conservam os seus itens; alterações de nome, ordem, adições e remoções são aplicadas individualmente, mantendo a posição de deslocamento.
- **Reconexão sem apagar os canais**: uma falha temporária de Bluetooth ou Wi-Fi conserva a listagem visível enquanto as consultas seguintes tentam restabelecer a ligação. Os comandos ficam temporariamente desativados até receber novo estado do equipamento.
- **Apenas aplicações disponíveis na procura Bluetooth**: a pesquisa consulta os equipamentos emparelhados e apresenta apenas os que respondem ao protocolo autenticado da PSiptv, com a aplicação aberta, controlo Bluetooth ativo e a mesma lista/perfil. Auscultadores, acessórios e aplicações fechadas deixam de aparecer como destinos.
- **Procura com limites**: até três equipamentos são consultados em paralelo, com tempo limite por equipamento e para a procura completa. Endereços repetidos são consultados apenas uma vez; o equipamento ativo aparece primeiro e fechar a página cancela a procura.
- **Comandos independentes da pesquisa**: consultas de acessórios não ocupam a fila dos comandos de reprodução.
- Mensagens da procura atualizadas em português e inglês, documentação e roteiro de validação revistos.

## Utilização

No ecrã **Comando remoto**, escolha **Bluetooth** e toque em **Procurar novamente** para atualizar os destinos disponíveis. Os equipamentos devem estar emparelhados no Android, com a PSiptv aberta, a opção **Permitir controlo por Bluetooth** ativa e a mesma lista/perfil.

As consultas de estado continuam automáticas, preservando a posição da lista de canais. O modo Wi-Fi continua disponível. A aplicação Wear OS mantém o controlo por Wi-Fi.

## Validação

- **249 verificações automatizadas**: 214 verificações do núcleo e 35 regressões/verificações adicionais.
- Nove novas verificações cobrem consultas repetidas sem notificações de alteração da lista, atualizações incrementais de canais, filtragem de equipamentos indisponíveis, deduplicação, limites de paralelismo e cancelamento da procura.
- Android e Windows compilados em Release; os avisos existentes sobre APIs Android obsoletas e campos não utilizados no Windows permanecem.
- O comportamento visual da lista e o rádio Bluetooth requerem confirmação nos equipamentos físicos. iOS e Mac Catalyst não foram compilados neste ambiente Windows.

## Distribuição

O APK **PSiptv.v1.6.3.apk** mantém o pacote **com.PS.PSiptv**, inclui ARM de 32 bits, ARM64 e x64 e utiliza a mesma identidade de assinatura da 1.6.2, com código de versão Android superior. Assinatura, alinhamento e metadados são verificados antes da publicação.

- Código de versão Android: **298638981**.
- SHA-256: `A49F671B5356383C1BA15BBCA986CA266C85E16E50B966908940156CAA2FA08F`.

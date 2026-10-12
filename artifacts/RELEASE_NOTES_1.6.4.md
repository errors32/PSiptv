# PSiptv 1.6.4

## Correções e melhorias

- **Lista Bluetooth completa reposta**: o comando remoto volta a mostrar todos os equipamentos emparelhados nas definições do Android, como na 1.6.2.
- **Removida a filtragem introduzida na 1.6.3**: os equipamentos deixam de precisar de responder a uma consulta automática para aparecerem no seletor. Esta filtragem podia deixar a lista vazia mesmo com o tablet/TV emparelhado.
- **Escolha manual do destino**: selecione o tablet ou a Android TV pretendidos. A PSiptv só consulta o estado quando escolhe o equipamento; acessórios sem PSiptv e aplicações fechadas podem aparecer na lista, mas não respondem aos comandos.
- **Mantida a correção da posição dos canais**: consultas periódicas e alterações dos canais usam a coleção existente, sem substituir a lista. Falhas temporárias conservam os canais visíveis durante a reconexão.
- Instruções, mensagens e roteiro de validação ajustados ao comportamento reposto. Controlo por Wi-Fi e sincronização Bluetooth de favoritos/podcasts mantidos.

## Como utilizar

1. Emparelhe os equipamentos nas definições Bluetooth do Android e ative **Permitir controlo por Bluetooth** no comando remoto de ambos.
2. No telemóvel, escolha **Bluetooth** e toque em **Procurar novamente**.
3. Selecione o tablet/TV com a PSiptv aberta e a mesma lista/perfil. Toque em **Ativar dispositivo selecionado**, se necessário, e escolha um canal favorito.

## Validação

- **245 verificações automatizadas**: 210 verificações do núcleo e 35 regressões/verificações adicionais. Mantêm-se os testes do protocolo Bluetooth e os cinco testes de atualização incremental dos canais; os quatro testes da filtragem removida deixam de fazer parte da suite.
- Android e Windows compilados em Release, com os avisos existentes de APIs Android obsoletas e campos não utilizados no Windows.
- O comportamento em equipamentos físicos permanece por confirmar. iOS e Mac Catalyst não foram compilados neste ambiente Windows.

## Distribuição

**PSiptv.v1.6.4.apk** mantém o pacote **com.PS.PSiptv**, inclui ARM de 32 bits, ARM64 e x64 e utiliza a mesma identidade de assinatura da 1.6.3, com código de versão Android superior. Assinatura, alinhamento e metadados verificados antes da publicação.

- Código de versão Android: **298639511**.
- SHA-256: `5A3F54590B3C371476DC75BCD11E2D7FA84573F0DCB002651E09627AD3EA77D5`.

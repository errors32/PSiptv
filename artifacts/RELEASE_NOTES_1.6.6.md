# PSiptv 1.6.6

## Alterações e melhorias

- **Bluetooth selecionado por defeito no comando remoto Android**: ao abrir o ecrã, a ligação inicial passa a ser Bluetooth. O modo Wi-Fi continua disponível no seletor.
- **Controlo Bluetooth ativo por defeito no Android quando ainda não existe uma preferência guardada**. Uma escolha anterior de desligar o controlo Bluetooth continua a ser respeitada.
- Ao abrir o comando remoto com Bluetooth ativo, a aplicação pede a permissão de dispositivos próximos quando necessária. Se a permissão for recusada, o controlo é desativado e o interruptor acompanha esse estado; pode voltar a ativá-lo manualmente.
- Mensagem inicial ajustada para não indicar uma procura Wi-Fi quando o ecrã inicia em Bluetooth. Plataformas sem suporte ao controlo Bluetooth continuam a iniciar em Wi-Fi.
- Mantidas a ligação Bluetooth persistente, a lista completa de equipamentos emparelhados e a preservação da posição dos canais.

## Utilização

Abra **Comando remoto**, autorize dispositivos próximos se solicitado e selecione o tablet/TV emparelhado. Mantenha a PSiptv aberta com controlo Bluetooth ativo e a mesma lista/perfil em ambos. Para usar a ligação persistente, atualize os dois equipamentos.

## Validação e distribuição

- **249 verificações automatizadas**: 214 verificações do núcleo e 35 regressões/verificações adicionais.
- Android e Windows compilados em Release; permanecem os avisos existentes. O fluxo de permissões e a ligação física devem ser confirmados nos equipamentos.
- **PSiptv.v1.6.6.apk** mantém o pacote **com.PS.PSiptv**, inclui ARM de 32 bits, ARM64 e x64 e usa a mesma identidade de assinatura da 1.6.5, com código de versão Android superior. Assinatura, alinhamento e metadados verificados antes da publicação.
- iOS e Mac Catalyst não foram compilados neste ambiente Windows.

- Código de versão Android: **298640261**.
- SHA-256: `6B1568C0541A620FD532487D1D718E7FF7E2131461019FE571F47D5E64717C71`.

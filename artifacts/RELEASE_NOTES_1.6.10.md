# PSiptv 1.6.10

## Transferências Bluetooth grandes e lentas

- Corrigido o limite fixo de 30 segundos que abrangia a ligação, a transferência completa e a gravação. A ligação inicial mantém um prazo independente de 30 segundos.
- A transferência tem agora um limite de 90 segundos sem progresso, renovado pelos blocos enviados/recebidos, e um limite total de 5 minutos. Os fechos provocados pelo prazo são identificados como timeout, mesmo quando o Android devolve «Broken pipe».
- Os snapshots são comprimidos com GZip e enviados/lidos em blocos até 16 KB. O limite de 8 MB também é aplicado após descompressão.
- A página mostra a fase da sincronização e os KB transferidos. Erros de ligação incluem a última fase; as alterações pendentes são conservadas.
- Mantida a sincronização apenas por marcações de ouvido/visto e a ausência de consultas ou tentativas periódicas.

## Instalação e validação

Atualize ambos os equipamentos para **1.6.10**: a compressão utiliza o protocolo Bluetooth v3. Os favoritos, marcações, emparelhamento e equipamento escolhido são mantidos.

- 289 verificações automatizadas: 214 do núcleo, 38 regressões, 24 de sincronização por consumo e 13 de transferências grandes.
- Verificados milhares de podcasts, trocas nos dois sentidos em streams lentos e fragmentados, compressão, limites por bloco, prazo renovado por progresso, bloqueios, cancelamento e limite de descompressão.
- O limite de 30 segundos existente era uma causa possível do erro comunicado; o teste físico entre o telefone e o tablet continua necessário para confirmar a resolução nesse par de equipamentos.
- Android e Windows compilados em Release sem erros; permanecem os avisos existentes.
- APK com a mesma identidade de assinatura da 1.6.9, pacote e alinhamento verificados e código de versão Android superior: **298662351**.
- SHA-256: `D1B350D4C5CE2C38B8ADC8E1D4170D4AD7EFDDDA26842B6A54F3788A489725CB`.

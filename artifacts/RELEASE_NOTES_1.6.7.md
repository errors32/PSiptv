# PSiptv 1.6.7

## Alteração

- **Design Clean com menos atalhos duplicados**: o botão de Configurações na barra superior, entre Pesquisa global e Abrir menu, fica oculto no design Clean.
- O acesso às configurações continua disponível em **Menu flutuante → Perfil e configurações**. A pesquisa global e o botão do menu mantêm-se visíveis.
- Ao mudar para outro design, o atalho de Configurações volta a aparecer; a visibilidade acompanha a alteração do design sem precisar de reiniciar a aplicação.
- Mantidos o Bluetooth por defeito no comando remoto, a ligação persistente, a lista completa de equipamentos emparelhados e a posição dos canais.

## Validação e distribuição

- **249 verificações automatizadas**: 214 verificações do núcleo e 35 regressões/verificações adicionais.
- Android e Windows compilados em Release; permanecem os avisos existentes. A disposição visual deve ser confirmada nos equipamentos.
- **PSiptv.v1.6.7.apk** mantém o pacote **com.PS.PSiptv**, inclui ARM de 32 bits, ARM64 e x64 e usa a mesma identidade de assinatura da 1.6.6, com código de versão Android superior. Assinatura, alinhamento e metadados verificados antes da publicação.
- iOS e Mac Catalyst não foram compilados neste ambiente Windows.

- Código de versão Android: **298640411**.
- SHA-256: `6768C9D3C412892249A3D3476044ECEBEC417151F3B61410337A13D20C6905AB`.

# PSiptv 1.7.15 — Bluetooth do relógio e splashscreen

- Substitui o socket RFCOMM do comando Wear OS pelo canal oficial Wear OS
  Data Layer para o telemóvel emparelhado. Evita a abertura do socket que
  originava `Null file descriptor returned`.
- O sistema usa Bluetooth quando a ligação emparelhada está disponível.
  Não depende de broadcasts, IPs ou descoberta na rede local do telemóvel,
  incluindo quando a VPN impede o comando por Wi-Fi.
- Descobre apenas telemóveis próximos com a nova versão PSiptv. Conserva a
  autenticação por perfil/token e transporta listas grandes por canal duplex.
- A sincronização Bluetooth de podcasts, favoritos e progresso mantém o serviço
  e o protocolo existentes.
- Splashscreen Android com fundo uniforme `#0F172C`, logótipo transparente,
  barras do sistema da mesma cor e cores nativas alinhadas com a aplicação.

## Atualizar

Instale a 1.7.15 **no telemóvel e no relógio**. No telemóvel use **Sobre → Instalar /
atualizar relógio**. Abra uma lista, mantenha o PSiptv aberto e o controlo
Bluetooth ativo. No relógio escolha **Bluetooth · telemóvel** e **Ligar**.
Confirme que o Watch 5 aparece ligado na aplicação Galaxy Wearable.

## Validação

394 verificações automáticas aprovadas, incluindo bootstrap e comando no mesmo
canal duplex, listas superiores a 100 KB e recusa de comandos após mudança de
perfil. Compilação Release, assinaturas e recursos dos APKs verificados.
Splashscreen observado no Android API 36, com fundo `#0F172C` confirmado nas
margens e em torno do logótipo, sem o quadrado opaco.
O teste de rádio com o Watch 5 emparelhado e a VPN real exige confirmação no
equipamento físico; o emulador disponível não reproduz esse par.

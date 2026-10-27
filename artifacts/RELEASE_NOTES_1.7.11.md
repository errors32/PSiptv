# PSiptv 1.7.11 — Android, Wear OS e Windows

- O comando **PSiptv Remote** do relógio passa a oferecer **Bluetooth e Wi-Fi**, começando em Bluetooth e memorizando a escolha.
- Bluetooth controla o volume, os canais e o dispositivo ativo através de uma ligação segura a equipamentos emparelhados, sem exigir o mesmo Wi-Fi ou guardar credenciais IPTV no relógio.
- O comando permite escolher o recetor, recorda o último equipamento que respondeu e apresenta instruções para autorização, emparelhamento e ligação.
- Corrige os APKs Wear de instalação manual para incluírem todo o código .NET, sem depender do Fast Deployment do Visual Studio.
- Adapta o comando a ecrãs pequenos/redondos com controlos deslizáveis, mensagens de estado, limites de espera e cancelamento ao sair ou trocar de ligação.
- Atualiza a aplicação de telemóvel/TV para 1.7.11, conservando o recetor Bluetooth e a compatibilidade com o comando do relógio. A distribuição Windows acompanha a mesma versão.

## Instalação

- **Telemóvel/Android TV:** `PSiptv.v1.7.11.apk`.
- **Galaxy Watch 5/Wear OS:** `PSiptv.Remote.Wear.v1.7.11.apk`. Instale este APK diretamente no relógio; instalar o APK do telemóvel no telefone não instala o comando no relógio.
- **Windows x64:** `PSiptv.v1.7.11.windows-x64.zip` e respetivo SHA-256.

Os dois APKs usam a identidade de assinatura de produção existente. Os ficheiros
`*-test.apk` anteriores eram apenas para testes e não fazem parte desta release.

No relógio, autorize **Dispositivos próximos**, emparelhe o recetor e toque em
**Ligar ao dispositivo**. No telemóvel/TV, mantenha o PSiptv aberto com uma lista
desbloqueada e **Permitir controlo por Bluetooth** ativo no ecrã de comando remoto.
Em Wi-Fi, ambos devem estar na mesma rede local.

## Validação e limites

368 verificações automáticas passaram, incluindo dez testes do protocolo Bluetooth
do relógio contra o servidor existente. O arranque, as permissões, a troca de
transporte e a opção memorizada foram validados no emulador. A ligação RFCOMM por
rádio e a apresentação no firmware Samsung ainda requerem teste num Watch 5 e
num recetor reais. Consulte `docs/wear-bluetooth.md` e `docs/wear-watch5-validation.md`.

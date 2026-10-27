# PSiptv Remote 1.7.15 — Bluetooth no telemóvel emparelhado

O modo **Bluetooth · telemóvel** usa Wear OS Data Layer (`ChannelClient`) para
comunicar com a aplicação PSiptv do telemóvel emparelhado. Quando o par está
ligado por Bluetooth, o Wear OS usa essa ligação. Não depende de broadcasts,
endereços IP ou descoberta na rede local do telemóvel.

## Utilização

1. Atualize **o telemóvel e o relógio** para a 1.7.15 ou posterior. O pacote e a
   assinatura de produção têm de coincidir nos dois equipamentos.
2. Confirme que o Watch 5 aparece ligado na aplicação Galaxy Wearable e que o
   Bluetooth está ativo nos dois equipamentos.
3. No telemóvel, mantenha uma lista aberta no PSiptv e a opção **Permitir controlo
   por Bluetooth** ativa no comando remoto.
4. No relógio, escolha **Bluetooth · telemóvel**, selecione o telemóvel e toque em
   **Ligar**. Pode consultar canais, ativar o dispositivo e alterar volume/canal.

Este modo permite controlar o telemóvel quando a VPN impede a descoberta pela
LAN. O sistema gere o transporte e pode usar outra ligação próxima disponível
quando o Bluetooth está indisponível; a aplicação não força um socket RFCOMM.
O modo **Wi-Fi** continua a descobrir equipamentos PSiptv na rede local.

A sincronização Bluetooth de podcasts, favoritos e progresso entre aplicações
Android conserva o serviço e o protocolo existentes. Não partilha o canal do
comando Wear OS. A instalação/atualização do APK do relógio continua a usar ADB
sem fios; consulte [o guia do instalador](wear-install.md).

## Implementação

O telefone anuncia `psiptv_remote_phone_v1`. O relógio procura essa capacidade
nos nós próximos da mesma aplicação e abre `/psiptv/remote/v1`. O Google Play
services exige o mesmo identificador de pacote e a mesma assinatura nos dois
lados. As versões oficiais usam a identidade de produção existente.

O canal transporta o protocolo PSiptv de negociação do perfil e autenticação
por token. Primeiro consulta o perfil atual; depois envia um único pedido no
mesmo canal. Revalida o perfil antes de executar o comando. Não repete comandos
automaticamente após falhas. Usa streams para suportar listas acima do limite
de 100 KB das mensagens individuais, com limites de tamanho e tempo.

O recetor regista os callbacks enquanto o telemóvel tem a aplicação aberta.
Sair da aplicação cancela os canais. Trocar de transporte ou sair do comando
no relógio também cancela a operação e fecha os streams. Não solicita a permissão
Bluetooth do relógio para abrir um socket direto.

## Validação

394 verificações automáticas aprovadas, incluindo quatro novas verificações
para identificação dos nós, bootstrap e pedido no mesmo canal, respostas acima
de 100 KB e recusa do comando se o perfil mudar durante a negociação. Os testes
do protocolo usam streams TCP locais como suporte duplex; não simulam rádio.
As verificações existentes de sincronização e podcasts também passaram.

Os APKs Release são compilados e assinados com a mesma chave, incluindo os
recursos da capacidade anunciada. O emulador disponível é Android API 36 de
telefone. Não havia um par Watch 5/telemóvel físico ligado para testar o rádio
com a VPN real. Confirme esse cenário no equipamento: Bluetooth ligado, Galaxy
Wearable ligada, Wi-Fi do relógio desligado e VPN do telemóvel ativa.

Referências: [visão geral e segurança Data Layer](https://developer.android.com/training/wearables/data/overview),
[clientes e transportes](https://developer.android.com/training/wearables/data/client-types)
e [ChannelClient](https://developers.google.com/android/reference/com/google/android/gms/wearable/ChannelClient).

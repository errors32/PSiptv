# PSiptv Remote 1.3.6 — Bluetooth e Wi-Fi

Estas funcionalidades, inicialmente validadas no APK de teste 1.3.6, são publicadas
com a versão oficial **1.7.11**, em conjunto com a aplicação de telemóvel/TV da
mesma versão. O APK oficial é `PSiptv.Remote.Wear.v1.7.11.apk` e usa a identidade
de produção; o APK `*-test.apk` descrito abaixo conserva a assinatura de teste.

## Utilização no Watch 5

1. Instale o APK **Wear OS 1.3.6** no relógio e abra **PSiptv Remote**.
2. Escolha **Bluetooth** e autorize o acesso a **Dispositivos próximos**.
3. Emparelhe o relógio e o telemóvel/TV nas definições Bluetooth. No recetor,
   mantenha uma lista desbloqueada, o PSiptv aberto e **Permitir controlo por
   Bluetooth** ativo no ecrã de comando remoto.
4. Escolha o recetor no relógio e toque em **Ligar ao dispositivo**. Se necessário,
   use **Ativar dispositivo** antes de alterar volume ou canal.

O mesmo Wi-Fi não é necessário em modo Bluetooth. A lista mostra os equipamentos
emparelhados; acessórios sem PSiptv não respondem. O último recetor que respondeu
é guardado para a próxima abertura. Sem resposta, o utilizador pode voltar a tentar
ou escolher outro equipamento. **Wi-Fi** continua disponível no seletor e a opção
de ligação é memorizada.

## Funcionamento

O cliente usa Bluetooth Classic RFCOMM seguro com o UUID de controlo já existente
na aplicação Android. Não altera o servidor nem a validação de perfil/token.
Uma primeira ligação obtém o hash do perfil aberto a partir da resposta de
descoberta já emitida pelo recetor quando o perfil não coincide; essa resposta
não contém token. Uma nova ligação negoceia normalmente com esse perfil e obtém
o token atual antes de transmitir o comando. O relógio não recebe nem guarda
credenciais IPTV. O bootstrap não executa comandos.

Os pedidos são serializados por ligação. Recetores que anunciam `KeepConnection`
mantêm a ligação para as consultas seguintes; recetores antigos podem fechá-la
após cada troca. Apenas uma consulta de estado pode ser repetida automaticamente
após uma ligação expirada ou perfil alterado; comandos de volume, ativação e
reprodução não são repetidos. Os prazos fecham o socket para interromper operações
Android bloqueadas. Sair, trocar de recetor ou mudar de transporte cancela os
pedidos e fecha a ligação. Um erro de reprodução não destrói uma sessão saudável.

No Android 12+, pede apenas `BLUETOOTH_CONNECT`, para consultar e contactar
equipamentos já emparelhados. Não faz pesquisa rádio nem pede localização.
Bluetooth desligado, permissão recusada e lista fechada têm mensagens próprias.
Uma recusa permanente pode ser corrigida nas definições da aplicação.

Referências: [permissões Bluetooth](https://developer.android.com/develop/connectivity/bluetooth/bt-permissions)
e [ligações RFCOMM](https://developer.android.com/develop/connectivity/bluetooth/connect-bluetooth-devices).

## Validação

- Debug e Release compilaram sem erros nem avisos, com assemblies no APK.
- Release inclui trimming e AOT e foi assinado com a chave de desenvolvimento.
- A suite produziu **368 resultados PASS**, incluindo dez novos testes de
  Bluetooth Wear. Usam TCP local apenas como suporte dos streams, contra o
  `BluetoothRemoteProtocol.ServeAsync` real: bootstrap sem comandos, estado,
  ativação, volume, canal, erros de reprodução, sessão persistente, servidor
  antigo, perfil alterado, token, limite de mensagem e cancelamento.
- A verificação do APK confirma launcher, declaração de relógio, permissão
  Bluetooth, runtimes ARM de 32/64 bits e assemblies .NET incluídas.
- No emulador, o APK Release instalou e abriu. Bluetooth foi a opção inicial,
  apresentou a indicação para ligar o rádio quando desligado e não manteve
  pedidos Wi-Fi ativos. A troca para Wi-Fi criou o pedido de rede; depois de
  terminar e reabrir a aplicação, a escolha Wi-Fi manteve-se. Regressar a
  Bluetooth removeu o pedido Wi-Fi. Os logs de crash filtrados pelos PIDs da
  aplicação ficaram vazios.
- O pedido de autorização abriu o diálogo Android de dispositivos próximos.
  Recusar mostrou a mensagem de autorização e manteve o seletor acessível.
  Evidência local: `artifacts/watch5-bt-grant.xml`, `watch5-bt-denied.xml`,
  `watch5-transport-remembered.xml` e `watch5-bt-after-request.txt`.

O emulador disponível é de telefone, API 36. O seu serviço Bluetooth apresentou
falhas e não permite validar a ligação RFCOMM por rádio. O Watch 5 e um recetor
real não estavam ligados: emparelhamento, alcance, compatibilidade Samsung e
controlo efetivo por Bluetooth continuam a exigir teste nos equipamentos.

## APK de teste

`artifacts/PSiptv.Remote.Wear.v1.3.6-test.apk`

SHA-256: `97C99485D4E4CE26D4C77A683167611D8F381F9E17179B42EAEA5199C2F4A786`.

Tem assinatura Android de desenvolvimento, como o APK de teste 1.3.5. Para
atualizar uma instalação com assinatura de produção, é necessária a chave original.
Não foi publicado em nenhuma loja ou release remota.

# Instalar e atualizar o relógio pelo telemóvel — 1.7.12

No Android, abra **Perfil → Sobre → Instalar / atualizar relógio**. O painel
descarrega o APK **Wear OS** da última release estável do repositório configurado,
valida o ficheiro e instala-o no relógio. Não exige uma instalação anterior do
PSiptv Remote. Usa o mesmo token GitHub guardado nas atualizações da aplicação.

## Primeira ligação

1. Ligue o relógio e o telemóvel à mesma rede Wi-Fi.
2. No Watch 5, ative as opções de programador em **Sobre o relógio → Informações
   de software**, tocando repetidamente em **Versão do software** até aparecer a
   confirmação. Ative **Depuração ADB** e **Depuração sem fios**. Autorize a rede
   quando o sistema o pedir.
3. Copie para o painel o IP e a **porta de ligação** do ecrã principal de depuração
   sem fios do relógio.
4. No relógio, escolha **Emparelhar novo dispositivo**. Introduza no painel a
   **porta de emparelhamento** e o **código de seis algarismos** apresentados nesse
   ecrã e toque em **Emparelhar telemóvel e relógio**.
5. Regresse ao ecrã principal de depuração sem fios do relógio e confirme a porta
   de ligação. Use **Verificar ligação e versão instalada**.
6. Toque em **Descarregar e instalar no relógio**. Mantenha o painel aberto até à
   confirmação da versão instalada. Pode desligar a depuração sem fios no fim.

O IP e a porta de ligação ficam guardados. A identidade de emparelhamento do
telemóvel fica no armazenamento seguro; o código de seis algarismos não é guardado.
A porta de ligação pode mudar quando o relógio volta a ligar ao Wi-Fi. Se revogar
a autorização no relógio ou apagar os dados do telemóvel, volte a emparelhar.

O Bluetooth usado pelo comando remoto continua disponível para volume e canais.
A instalação de APKs usa a depuração **sem fios por Wi-Fi**, autorizada pelo
utilizador. Wear OS 3 e posteriores não suportam depuração ADB por Bluetooth.
Não exige um computador nem uma aplicação instaladora de terceiros.

## Verificações e erros

- Cada instalação consulta novamente a última release. Seleciona exatamente
  `PSiptv.Remote.Wear.v{versão}.apk`, mesmo quando o telemóvel já tem essa versão.
  Não usa o APK de telemóvel como alternativa.
- Exige um asset publicado, tamanho válido e SHA-256. Confirma o tamanho recebido,
  o hash, o identificador do pacote, a funcionalidade de relógio e a assinatura
  igual à da aplicação instalada no telemóvel.
- Antes de enviar, confirma que o destino anuncia `android.hardware.type.watch`.
  Um telemóvel/TV indicado por engano é recusado. Endereços e portas são validados;
  não entram em comandos shell.
- Consulta o `versionCode` no relógio e dispensa a instalação se já tiver o build
  pretendido ou um superior. Não força downgrades nem desinstala aplicações.
- A transferência mostra bytes enviados. O estado final só é apresentado após a
  confirmação do gestor de pacotes e uma nova consulta da versão no relógio.
- Cancelar ou sair do painel interrompe as operações. Se cancelar durante a fase
  final, consulte a versão antes de repetir: o gestor de pacotes pode já ter
  concluído a instalação.

Uma instalação de teste com assinatura Android Debug não pode receber uma
atualização de produção com assinatura diferente. O painel apresenta a recusa do
gestor de pacotes e não apaga os dados para tentar contorná-la. Falta de espaço,
portas trocadas, código expirado, isolamento entre clientes Wi-Fi ou depuração
desligada também podem impedir a instalação.

## Implementação e validação

O telefone usa libadb-android 3.1.1, spake2-android 2.2.1, Bouncy Castle 1.81 e
Conscrypt Android 2.5.3, com versões fixas. A adaptação local de
`WatchPairingConnectionCtx` conserva o protocolo de emparelhamento do projeto
original e acrescenta deadlines de rede, fecho cancelável e limpeza segura quando
a ligação falha antes de criar os streams. A identidade RSA/X.509 é criada no
telemóvel, separada da chave de assinatura dos APKs e das credenciais IPTV.

Os comandos usam ADB shell-v2, incluindo frames de entrada e a resposta explícita
de saída. Isto evita depender do EOF de um canal para confirmar uma operação.
Os únicos comandos de produção são leitura de funcionalidades, consulta do
pacote PSiptv e instalação por streaming do APK validado.

A suite automática produziu **390 resultados PASS**. No emulador API 36, a
instrumentação Debug validou o emparelhamento TLS real com o código Android,
cancelamento durante uma ligação TLS bloqueada e envio por vários blocos de um
APK de teste de aproximadamente 2 MB. O gestor de pacotes confirmou a instalação
e o pacote apareceu no dispositivo. A instrumentação é opcional, apenas em
Debug, e não é incluída nos APKs de produção.

O APK Release também abriu e permitiu navegar de Sobre para o novo painel. Foram
verificados os campos, o estado de endereço em falta e a criação da identidade
segura seguida de uma falha de rede recuperável. O processo manteve-se ativo sem
erros nos logs de crash filtrados pelo PID. Os APKs finais foram verificados sem
instrumentação de teste e sem a flag `debuggable`.

Para repetir a instrumentação, compile Debug com `WearInstallerSmoke=true` e um
manifesto de teste que declare `com.ps.psiptv.tests.WearInstallerSmoke` como
instrumentation para `com.PS.PSiptv`. O runner recebe `mode=cancel`, `pair` ou
`install`, uma identidade RSA PKCS#8/DER temporária em `key`/`certificate` e, nos
últimos modos, a porta e o código/ficheiro APK. Use um APK de teste com pacote
diferente de PSiptv para não substituir o próprio runner durante a instalação.

Não havia um Watch 5 físico ligado: a experiência de emparelhamento específica do
firmware Samsung, a instalação do APK Wear completo por rádio e redes domésticas
com isolamento continuam a exigir confirmação no equipamento real.

Referências: [depuração Wear OS](https://developer.android.com/training/wearables/get-started/debugging),
[libadb-android](https://github.com/MuntashirAkon/libadb-android/tree/3.1.1) e
[protocolo shell ADB](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/shell_protocol.h).

Os avisos das bibliotecas são incluídos no APK em `wear-installer-notices.txt`.
O spake2-java é LGPL-3.0: [fonte da versão usada](https://github.com/MuntashirAkon/spake2-java/tree/2.2.1).
As dependências podem ser substituídas nas referências Maven do projeto e o APK
reconstruído. A adaptação do emparelhamento parte de
[PairingConnectionCtx 3.1.1](https://github.com/MuntashirAkon/libadb-android/blob/3.1.1/libadb/src/main/java/io/github/muntashirakon/adb/PairingConnectionCtx.java)
(SHA-256 da origem: `DD5F1F6086E166B4FC95684A6A3F67E8732FB101DCF9631164741D0BD58861A1`).

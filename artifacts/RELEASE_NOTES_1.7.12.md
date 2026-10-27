# PSiptv 1.7.12 — Instalar o relógio pelo telemóvel

- Novo painel **Sobre → Instalar / atualizar relógio** na aplicação Android.
- O telemóvel consulta a última release, descarrega o APK Wear OS correto e
  instala-o diretamente no relógio, tanto na primeira instalação como numa
  atualização. Usa o token GitHub já configurado na aplicação.
- Emparelhamento por código, IP/porta guardados, consulta da versão instalada,
  progresso do download e envio, cancelamento e confirmação final no relógio.
- Valida SHA-256, tamanho, pacote, assinatura e tipo de dispositivo; recusa
  telemóveis/TV como destino e não força downgrades ou desinstalações.
- Pacotes de telemóvel/TV, Wear OS e Windows atualizados para 1.7.12, com os APKs
  assinados pela identidade de produção existente.

## Preparação no Watch 5

Ligue ambos os equipamentos ao mesmo Wi-Fi. Ative **Depuração ADB** e **Depuração
sem fios** nas opções de programador do relógio. Na primeira utilização, escolha
**Emparelhar novo dispositivo** e introduza o código e a porta no painel do
telemóvel. A porta de emparelhamento é diferente da porta de ligação apresentada
no ecrã principal. Depois da instalação, pode desligar a depuração sem fios.

O controlo remoto continua a permitir Bluetooth ou Wi-Fi. A instalação usa Wi-Fi
com a autorização de depuração do relógio; não exige um computador.

## Pacotes

- **Telemóvel / Android TV:** `PSiptv.v1.7.12.apk`.
- **Galaxy Watch 5 / Wear OS:** `PSiptv.Remote.Wear.v1.7.12.apk`.
- **Windows x64:** `PSiptv.v1.7.12.windows-x64.zip` e respetivo SHA-256.

## Validação

390 verificações automáticas aprovadas. Num emulador API 36, passaram o
emparelhamento TLS com o serviço Android real, o cancelamento de uma ligação
bloqueada e o envio/instalação de um APK de teste em vários blocos. Não havia um
Watch 5 físico ligado; a confirmação no firmware Samsung e numa rede doméstica
real permanece necessária. Consulte `docs/wear-install.md` para instruções,
verificações e limitações.

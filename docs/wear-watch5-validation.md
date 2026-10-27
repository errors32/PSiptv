# PSiptv Remote 1.3.5 — Galaxy Watch 5

## Diagnóstico e alterações

O manifesto gerado já tinha uma atividade exportada com `MAIN`/`LAUNCHER` e a
funcionalidade `android.hardware.type.watch`. Não faltava uma atividade de arranque.
Contudo, o APK Debug inicial continha os runtimes nativos sem assemblies .NET:
dependia do Fast Deployment e não era um pacote autónomo para instalação manual.
Isto explica uma possível falha de arranque após copiar apenas o APK; não foi
possível recolher os logs da instalação original no relógio do utilizador.

- O APK passa a incluir as assemblies em `lib/<abi>/libassembly-store.so`, tanto
  em Debug como em Release, incluindo ARM de 32 e 64 bits.
- O ecrã passa a usar uma lista com cabeçalho deslizável e margens específicas para
  ecrãs redondos. Os controlos não retiram todo o espaço disponível aos canais.
- Sem recetores, mostra instruções e um botão para abrir as definições Wi-Fi.
- Pede e usa uma rede Wi-Fi direta enquanto a atividade está visível; ao sair,
  cancela os pedidos, desassocia a rede e liberta o callback. O encaminhamento
  habitual por Bluetooth do Wear OS pode não alcançar os broadcasts da LAN.
- A serialização usa código gerado para continuar a funcionar após trimming.
- Os comandos têm um prazo de cinco segundos. Uma falha na consulta de estado
  permite voltar à descoberta, em vez de ficar indefinidamente num recetor perdido.
- Os recetores ativos aparecem primeiro; um recetor inativo pode ser ativado
  explicitamente antes de enviar volume ou canal.

Referências: [Fast Deployment em .NET Android](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties#androidenablefastdeployment),
[rede no Wear OS](https://developer.android.com/training/wearables/data/network-communication)
e [serialização em aplicações com trimming](https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/8.0/publishtrimmed).

## Verificações automáticas

- `dotnet build PSiptv.Wear/PSiptv.Wear.csproj -c Debug`: sem erros nem avisos.
- Build Release com trimming e AOT, assinado apenas com a chave Android de
  desenvolvimento para validação: sem erros nem avisos.
- `dotnet run --project PSiptv.Tests/PSiptv.Tests.csproj -c Release`: 358 resultados
  `PASS`, incluindo sete verificações do cliente Wear. Exercitam o contrato JSON,
  compatibilidade de valores predefinidos, comandos e token por TCP local,
  rejeição do recetor, timeout e cancelamento ao sair.
- `scripts/Test-WearPackage.ps1` passou nos APKs Debug e Release: valida o
  identificador, o launcher, a funcionalidade de relógio, ambos os runtimes ARM e
  as assemblies incluídas no APK. A CI passa a compilar e verificar também o Wear.

## APK para teste

`artifacts/PSiptv.Remote.Wear.v1.3.5-test.apk` é o APK **Wear OS**, em configuração
Release, com assinatura de desenvolvimento. Não foi publicado. Para atualizar uma
instalação com assinatura de produção, é necessário assinar com a chave original.
O APK de telemóvel e o de relógio partilham o identificador `com.PS.PSiptv`, mas não
são intercambiáveis. Consulte as instruções de instalação no README.

SHA-256: `AA12B62CDC7AC05389303221B26A291D3B5C846EC3A7FA68B7E07AFE65D11CF9`.

## Arranque no emulador

O APK Release foi instalado por ADB num emulador Android API 36 com dados novos,
sem Visual Studio nem ficheiros Fast Deployment. O gestor de pacotes resolveu a
atividade `com.PS.PSiptv/crc6493ae4c805b85dde3.MainActivity` com `MAIN`/`LAUNCHER`.
O arranque explícito dessa atividade devolveu `Status: ok`; o processo continuou
ativo e apresentou a interface. Os logs de crash filtrados pelo PID da aplicação
ficaram vazios, incluindo depois de sair para as definições Wi-Fi e regressar.

A interface foi inspecionada a 396 × 396 pixels e densidade 320 (198 × 198 dp).
Este é um emulador de telefone com dimensões alteradas, não um emulador Wear OS;
não valida o launcher Samsung nem a configuração de ecrã redondo. O launcher do
próprio emulador de telefone falhou ao usar estas dimensões muito pequenas; o
processo PSiptv Remote manteve-se ativo.

O `dumpsys connectivity` mostrou o pedido Wi-Fi da aplicação enquanto visível e
a sua ausência na secção de pedidos ativos depois de abrir as definições. O
regresso à atividade terminou com `Status: ok` e o mesmo PID.

Capturas locais: `artifacts/watch5-release-screen.png` e
`artifacts/watch5-release-screen-small.png`. Os testes TCP usam um recetor local
simulado; não representam uma sessão completa com um telemóvel ou TV real.

## Limites da validação

Não havia um Galaxy Watch 5 ligado por ADB. A apresentação num ecrã fisicamente
redondo, o pedido de Wi-Fi direto no firmware Samsung e a descoberta/controlo de
um telemóvel ou TV na rede real precisam de confirmação no equipamento.

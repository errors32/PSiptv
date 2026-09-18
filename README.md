# PSiptv

Leitor IPTV em português, desenvolvido em .NET 10 / MAUI. Não inclui listas nem conteúdos pré-carregados.

## Funcionalidades

- Contas Xtream Codes: servidor, utilizador e palavra-passe, com validação de autenticação e estado da conta.
- Listas M3U por HTTP/HTTPS: nomes, logótipos, categorias, `tvg-id`, `group-title`, `EXTGRP`, endereços relativos e fonte XMLTV no cabeçalho.
- **Fontes adicionais**: ficheiros M3U locais, Stalker Portal por endereço MAC, Jellyfin e Plex por token, Tvheadend com autenticação Basic opcional e HDHomeRun por descoberta do alinhamento do dispositivo. As ligações são validadas antes de guardar; streams Stalker temporárias são resolvidas apenas no momento da reprodução.
- **Página inicial inteligente**: a nova primeira tab reúne continuar a ver com retoma, canais recentes, favoritos, programas a começar nos próximos 90 minutos e gravações recentes. O conteúdo é personalizado por lista e perfil e atualizado ao regressar à página.
- **TV ao Vivo**: cartões grandes com logótipos e favoritos, filtrados pelo grupo escolhido; tocar num canal inicia o preview integrado. Em horizontal, o vídeo fica ao lado dos canais. Maximizar esconde os menus e força landscape no Android; voltar restaura a orientação anterior.
- **Multiview**: na TV ao Vivo, abra um mosaico 2×2 para acompanhar até quatro canais da categoria em simultâneo. Pode pesquisar, adicionar, substituir ou remover canais; tocar num painel transfere o áudio para esse canal e mantém os restantes sem som. A quantidade de streams que funciona em simultâneo depende do dispositivo e do fornecedor.
- **Chromecast avançado**: no Android, o envio direto abre um controlador do recetor com estado e título atuais, reprodução/pausa, saltos de 30 segundos, progresso, volume, silêncio, paragem e desconexão. Filmes e episódios enviam uma fila nativa de até 100 conteúdos, com anterior/seguinte e reprodução automática no Chromecast.
- **Reprodução a partir do browser**: o browser interno deteta elementos de vídeo e streams HLS/DASH carregadas pela página. Quando encontra uma transmissão HTTP/HTTPS reproduzível, ativa o botão de reprodução para a abrir no leitor integrado com os controlos, proporção, áudio, legendas, ecrã inteiro e partilha já existentes.
- **Favoritos**: use ☆/★ junto a canais, filmes, séries e episódios (ou no leitor integrado) para guardar/remover. Abra **⋯ → Favoritos** para pesquisar e filtrar por tipo. Persistem por lista no armazenamento seguro, respeitam o PIN e são eliminados ao apagar a lista. A identidade M3U usa o endereço do stream para resistir à reordenação da lista; Xtream usa o ID e tipo do conteúdo.
- **Perfis de utilizador**: o perfil Principal mantém automaticamente os favoritos e o histórico já existentes. É possível criar até oito perfis locais, editar o nome e o ícone de qualquer perfil e alternar entre eles em Perfil e configurações → Perfis de utilizador. Cada perfil tem favoritos e histórico independentes; as listas IPTV e os catálogos permanecem partilhados.
- **Sincronização e cópia de segurança**: exporta listas, perfis, favoritos, histórico, personalização de categorias, fontes do browser e configurações para um ficheiro `.psiptvbackup` JSON sem palavra-passe ou encriptação. O seletor de partilha permite guardá-lo num serviço cloud ou enviá-lo para outro dispositivo; a reposição une os dados da cópia aos dados existentes. Os catálogos descarregáveis não são incluídos. Como o ficheiro inclui as credenciais das listas em texto legível, deve ser guardado num local privado.
- **Atualização automática das listas**: nas configurações gerais pode manter o modo Manual (predefinido), atualizar ao arrancar, diariamente ou semanalmente, limitar as transferências a Wi-Fi e permitir a execução em segundo plano. Com esta opção ativa, o arranque deixa a interface disponível enquanto Início e Favoritos são preparados e atualizados assincronamente. O Android usa uma tarefa persistente do sistema; a interface indica quantos canais foram adicionados e removidos. O botão ↻ continua disponível para atualizações manuais.
- **Guia TV**: selecione um canal para consultar os programas e iniciar a reprodução. Xtream usa `get_short_epg`; M3U usa XMLTV e associa `tvg-id` ao identificador de canal. Horas apresentadas no fuso local.
- **Guia TV em grelha**: na linha da categoria da TV ao Vivo, alterne entre a vista simples de cartões e uma grelha temporal por canal. A grelha mostra a programação atual e seguinte, mantém todas as linhas sincronizadas ao deslocar horizontalmente e limita a quatro os pedidos EPG simultâneos.
- **Pesquisa global**: o botão de pesquisa abre resultados unificados da lista ativa para canais, filmes, séries, favoritos e programas EPG. Os resultados permitem iniciar o direto/VOD, abrir uma série, consultar um programa futuro ou reproduzir catch-up disponível.
- **Catch-up TV**: programas já terminados podem ser reproduzidos diretamente no guia quando o canal Xtream declara `tv_archive` ou uma lista M3U fornece `catchup`, `catchup-days` e `catchup-source`. A disponibilidade e a duração do arquivo dependem do fornecedor.
- **Reconexão e diagnóstico do stream**: o leitor identifica erros e períodos de 10 segundos sem receber dados, apresenta o estado da ligação e realiza até três tentativas automáticas com esperas de 1, 2 e 4 segundos. Canais regressam ao direto; filmes e episódios retomam a última posição válida. Trocar de conteúdo ou sair do leitor cancela imediatamente a recuperação pendente.
- **Preferências avançadas de áudio e legendas**: no leitor Android pode definir idiomas preferidos, mostrar legendas sempre ou apenas quando o áudio estiver noutro idioma, memorizar faixas escolhidas e corrigir a sincronização com atrasos de áudio e legendas entre −5000 e +5000 ms. Durante a reprodução, os botões Áudio e Legendas permitem trocar imediatamente entre as faixas disponíveis.
- **Legendas externas**: durante a reprodução de VOD pode carregar um ficheiro local SRT/WebVTT ou indicar um URL HTTP/HTTPS. A aplicação interpreta tempos, texto multilinha, etiquetas e entidades, desenha a faixa sobre o vídeo em Android e Windows, respeita o atraso de legendas configurado e permite desligá-la ou voltar a ligá-la sem reiniciar o vídeo.
- **Lembretes de programas**: os programas futuros do guia podem ser marcados por perfil, com aviso à hora de início ou 5, 10, 15 ou 30 minutos antes. O ecrã de lembretes permite consultar e remover os avisos guardados. Android agenda notificações do sistema e repõe os alarmes após reiniciar o dispositivo; nas restantes plataformas, os avisos são apresentados enquanto a aplicação estiver em execução.
- **Gravação e agendamento DVR**: grave imediatamente um canal durante 30, 60 ou 120 minutos, ou agende qualquer programa atual/futuro a partir do guia. As gravações ficam no armazenamento privado da aplicação, separadas por perfil, e podem ser reproduzidas, interrompidas ou eliminadas em **Configurações → Gravações DVR**. Streams MPEG-TS e HLS não cifrado são suportados; no Android, um serviço em primeiro plano mantém a gravação ativa e os agendamentos são repostos após reiniciar.
- **Gravação recorrente de séries**: no guia, use **Gravar série** para criar uma regra por texto do título, com canal e horário opcionais. A aplicação pesquisa os oito dias seguintes do EPG ao criar a regra, no arranque e periodicamente, agenda novos episódios e evita repetições através da identidade do episódio. As regras podem ser ativadas, editadas, eliminadas e incluídas na cópia de segurança; eliminar uma regra preserva os agendamentos já criados.
- **Diagnóstico de fontes**: testa a ligação e autenticação, o catálogo de canais, o guia EPG e o endereço de uma stream de amostra. Apresenta o resultado e a duração de cada teste, distingue falhas de avisos opcionais e permite copiar um relatório sem credenciais, tokens ou parâmetros privados.
- **Downloads offline de VOD**: descarrega filmes e episódios para o armazenamento privado da aplicação, com fila de até dois downloads simultâneos, progresso, pausa, retoma por pedidos HTTP parciais, reprodução local e gestão por perfil. Downloads interrompidos ficam disponíveis para retoma e são removidos com a lista ou o perfil associado.
- **Experiência dedicada a Android TV**: a aplicação deteta televisões e dispositivos Leanback e aplica um layout mais compacto sem afetar telemóveis ou tablets. Títulos, barras de categorias, navegação inferior, cartões de canais e submenus ocupam menos altura; os ícones passam a ficar ao lado dos nomes onde isso poupa espaço, mantendo alvos de foco visíveis e adequados ao comando remoto.
- **Timeshift**: no leitor Android, os canais em direto podem ser pausados e retomados, recuados ou avançados em intervalos de 30 segundos e devolvidos imediatamente ao direto. O buffer é temporário, tem uma janela configurável entre 5 e 120 minutos e é limpo automaticamente; a capacidade efetiva de pausa e recuo depende do formato disponibilizado pelo fornecedor.
- **VOD**: filmes e séries separados; Xtream carrega episódios ordenados por temporada. Nas M3U, a classificação usa o grupo ou o caminho/extensão do conteúdo; episódios são entradas individuais.
- **Detalhes de filmes e séries**: conteúdos Xtream apresentam imagem de destaque, sinopse, data, género, duração, classificação, elenco, realização e trailer quando fornecidos. Filmes podem ser reproduzidos a partir da página; séries reutilizam os episódios recebidos com os detalhes, evitando um segundo pedido.
- **Perfil → Configurações**: perfis Escuro, Claro e Do sistema (acompanha a aparência do dispositivo), cinco cores de destaque, adicionar/editar/eliminar listas e definir/alterar/remover PIN.
- **Leitores externos**: botão no leitor integrado e preferência nas definições, aplicável a canais, filmes e episódios. Windows apresenta «Abrir com» para uma pequena lista M3U; Android apresenta os leitores de vídeo compatíveis instalados. iOS usa VLC; Mac Catalyst usa a aplicação associada a M3U.
- **Desbloqueio biométrico por lista**: Android 9+ usa o diálogo biométrico nativo; iPhone/iPad usam Face ID ou Touch ID conforme o dispositivo. No ecrã de desbloqueio, ative «Permitir biometria neste dispositivo», confirme o PIN e a biometria. Nos acessos seguintes, escolha biometria ou PIN. Cancelamento, indisponibilidade ou falha mantêm o PIN disponível. A autorização biométrica fica associada ao PIN atual; alterar o PIN ou eliminar a lista revoga-a. Windows e dispositivos sem biometria continuam a usar PIN.
- **Última lista**: abre automaticamente a última lista aberta com sucesso ao iniciar a aplicação. Listas protegidas continuam a exigir PIN ou biometria. Pode desligar em Perfil → Configurações → Arranque e segurança. Cancelar o desbloqueio não volta a abrir o pedido em ciclo; bloquear manualmente não desbloqueia a lista automaticamente.
- **Identidade visual**: TV retro com madeira e azulejo português, cujo ecrã transita do ruído analógico para uma imagem digital luminosa. O mesmo desenho é usado no logótipo, ícone e splash nativo.
- Credenciais e listas persistidas com MAUI `SecureStorage`. PIN de 4–8 algarismos, derivado com PBKDF2-SHA256 e sal aleatório; o PIN não é guardado em texto simples. Após cinco erros, espera de 30 segundos (contador em memória).
- Bloqueio manual ou ao passar para segundo plano: limpa o catálogo, termina a reprodução e fecha páginas de conteúdo/credenciais. Para editar ou eliminar uma lista protegida, é necessário o PIN atual.
- Cancelamento de pedidos obsoletos, limite de 40 MB por resposta e tempo máximo de 60 segundos por pedido.

A navegação inferior apresenta **Início**, **TV ao Vivo**, **Filmes/Séries**, **Favoritos** e **Browser**. No topo estão **Partilhar ecrã**, **Pesquisar** e **Perfil e configurações**. A pesquisa filtra a secção e o grupo atuais. O perfil apresenta a lista ativa e cartões para Configurações, Atualizar conteúdo, Trocar playlist, Adicionar lista, Guia TV, Favoritos e Bloquear; a escolha de tema e de cor de destaque fica guardada.

A partilha abre os controlos de transmissão do Android ou de ligação a um ecrã sem fios no Windows; em Apple, indica como abrir a duplicação de ecrã/AirPlay. No Android, o Chromecast pode receber diretamente a stream quando o formato e o fornecedor o permitem e apresenta o controlador avançado após estabelecer a sessão. Ao sair para as definições do sistema, aplica-se o bloqueio de segurança existente; volte a abrir a lista ao regressar.

## Executar

Requer SDK .NET 10 e as workloads MAUI da plataforma. Android requer versão 8.0/API 26 ou superior, conforme o pacote multimédia. O projeto fixa `Microsoft.Maui.Controls` em 10.0.60 e `CommunityToolkit.Maui.MediaElement` em 10.0.0.

No Visual Studio, abra `PSiptv.slnx`, escolha o projeto **PSiptv** e o destino **Windows Machine** ou um dispositivo/emulador Android.

```powershell
dotnet build PSiptv/PSiptv.csproj -f net10.0-windows10.0.19041.0
dotnet build PSiptv/PSiptv.csproj -f net10.0-android
```

Para distribuir uma atualização Android, use sempre a mesma chave de assinatura da
versão já instalada e aumente `ApplicationVersion`. A compilação Release produz um
APK instalável e recusa gerar um pacote sem assinatura. Configure a chave fora do
repositório antes de publicar:

```powershell
$env:PSIPTV_ANDROID_KEYSTORE = "C:\caminho\psiptv.keystore"
$env:PSIPTV_ANDROID_KEY_ALIAS = "psiptv"
$env:PSIPTV_ANDROID_KEY_PASSWORD = "..."
$env:PSIPTV_ANDROID_STORE_PASSWORD = "..."
dotnet publish PSiptv/PSiptv.csproj -c Release -f net10.0-android
```

O ficheiro a instalar é `com.PS.PSiptv-Signed.apk`; um `.aab` não pode
ser aberto diretamente no telemóvel ou Android TV. Uma instalação existente só
aceita a atualização quando o identificador do pacote e a assinatura coincidem e o
novo `ApplicationVersion` é superior.

### Atualizações Android através de GitHub Releases

No Android, **Configurações → Sobre → Atualizações da aplicação** verifica a
última Release publicada, descarrega o APK privado com progresso, valida o
tamanho, o digest SHA-256 (quando fornecido pelo GitHub), o identificador do
pacote e o `versionCode`, e abre o instalador do sistema. A verificação
automática ocorre no máximo uma vez por dia e apenas depois de configurar o
acesso ao repositório privado.

Crie para cada dispositivo/utilizador um fine-grained personal access token
limitado ao repositório, com a permissão **Contents: Read-only**, e introduza-o
nessa página. O token é guardado pelo `SecureStorage` do dispositivo; nunca o
inclua no projeto nem no APK. A Release deve usar uma tag de versão como
`1.3.0` ou `v1.3.0` e conter, por predefinição, o asset
`PSiptv.v1.3.0.apk`. Se existir apenas um asset `.apk`, esse ficheiro também é
aceite para manter compatibilidade com Releases antigas.

Os valores predefinidos vêm do repositório atual e podem ser substituídos no
build sem alterar código:

```powershell
dotnet publish PSiptv/PSiptv.csproj -c Release -f net10.0-android `
  -p:GitHubReleaseOwner=errors32 `
  -p:GitHubReleaseRepository=PSiptv `
  -p:GitHubReleaseAssetName="PSiptv.v{version}.apk"
```

Na primeira instalação, o Android pedirá autorização para esta aplicação
instalar APKs. A atualização mantém os dados se conservar `ApplicationId`, a
mesma chave de assinatura e um `ApplicationVersion` superior. A funcionalidade
e a permissão de instalação existem apenas no target Android.

iOS e Mac Catalyst requerem um Mac com Xcode e configuração de assinatura apropriada; não foram validados neste ambiente Windows.

### Erro Android: «No view found … leftToRight»

Se aparecer este erro no arranque, os recursos Java do APK e os IDs usados pelo código .NET podem pertencer a builds diferentes. Pare a depuração, limpe os diretórios gerados `PSiptv/bin/Debug/net10.0-android` e `PSiptv/obj/Debug/net10.0-android`, e volte a compilar e instalar. Não é necessário eliminar os dados da aplicação. O build Android Debug inclui as assemblies no APK (`EmbedAssembliesIntoApk=true`) para manter o código e os recursos na mesma instalação, sem Fast Deployment de assemblies.
## Primeira utilização

1. Prima **⋯ → Adicionar lista** no topo.
2. Dê um nome e escolha o tipo de fonte: **Xtream Codes**, **Link M3U**, **Ficheiro M3U local**, **Stalker Portal**, **Jellyfin**, **Plex**, **Tvheadend** ou **HDHomeRun**.
3. Indique os dados pedidos pelo tipo escolhido: credenciais Xtream, endereço MAC Stalker, token Jellyfin/Plex, credenciais Basic opcionais do Tvheadend, ou apenas o endereço base para HDHomeRun. Para M3U local, escolha um ficheiro `.m3u` ou `.m3u8`; a aplicação guarda uma cópia privada.
4. Se necessário, indique uma fonte **XMLTV**. A fonte explícita tem prioridade sobre a fonte do fornecedor/cabeçalho M3U.
5. Opcionalmente defina e confirme um PIN. Prima **Testar ligação e guardar**.
6. No ecrã principal, prima **⋯ → Abrir lista** e escolha a lista guardada.
7. Para usar outro leitor, prima **Abrir com leitor externo** durante a reprodução, ou ative **Definições → Reprodução → Preferir leitor externo**. Instale primeiro um leitor compatível, por exemplo VLC. Em Windows, selecione-o no diálogo do sistema.

## Testes

O projeto de testes é um executável sem dependências adicionais. Usa respostas HTTP simuladas e termina com erro se qualquer verificação falhar.

```powershell
dotnet run --project PSiptv.Tests/PSiptv.Tests.csproj
```

Valida o parsing M3U/XMLTV e XMLTV GZip, classificação e categorias, URLs relativos e ficheiros locais, fusos horários, proteção do PIN, autenticação Xtream, Stalker, Jellyfin, Plex, Tvheadend e HDHomeRun, temporadas, EPG, regras DVR e cancelamento.

Validação realizada: 107 verificações passaram; Windows e Android compilaram sem erros. O executável Windows iniciou e permaneceu ativo no teste de arranque. A reprodução, o Multiview, o Chromecast num recetor real, as legendas externas sobre vídeo real, a deteção de streams em sites reais, os downloads e a gravação, bem como a abertura efetiva num leitor externo instalado, ainda requerem teste com uma fonte real. Os destinos Apple não foram compilados neste ambiente.

## Estrutura

- `PSiptv.Core`: modelos, clientes Xtream/M3U/Stalker/Jellyfin/Plex/Tvheadend/HDHomeRun, XMLTV e PIN, sem dependência de MAUI.
- `PSiptv/Services`: armazenamento seguro, sessão e tema.
- `PSiptv/Views`: contas, definições, PIN, guia, episódios e leitor.
- `PSiptv/MainPage.xaml.cs`: navegação e catálogo com lista virtualizada.
- `PSiptv.Tests`: verificações locais com dados simulados.

## Limitações atuais

- A disponibilidade de categorias, programação e conteúdos depende do fornecedor. O guia Xtream apresenta os programas devolvidos pelo endpoint de EPG curto.
- A reprodução usa o leitor nativo através de [MediaElement](https://learn.microsoft.com/dotnet/communitytoolkit/maui/views/mediaelement); codecs e formatos suportados variam por sistema operativo. Não inclui transcodificação, DRM nem cabeçalhos HTTP personalizados para streams.
- A reprodução externa entrega o endereço do stream (que pode conter credenciais) ao leitor escolhido. Windows/Mac criam uma lista M3U temporária na cache; ficheiros com mais de 24 horas são removidos na próxima inicialização. O conteúdo de vídeo não é descarregado. A compatibilidade e a reprodução após sair do PSiptv passam a ser controladas pelo leitor externo.
- XMLTV pode ser XML simples ou comprimido em GZip (`.gz`), até 40 MB depois de descomprimido. Não há descarga de conteúdos de vídeo.
- Os ficheiros de vídeo das gravações DVR ocupam o armazenamento interno da aplicação e não são incluídos na cópia de segurança. HLS cifrado/DRM não pode ser gravado; nas plataformas não Android, a aplicação tem de permanecer em execução até ao fim do agendamento.
- Os downloads offline ocupam o armazenamento interno da aplicação, não são incluídos na cópia de segurança e dependem de o fornecedor aceitar pedidos HTTP de retoma. VOD em HLS segmentado e conteúdo com DRM não são descarregados nesta versão.
- As regras recorrentes dependem da qualidade e da antecedência do EPG. A pesquisa automática agenda programas encontrados nos oito dias seguintes; as gravações de vídeo continuam excluídas da cópia de segurança.
- Ligações HTTP são permitidas para compatibilidade com fornecedores IPTV; quando disponível, utilize HTTPS.
- O PIN controla o acesso dentro da aplicação. Não existe recuperação de PIN; mantenha-o num local seguro.
- As integrações foram testadas com dados simulados. É necessário verificar reprodução e EPG com a conta real e no dispositivo de destino.






# PSiptv

Leitor IPTV em português, desenvolvido em .NET 10 / MAUI. Não inclui listas nem conteúdos pré-carregados.

## Comando remoto por Bluetooth (Android)

Desde a 1.6.6, o comando remoto inicia em Bluetooth no Android. O controlo Bluetooth fica ativo por defeito quando ainda não existe uma preferência guardada; uma opção anterior de desligar é respeitada. Ao abrir o comando, autorize o acesso a dispositivos próximos quando solicitado. Pode escolher Wi-Fi no seletor de ligação.

O telemóvel pode mudar os canais favoritos e ajustar o volume no tablet ou numa Android TV através de Bluetooth, mesmo que estejam em redes Wi-Fi diferentes. Instale esta versão da PSiptv nos dois equipamentos, emparelhe-os nas definições Bluetooth do Android, abra a mesma lista com o mesmo nome de perfil e ative **Permitir controlo por Bluetooth** no ecrã **Comando remoto** em ambos.

No telemóvel, escolha **Bluetooth** em **Ligação**, selecione o tablet/TV e toque em **Ativar dispositivo selecionado** se necessário. Escolha um canal favorito para o reproduzir no equipamento selecionado. **Procurar novamente** mostra todos os equipamentos emparelhados no Android, sem exigir uma resposta prévia da PSiptv. Escolha o tablet/TV pretendido; acessórios sem PSiptv ou aplicações fechadas não respondem aos comandos. O modo **Wi-Fi** continua disponível. As consultas de estado preservam a posição da lista de canais, incluindo durante falhas temporárias da ligação.

As aplicações devem permanecer abertas e os equipamentos dentro do alcance Bluetooth. A TV deve suportar ligações Bluetooth Classic RFCOMM entre aplicações. O vídeo continua a ser obtido pela ligação à Internet do tablet/TV; Bluetooth transporta os comandos e o estado da reprodução.

A partir da 1.6.5, atualize ambos os equipamentos para manter uma ligação Bluetooth entre as consultas e os comandos. Recetores anteriores continuam a usar uma ligação por pedido. Fechar o comando remoto ou trocar de equipamento encerra a ligação; consultas de estado podem reconectar após falha, sem repetir automaticamente comandos de reprodução.

## Podcasts e favoritos na rede local

O botão **Podcasts**, junto ao seletor de TV/Rádios, abre a pesquisa no diretório iTunes e os episódios dos feeds RSS. Use a estrela para guardar um podcast ou um episódio no perfil ativo. Em **Configurações → Podcasts favoritos e ouvidos** pode consultar os favoritos e, dentro de cada podcast, filtrar episódios **Por ouvir** ou **Ouvidos**, ou alterar esse estado manualmente. O leitor integrado marca o episódio como ouvido quando chega ao fim; leitores externos não comunicam a conclusão à aplicação. O estado só é conservado enquanto o podcast ou o episódio estiver nos favoritos e acompanha as cópias de segurança.

Na primeira abertura de uma lista desbloqueada após iniciar a aplicação, a PSiptv procura outras instâncias abertas na mesma rede local, com a mesma ligação à lista e o mesmo nome de perfil. Com esta versão nos dois equipamentos, combina alterações individuais de favoritos, estados ouvidos e progresso, conservando remoções para não recuperar dados apagados. Alterações independentes são preservadas; conflitos no mesmo item usam a data e um desempate determinístico. Versões antigas mantêm o comportamento de substituição pela cópia mais recente. A consulta tem um limite de sete segundos e uma falha de rede permite continuar com os favoritos locais. Regressar do segundo plano ou trocar de perfil durante a mesma execução não repete a consulta. Os relógios dos equipamentos devem estar acertados; redes que bloqueiam descoberta UDP ou a porta TCP 45873 impedem a sincronização. Favoritos antigos sem data mantêm-se legíveis e passam a ter uma data na próxima alteração.

Em **Sincronização e cópia de segurança** e nas definições dos podcasts pode ativar a sincronização **Bluetooth**, sem exigir a mesma rede Wi-Fi. Instale a mesma versão nos dois equipamentos, emparelhe-os nas definições Android, ative a opção nos dois e abra a mesma lista com o mesmo nome de perfil. Escolha o equipamento uma vez; as seguintes sincronizações ocorrem ao marcar podcasts como ouvidos/por ouvir ou filmes/episódios como vistos/por ver. Sem consultas periódicas. Usa Bluetooth Classic seguro do Android e não sincroniza listas ou credenciais. Consulte [Validação](docs/validation.md) para testes e limitações.

Na versão **1.7.0**, os equipamentos que concluíram uma sincronização ficam
memorizados por lista/perfil. Ao abrir ou voltar à aplicação depois de a deixar
em segundo plano, tenta sincronizar com os equipamentos memorizados, mesmo
sem alterações locais pendentes, com **cinco tentativas no total por abertura**.
Os que responderem não são repetidos nessa abertura. Se nenhum responder,
para até à próxima abertura; novas marcações ficam pendentes. Uma sincronização
manual ou recebida bem-sucedida também permite retomar as marcações automáticas.
A escolha guardada nas versões anteriores é reutilizada na atualização.

Referência do diretório: [iTunes Search API](https://developer.apple.com/library/archive/documentation/AudioVideo/Conceptual/iTuneSearchAPI/Searching.html).

## Aviso legal e responsabilidade

A PSiptv é apenas um leitor multimédia destinado a fins recreativos, de desenvolvimento e de teste. A aplicação não fornece, aloja, vende, recomenda nem valida listas IPTV, canais, conteúdos ou URLs de terceiros.

Ao instalar ou utilizar a aplicação, o utilizador reconhece e aceita que é o único responsável pelas listas, credenciais, URLs, sites e restantes conteúdos que adiciona, acede ou reproduz, bem como pelo uso que faz da aplicação. Cabe exclusivamente ao utilizador confirmar que possui os direitos e autorizações necessários e que a sua utilização cumpre a legislação, as licenças, os direitos de autor e os termos aplicáveis no seu país.

Algumas listas IPTV, transmissões ou URLs podem ser ilegais ou não autorizadas em determinadas jurisdições. O programador não controla os conteúdos ou serviços de terceiros e, na medida máxima permitida por lei, não se responsabiliza pela sua origem, disponibilidade, legalidade, segurança ou utilização, nem por danos ou outras consequências decorrentes da instalação ou utilização da aplicação. Se tiver dúvidas sobre a legalidade de um conteúdo, não o adicione nem reproduza e procure aconselhamento jurídico local.

## Funcionalidades

- **Estilos e temas independentes**: em Perfil → Configurações → Aparência, escolha Atual, Clean, Cinema, Revista, Guia TV, Consola, Biblioteca ou Modular. Cinema destaca a retoma e usa capas; Revista organiza secções em listas numeradas; Guia TV privilegia a programação; Consola amplia cartões e navegação; Biblioteca usa capas verticais e navegação lateral em ecrãs largos; Modular distribui as secções do início em módulos. Os temas Meia-noite, Floresta, Oceano, Terracota e Ameixa juntam-se aos existentes e podem ser combinados com qualquer estilo. As escolhas são guardadas e incluídas nas cópias de segurança.

- Contas Xtream Codes: servidor, utilizador e palavra-passe, com validação de autenticação e estado da conta.
- Listas M3U por HTTP/HTTPS: nomes, logótipos, categorias, `tvg-id`, `group-title`, `EXTGRP`, endereços relativos e fonte XMLTV no cabeçalho.
- **Fontes adicionais**: ficheiros M3U locais, Stalker Portal por endereço MAC, Jellyfin e Plex por token, Tvheadend com autenticação Basic opcional e HDHomeRun por descoberta do alinhamento do dispositivo. As ligações são validadas antes de guardar; streams Stalker temporárias são resolvidas apenas no momento da reprodução.
- **Página inicial inteligente**: a nova primeira tab reúne continuar a ver com retoma, canais recentes, favoritos, programas a começar nos próximos 90 minutos e gravações recentes. O conteúdo é personalizado por lista e perfil e atualizado ao regressar à página.
- **TV ao Vivo**: cartões grandes com logótipos e favoritos, filtrados pelo grupo escolhido; tocar num canal inicia o preview integrado. Em horizontal, o vídeo fica ao lado dos canais. Maximizar esconde os menus e força landscape no Android; voltar restaura a orientação anterior.
- **Multiview**: na TV ao Vivo, abra um mosaico 2×2 para acompanhar até quatro canais da categoria em simultâneo. Pode pesquisar, adicionar, substituir ou remover canais; tocar num painel transfere o áudio para esse canal e mantém os restantes sem som. A quantidade de streams que funciona em simultâneo depende do dispositivo e do fornecedor.
- **Rádios portuguesas**: na TV ao Vivo e em Favoritos, o botão junto ao Multiview alterna entre canais de TV e rádios. A vista de rádios consulta as estações de Portugal registadas no [Radio Browser](https://www.radio-browser.info/), permite pesquisar por nome e guardar favoritas no perfil ativo. No Windows, o player mostra uma forma de onda e um espectro que reagem em tempo real aos graves e agudos da emissão. No Android, o espectro lê o áudio de saída apenas durante a reprodução de rádio, depois de pedir a permissão de áudio ao utilizador. A rádio continua a tocar se a permissão for recusada ou se o dispositivo não suportar a captura. A disponibilidade das emissões depende dos endereços publicados pelas rádios e da ligação à Internet.
- **Chromecast avançado**: no Android, o envio direto abre um controlador do recetor com estado e título atuais, reprodução/pausa, saltos de 30 segundos, progresso, volume, silêncio, paragem e desconexão. Filmes e episódios enviam uma fila nativa de até 100 conteúdos, com anterior/seguinte e reprodução automática no Chromecast.
- **Reprodução a partir do browser**: o browser interno deteta elementos de vídeo e streams HLS/DASH carregadas pela página, incluindo pedidos `fetch`/XHR, iframes e service workers no Android. Quando encontra uma transmissão HTTP/HTTPS reproduzível, ativa o botão de reprodução para a abrir no leitor integrado com os controlos, proporção, áudio, legendas, ecrã inteiro e partilha já existentes. Os pop-ups válidos abrem em separadores internos que podem ser alternados e fechados, enquanto os recursos publicitários conhecidos continuam a ser bloqueados.
- **Favoritos**: use ☆/★ junto a canais, filmes, séries e episódios (ou no leitor integrado) para guardar/remover. Abra **⋯ → Favoritos** para pesquisar e filtrar por tipo. Persistem por lista no armazenamento seguro, respeitam o PIN e são eliminados ao apagar a lista. A identidade M3U usa o endereço do stream para resistir à reordenação da lista; Xtream usa o ID e tipo do conteúdo.
- **Perfis de utilizador**: o perfil Principal mantém automaticamente os favoritos e o histórico já existentes. É possível criar até oito perfis locais, editar o nome e o ícone de qualquer perfil e alternar entre eles em Perfil e configurações → Perfis de utilizador. Cada perfil tem favoritos e histórico independentes; as listas IPTV e os catálogos permanecem partilhados.
- **Sincronização e cópia de segurança**: exporta listas, perfis, favoritos, histórico, personalização de categorias, fontes do browser e configurações para um ficheiro `.psiptvbackup`. A interface propõe proteção por palavra-passe, que encripta e autentica todo o documento, incluindo credenciais. Pode optar por JSON legível; cópias JSON antigas continuam a ser restauradas. A palavra-passe não é guardada e é necessária para abrir uma cópia protegida. O token GitHub volta ao `SecureStorage` ao restaurar. O seletor de partilha permite guardar a cópia num serviço cloud ou enviá-la para outro dispositivo; a reposição une os dados aos existentes. Os catálogos descarregáveis não são incluídos.
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
- **Experiência dedicada a Android TV**: a aplicação deteta televisões e dispositivos Leanback e aplica um layout mais compacto sem afetar telemóveis ou tablets. Títulos, barras de categorias, navegação inferior, cartões de canais e submenus ocupam menos altura; os ícones passam a ficar ao lado dos nomes onde isso poupa espaço, mantendo alvos de foco visíveis e adequados ao comando remoto. Durante a TV em direto em ecrã inteiro, as teclas cima/baixo do D-pad mudam diretamente para o canal anterior/seguinte da categoria quando os controlos e o menu de canais estão ocultos.
- **Android Auto**: apresenta e reproduz em modo áudio apenas os canais marcados como favoritos no perfil ativo, usando a interface segura e os controlos multimédia do automóvel. Uma lista protegida por PIN tem de ser previamente desbloqueada na aplicação.
- **Comando Wear OS**: a aplicação complementar **PSiptv Remote** descobre automaticamente instâncias do PSiptv na mesma rede Wi-Fi, permite escolher o dispositivo quando existe mais do que um, ajustar o volume e mudar de canal. O telemóvel/televisor deve ter o PSiptv aberto e ambos os dispositivos devem estar na mesma rede local.
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

A navegação inferior apresenta **Início**, **TV ao Vivo**, **Filmes/Séries**, **Favoritos** e **Browser**. No topo estão **Partilhar ecrã**, **Pesquisar** e **Perfil e configurações**. A pesquisa filtra a secção e o grupo atuais. O perfil apresenta a lista ativa e cartões para Configurações, Atualizar conteúdo, Trocar playlist, Adicionar lista, Guia TV, Favoritos e Bloquear. Em **Configurações → Aparência**, o design Atual/Clean e o tema são escolhas independentes. O design Clean usa menu recolhido, cartões compactos e ações contextuais; os temas Neve, Grafite, Areia e Índigo juntam-se ao claro, escuro e automático, com cor de destaque ajustável.

A partilha abre os controlos de transmissão do Android ou de ligação a um ecrã sem fios no Windows; em Apple, indica como abrir a duplicação de ecrã/AirPlay. No Android, o Chromecast pode receber diretamente a stream quando o formato e o fornecedor o permitem e apresenta o controlador avançado após estabelecer a sessão. Ao sair para as definições do sistema, aplica-se o bloqueio de segurança existente; volte a abrir a lista ao regressar.

## Executar

Requer SDK .NET 10 e as workloads MAUI da plataforma. Android requer versão 8.0/API 26 ou superior, conforme o pacote multimédia. O projeto fixa `Microsoft.Maui.Controls` em 10.0.60 e `CommunityToolkit.Maui.MediaElement` em 10.0.0. Android e Windows usam LibVLC no leitor integrado para suportar os formatos e codecs habituais de IPTV.

No Visual Studio, abra `PSiptv.slnx`, escolha o projeto **PSiptv** e o destino **Windows Machine** ou um dispositivo/emulador Android.

```powershell
dotnet build PSiptv/PSiptv.csproj -f net10.0-windows10.0.19041.0
dotnet build PSiptv/PSiptv.csproj -f net10.0-android
dotnet build PSiptv.Wear/PSiptv.Wear.csproj
```

Para distribuir uma atualização Android, use sempre a mesma chave de assinatura da
versão já instalada e aumente `ApplicationVersion`. Na linha de comandos, a
compilação Release exige a chave de assinatura fora do repositório:

```powershell
$env:PSIPTV_ANDROID_KEYSTORE = "C:\caminho\psiptv.keystore"
$env:PSIPTV_ANDROID_KEY_ALIAS = "psiptv"
$env:PSIPTV_ANDROID_KEY_PASSWORD = "..."
$env:PSIPTV_ANDROID_STORE_PASSWORD = "..."
dotnet publish PSiptv/PSiptv.csproj -c Release -f net10.0-android
```

No Visual Studio, selecione **Release → Publicar**, aguarde a criação do arquivo
e use **Distribuir → Ad Hoc** para escolher a identidade de assinatura existente
e guardar o APK assinado. A identidade é escolhida nesta etapa, depois da
compilação do arquivo. Não distribua o APK intermédio antes de o assinar.

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

## Sincronização de conteúdos vistos e ouvidos por Bluetooth (Android)

Nas **Definições dos podcasts → Sincronização por Bluetooth**, ative a opção em
ambos os equipamentos, depois de os emparelhar nas definições do Android.
Abra a mesma lista com o mesmo nome de perfil nos dois e prima **Escolher
dispositivo e sincronizar** num deles. A escolha fica guardada mesmo que o outro
equipamento ainda não esteja disponível.

Os podcasts e os estados vistos de filmes e episódios são sincronizados nos dois
sentidos. Marcar um podcast como ouvido/por ouvir ou um filme/episódio como
concluído/por ver desencadeia uma troca em segundo plano, agrupando marcações
próximas. Atualizações da posição, visitas a podcasts, início de reprodução e
alterações de favoritos não desencadeiam ligações. Não existem consultas ou
tentativas periódicas. Se a troca falhar, as alterações ficam guardadas até à
próxima marcação ou reabertura da aplicação. Se a campanha de abertura terminar
sem sucesso, as tentativas automáticas ficam bloqueadas até à próxima abertura
ou até uma sincronização manual/recebida comprovar a disponibilidade.
Ambas as aplicações devem permanecer abertas e o Bluetooth ligado; não é
necessária uma rede Wi-Fi. Os downloads de áudio não são transferidos.

As alterações mais recentes prevalecem por conteúdo, incluindo marcações como
«por ouvir» e «por ver». Cada troca transporta também o estado atual dos favoritos
e progresso. Cada lista/perfil mantém os
seus dados e equipamento escolhido. A sincronização para ao sair da aplicação
ou ao desativar a opção.

A versão 1.6.10 usa o protocolo v3: snapshots comprimidos, leitura/escrita em
blocos de 16 KB e confirmação de receção antes de fechar a ligação. A ligação
inicial tem um prazo separado de 30 segundos; a troca permite até 5 minutos,
com um limite de 90 segundos sem progresso, renovado pelos dados transferidos.
O limite de 8 MB aplica-se também aos dados descomprimidos. A página mostra
a fase e os KB transferidos; timeouts são identificados mesmo quando o Android
devolve «Broken pipe» ao fechar o socket. Atualize ambos os equipamentos:
versões com protocolos diferentes são recusadas. A
página de sincronização conserva o último erro, distinguindo lista/perfil,
versão, interrupção da ligação e falha de armazenamento.

## Testes

O projeto de testes é um executável sem dependências adicionais. Usa respostas HTTP simuladas e termina com erro se qualquer verificação falhar.

```powershell
dotnet run --project PSiptv.Tests/PSiptv.Tests.csproj
```

Valida o parsing M3U/XMLTV e XMLTV GZip, classificação e categorias, URLs relativos e ficheiros locais, fusos horários, proteção do PIN, autenticação Xtream, Stalker, Jellyfin, Plex, Tvheadend e HDHomeRun, temporadas, EPG, regras DVR e cancelamento.

Validação realizada: 121 verificações passaram; Windows e Android compilaram sem erros. O executável Windows iniciou e permaneceu ativo no teste de arranque. A reprodução, o Multiview, o Chromecast num recetor real, as legendas externas sobre vídeo real, a deteção de streams em sites reais, os downloads e a gravação, bem como a abertura efetiva num leitor externo instalado, ainda requerem teste com uma fonte real. Os destinos Apple não foram compilados neste ambiente.

## Estrutura

- `PSiptv.Core`: modelos, clientes Xtream/M3U/Stalker/Jellyfin/Plex/Tvheadend/HDHomeRun, XMLTV e PIN, sem dependência de MAUI.
- `PSiptv.Wear`: aplicação complementar Wear OS para volume e mudança de canal por Wi-Fi.
- `PSiptv/Services`: armazenamento seguro, sessão e tema.
- `PSiptv/Views`: contas, definições, PIN, guia, episódios e leitor.
- `PSiptv/MainPage.xaml.cs`: navegação e catálogo com lista virtualizada.
- `PSiptv.Tests`: verificações locais com dados simulados.

## Limitações atuais

- A disponibilidade de categorias, programação e conteúdos depende do fornecedor. O guia Xtream apresenta os programas devolvidos pelo endpoint de EPG curto.
- Android e Windows usam LibVLC no leitor integrado; os destinos Apple usam [MediaElement](https://learn.microsoft.com/dotnet/communitytoolkit/maui/views/mediaelement). Codecs e formatos suportados continuam a variar por sistema operativo. Não inclui transcodificação nem DRM; no leitor LibVLC, os cabeçalhos User-Agent, Referer e Cookie associados à stream são encaminhados ao fornecedor.
- A reprodução externa entrega o endereço do stream (que pode conter credenciais) ao leitor escolhido. Windows/Mac criam uma lista M3U temporária na cache; ficheiros com mais de 24 horas são removidos na próxima inicialização. O conteúdo de vídeo não é descarregado. A compatibilidade e a reprodução após sair do PSiptv passam a ser controladas pelo leitor externo.
- XMLTV pode ser XML simples ou comprimido em GZip (`.gz`), até 40 MB depois de descomprimido. Não há descarga de conteúdos de vídeo.
- Os ficheiros de vídeo das gravações DVR ocupam o armazenamento interno da aplicação e não são incluídos na cópia de segurança. HLS cifrado/DRM não pode ser gravado; nas plataformas não Android, a aplicação tem de permanecer em execução até ao fim do agendamento.
- Os downloads offline ocupam o armazenamento interno da aplicação, não são incluídos na cópia de segurança e dependem de o fornecedor aceitar pedidos HTTP de retoma. VOD em HLS segmentado e conteúdo com DRM não são descarregados nesta versão.
- As regras recorrentes dependem da qualidade e da antecedência do EPG. A pesquisa automática agenda programas encontrados nos oito dias seguintes; as gravações de vídeo continuam excluídas da cópia de segurança.
- Ligações HTTP são permitidas para compatibilidade com fornecedores IPTV; quando disponível, utilize HTTPS.
- O PIN controla o acesso dentro da aplicação. Não existe recuperação de PIN; mantenha-o num local seguro.
- As integrações foram testadas com dados simulados. É necessário verificar reprodução e EPG com a conta real e no dispositivo de destino.





## Novidades da 1.6.1

A versão 1.6.1 acrescenta início personalizável, pré-visualização nativa e acessibilidade da aparência, catálogo unificado, alternativas de canais, várias fontes XMLTV, coleções e estados, integração opcional com o launcher Android TV, controlo de conflitos e retenção DVR, diagnóstico de reprodução e atalhos com memória de foco. Consulte o [guia de utilização](docs/experience-1.6.1.md) e as [notas da versão](artifacts/RELEASE_NOTES_1.6.1.md).


# PSiptv 1.5.5

## Novidades

- Novo botão Podcasts junto ao seletor TV/Rádios, com pesquisa e reprodução dos episódios dos feeds RSS.
- Podcasts e episódios favoritos separados por perfil, acessíveis também pelo início e pela pesquisa global.
- Controlo de episódios Ouvidos/Por ouvir em Configurações → Podcasts favoritos e ouvidos.
- Marcação automática ao terminar a reprodução no leitor integrado; o estado só é conservado para favoritos.
- Estados dos podcasts incluídos nas cópias de segurança.
- Sincronização dos favoritos no arranque com outra instância aberta na mesma rede, com a mesma lista e nome de perfil.
- A cópia com a data mais recente substitui a local, incluindo remoções, preservando a data recebida para evitar atualizações repetidas.
- Compatibilidade com favoritos e respostas de controlo remoto de versões anteriores.

## Android

- Tag: `1.5.5`
- Pacote: `com.PS.PSiptv`
- Versão: `1.5.5`
- Nome do APK para a release: `PSiptv.v1.5.5.apk`
- Código de versão Android: `298578231`
- Arquiteturas: ARM, ARM64 e x64.
- SHA-256 do APK assinado: `63261CB35053A5FEB3039E34773D6191B5146B265D921FECC5865A75BD7CA4A9`
- Assinatura verificada e coincidente com a chave de distribuição da versão anterior.

## Validação

- 138 verificações automatizadas concluídas.
- Compilações Windows e Android sem erros.
- Publish Android Release 1.5.5 concluído sem erros; quatro avisos existentes de APIs Android obsoletas.
- Pesquisa no diretório de podcasts verificada.
- A sincronização entre dois equipamentos físicos ainda não foi testada.

A sincronização consulta a rede apenas na primeira abertura de uma lista desbloqueada após iniciar a aplicação. Os equipamentos devem ter os relógios acertados e a rede deve permitir a descoberta local. Leitores externos não comunicam o fim da reprodução à aplicação.

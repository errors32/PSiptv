# PSiptv 1.6.1

Esta versão acrescenta personalização, organização e ferramentas de reprodução aos estilos e temas introduzidos na 1.6.0.

## Novidades

1. **Página inicial personalizável** — mostrar/ocultar e reordenar secções por perfil, fixar conteúdos e retirar da retoma os conteúdos marcados como concluídos.
2. **Pré-visualização nativa da aparência** — exemplos dos estilos dentro das configurações, tamanho de texto, cartões compactos e redução das animações de foco.
3. **Catálogo unificado** — adicionar várias listas à mesma pesquisa, identificar a origem de cada resultado e filtrar canais, filmes e séries. As listas protegidas continuam a exigir autorização; a abertura de um resultado ativa a lista correspondente.
4. **Versões e alternativas de canais** — agrupamento por nome sem o sufixo de qualidade, versão e qualidade preferidas e troca automática opcional após esgotar as reconexões. As alternativas pertencem à lista ativa e excluem conteúdos ocultos ou bloqueados.
5. **EPG com várias fontes** — até oito endereços XMLTV, associação manual entre canal e identificador EPG, correção horária por fonte e cache de programação. A pesquisa global e as gravações recorrentes usam também estas fontes.
6. **Coleções e estados pessoais** — criar, renomear e eliminar coleções, organizar conteúdos e marcar filmes/episódios como por ver, a ver ou concluídos, separados por lista e perfil.
7. **Descoberta no Android TV** — integração opcional com Continuar a ver/Watch Next e uma fila de canais favoritos, com abertura direta na aplicação. Listas protegidas por PIN e conteúdos bloqueados/ocultos não são publicados no launcher.
8. **DVR com controlo de conflitos** — limite de gravações simultâneas por lista, verificação de sobreposições, margens antes/depois dos programas e limpeza opcional de gravações concluídas após 7, 30 ou 90 dias. A limpeza manual permanece a opção inicial.
9. **Diagnóstico de reprodução** — histórico local das últimas 100 sessões por perfil, com tempo de arranque, interrupções, resolução, codecs e bitrate quando disponibilizados pelo leitor, sem guardar URLs ou credenciais.
10. **Atalhos e memória de foco** — escolha das teclas de pesquisa, início e ecrã inteiro, espaço para reproduzir/pausar e reposição do foco no cartão anterior, respeitando campos de texto e mudanças de sessão.

As cópias de segurança passam a incluir a organização do início, coleções, estados, versões preferidas, fontes/mapeamentos EPG e novas preferências, mantendo compatibilidade com backups anteriores. Os dados pessoais adicionais são removidos com a lista ou o perfil.

## Onde encontrar

- **Perfil → Configurações → Aparência**: pré-visualização, texto, densidade, animações e personalização do início.
- **Perfil**: personalizar início, coleções/estados e diagnóstico de reprodução.
- **Pesquisa → Catálogo unificado**: consultar várias listas.
- **Configurações → Geral**: alternativas, qualidade, DVR, Android TV e atalhos.
- **Configurações → EPG**: fontes XMLTV e associações de canais.
- **Leitor → Coleções e estado**: fixar e organizar o conteúdo atual.

## Validação e distribuição

- 194 verificações do núcleo e 35 regressões/verificações adicionais concluídas com sucesso: 229 no total.
- Compilação Windows e compilação do código Android sem erros; permanecem avisos existentes de APIs Android obsoletas e campos não utilizados no Windows.
- Testes adicionais cobrem agrupamento/qualidade dos canais, sobreposições e margens DVR, retenção, associações e correções EPG, estados, fusão de coleções e compatibilidade dos dados antigos.
- Validação visual, reprodução real, comportamento do comando e integração com o launcher Android TV em dispositivos físicos permanecem pendentes. O roteiro encontra-se em `docs/validation.md`.
- Esta publicação Git disponibiliza o código-fonte e **não inclui um novo APK de distribuição**. iOS e Mac Catalyst não foram compilados neste ambiente Windows.

# PSiptv 1.7.4 — Windows e Android

- Nova página de atualizações em Configurações → Sobre → Atualizações da aplicação.
- Verificação diária opcional e procura manual das versões Windows no GitHub.
- Transferência com progresso, cancelamento e validação obrigatória de tamanho e SHA-256.
- Atualização da distribuição portátil após fechar a aplicação, com cópia dos binários anteriores e reposição em caso de falha na substituição.
- Pacote Windows x64 autónomo com .NET, Windows App SDK e LibVLC incluídos.

## Instalar

Descarregue `PSiptv.v1.7.4.windows-x64.zip`, extraia para uma pasta com permissões de escrita (por exemplo, dentro da pasta pessoal) e execute `PSiptv.exe`. Não execute diretamente dentro do ZIP. Instalações antigas precisam desta instalação manual inicial para receber o novo atualizador. As listas e definições são mantidas no armazenamento de dados da aplicação.

O repositório é privado: configure um token GitHub com acesso de leitura a Contents nas definições de atualização. O token é guardado no armazenamento seguro do Windows.

Windows e Android passam a usar a versão 1.7.4. No Android, instale `PSiptv.v1.7.4.apk`, assinado com a mesma identidade da versão anterior e com versionCode superior.

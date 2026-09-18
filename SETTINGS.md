# Configurações da aplicação

As opções gerais são persistidas no dispositivo. Histórico (100 entradas), ordenação, categorias personalizadas/ocultas e controlo parental são isolados por lista e guardados em SecureStorage.

- Reprodução Android: LibVLCSharp 3.10.1 e VideoLAN.LibVLC.Android 3.7.0-beta. O pacote nativo foi escolhido para suportar alinhamento de 16 KB; a versão NuGet ainda está identificada como beta.
- O leitor Android e o preview usam as mesmas opções: descodificador, buffer em segundos, agente HTTP, legendas, OpenSL ES, OpenGL e taxa de dados recebidos. As alterações aplicam-se ao abrir o próximo vídeo.
- A escolha MPEGTS/HLS reescreve apenas URLs de canais geradas por Xtream. URLs M3U assinadas e conteúdos VOD são preservados.
- Autoplay avança na ordem das temporadas/episódios, depois de terminar o episódio e de decorrer o intervalo escolhido. Sair do leitor ou bloquear a aplicação cancela a contagem.
- Imagem em imagem: Android 8 ou superior, quando permitido pelo dispositivo. A sessão de reprodução mantém-se apenas durante a janela flutuante; sair normalmente continua a bloquear a lista.
- O PIN parental é independente do PIN da lista. É necessário para alterar/desativar a proteção. Categorias personalizadas protegidas também protegem os respetivos conteúdos quando abertos pelo histórico/favoritos; episódios herdam a proteção da série.
- A fonte EPG aceita XMLTV personalizado ou a fonte do fornecedor. A atualização é feita ao abrir/atualizar o guia. A cache do guia dura até seis horas e apresenta a data da última atualização em memória.
- Limpeza automática: no arranque. Limpeza manual disponível. Remove cache de imagens e do guia, preservando credenciais, histórico e favoritos.
- Idioma: português, inglês ou sistema. Reiniciar aplica a escolha aos ecrãs já existentes. Títulos de conteúdos e categorias do fornecedor são preservados.
- As opções específicas do LibVLC Android ficam indisponíveis nas restantes plataformas, que mantêm o leitor MediaElement. O agente HTTP continua a aplicar-se aos pedidos ao fornecedor.
- As páginas de privacidade e termos apresentam informação local; não dependem de URLs legais externos inexistentes.

## Validação

Logs de compilação e testes em `artifacts/ui-validation/`:

- `settings-main-build.log`: APK Android principal.
- `settings-build.log`: APK isolado para ensaios no emulador.
- `settings-windows-build.log`: Windows.
- `settings-tests.log`: 46 verificações, incluindo filtros, ordenação, bloqueios herdados, formatos e agente HTTP.

O Android consulta o estado do LibVLC a partir da interface, sem callbacks geridos em threads nativas do motor: esta abordagem evita o crash JNI observado ao terminar a reprodução. O leitor foi ensaiado no emulador com ficheiros de teste, histórico, PIN parental e imagem em imagem. Biometria física, Chromecast/dispositivo remoto e iOS exigem validação nos respetivos equipamentos.

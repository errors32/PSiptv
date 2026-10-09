# Validação

Execute `dotnet run --project PSiptv.Tests/PSiptv.Tests.csproj -c Release` para os testes do núcleo e as regressões. O processo termina com erro quando uma verificação falha. O workflow `.github/workflows/validate.yml` executa estes testes e compila Android e Windows em cada push e pull request. Não publica versões nem usa credenciais de assinatura.

As regressões abrangem pedidos de reprodução substituídos, cancelamento, escrita atómica de imagens, transferências partilhadas, pesquisa e ordenação num catálogo de 20 000 conteúdos, backups protegidos e antigos, sincronização por alteração individual, remoções e troca bidirecional pelo protocolo Bluetooth. A troca do protocolo é testada com streams locais; não substitui testes do rádio Bluetooth nos equipamentos.

## Android TV

Com uma TV/emulador ligado ao ADB, abra uma pesquisa e mantenha o teclado visível. Execute:

```powershell
./scripts/Test-AndroidTvSearch.ps1 -Adb 'C:/Program Files (x86)/Android/android-sdk/platform-tools/adb.exe' -Serial '<serial>'
```

Repita em Podcasts, Pesquisa global e pesquisa de rádios. O script verifica Back, foco mantido, movimento para baixo e reentrada na pesquisa. Não inicia reprodução nem escreve no campo. Se o firmware não reportar a visibilidade do teclado ao `dumpsys input_method`, o teste recusa executar; valide manualmente nesse dispositivo.

Verifique também seleção de cartões, regresso do leitor ao cartão anterior, scroll e navegação em listas vazias.

## Reprodução e ciclo de vida

- Num fornecedor de teste com resolução de URLs atrasada, escolha A e depois B; faça a resposta de A chegar depois de B. B deve continuar a reproduzir. Repita fazendo A falhar.
- Saia do leitor enquanto o URL é resolvido. O pedido deve cancelar e nenhuma reprodução deve começar depois.
- Reproduza rádio/podcast, navegue para outra página e regresse: áudio e posição devem manter-se. Troque para vídeo e confirme que o áudio anterior para.
- Teste Home/retoma, bloqueio/desbloqueio e picture-in-picture: a reprodução deve seguir o comportamento definido para cada modo sem duplicar áudio.

## Bluetooth sem rede comum

1. Instale esta versão nos dois equipamentos Android e emparelhe-os nas definições Bluetooth do sistema.
2. Abra a mesma lista, com as mesmas credenciais quando existirem, e o mesmo nome de perfil nos dois equipamentos.
3. Ative **Sincronização por Bluetooth** em **Sincronização e cópia de segurança** ou nas definições dos podcasts. Autorize o acesso a dispositivos próximos.
4. Desligue o Wi-Fi num dos equipamentos. Adicione favoritos diferentes e altere progresso/estado ouvido em cada um.
5. Escolha o outro equipamento e sincronize. Confirme que ambos conservam os favoritos e convergem no estado dos podcasts.
6. Remova um favorito e sincronize. Volte a sincronizar: a remoção não deve ser revertida.
7. Mude de perfil numa das aplicações: a troca deve ser recusada. Repita bloqueando a lista durante a transferência.
8. Interrompa o Bluetooth durante a troca, reative-o e volte a ativar a opção na aplicação; confirme recuperação e ausência de dados parciais.

A sincronização é opcional, usa RFCOMM seguro e apenas dispositivos emparelhados. Depois de uma ligação manual bem-sucedida, volta a tentar o equipamento escolhido a cada minuto enquanto a aplicação estiver aberta. Não liga o rádio nem emparelha equipamentos automaticamente. Os relógios devem estar acertados: conflitos no mesmo item usam a data da alteração e um desempate determinístico. Os estados de remoção são conservados para impedir que cópias antigas recuperem dados apagados. Equipamentos sem Bluetooth Classic não suportam esta ligação. A integração é Android padrão e não depende da conta Xiaomi nem do serviço proprietário de partilha de ecrã.

## Backups e espaço

- Exporte uma cópia protegida, restaure noutro equipamento e confirme listas, perfis, favoritos e progresso. Uma palavra-passe errada ou um ficheiro alterado deve falhar antes de modificar os dados locais.
- Restaure também um backup JSON antigo.
- Carregue podcasts, consulte o espaço de cache e limpe-o. Favoritos, estados ouvidos e downloads devem permanecer.
- Cancele downloads de logótipos durante scroll e confirme que não ficam imagens incompletas. Uma imagem ausente deve continuar sem impedir a navegação.

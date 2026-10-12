# PSiptv 1.6.2

## Novidades

- **Comando remoto por Bluetooth no Android**: o telemóvel pode selecionar canais favoritos e ajustar o volume no tablet ou numa Android TV compatível, mesmo quando os equipamentos estão em redes Wi-Fi diferentes.
- **Escolha da ligação**: o ecrã Comando remoto permite alternar entre Wi-Fi e Bluetooth, atualizar os equipamentos disponíveis e ativar o dispositivo que vai reproduzir os canais.
- **Ativação independente**: a opção Permitir controlo por Bluetooth é guardada por equipamento e funciona separadamente da sincronização Bluetooth de favoritos e podcasts.
- **Ajuda e identificação dos equipamentos**: instruções de emparelhamento no próprio ecrã e identificação adicional para distinguir dispositivos Bluetooth com o mesmo nome.

## Melhorias

- Ligações Bluetooth seguras apenas entre equipamentos emparelhados no Android, com validação da versão do protocolo, da lista/perfil e do token antes de executar comandos.
- Tratamento de ligações interrompidas, mensagens excessivas, tempos limite e erros de reprodução. O serviço acompanha a abertura e o segundo plano da aplicação.
- O dispositivo remoto continua a ser reconhecido como ativo durante as consultas Bluetooth mesmo quando a descoberta Wi-Fi não o encontra.
- Proteção contra respostas atrasadas ao trocar de equipamento, ligação ou fechar o comando remoto.
- Ecrã reorganizado para conservar espaço para os canais no telemóvel, com ajuda Bluetooth e traduções adicionais para inglês.
- Controlo por Wi-Fi e sincronização Bluetooth existentes mantidos.

## Como utilizar

1. Instale a versão atualizada nos dois equipamentos e emparelhe-os nas definições Bluetooth do Android.
2. Abra a mesma lista com o mesmo nome de perfil em ambos e ative **Permitir controlo por Bluetooth** no ecrã **Comando remoto**.
3. No telemóvel, selecione **Bluetooth**, escolha o tablet ou a Android TV e toque em **Ativar dispositivo selecionado**, se necessário.
4. Escolha um canal favorito ou ajuste o volume.

As aplicações devem permanecer abertas e os equipamentos dentro do alcance Bluetooth. A Android TV precisa de suportar Bluetooth Classic RFCOMM entre aplicações. Bluetooth transporta os comandos e o estado; o vídeo continua a usar a ligação à Internet do tablet/TV. A aplicação Wear OS mantém o comando por Wi-Fi.

## Validação

- 205 verificações do núcleo e 35 regressões/verificações adicionais: **240 no total**.
- 11 verificações específicas do novo protocolo: seleção de canal, autenticação, isolamento de perfis, incompatibilidade de versões, erros do leitor, limites de mensagens, interrupção e cancelamento.
- Compilação Windows em Release concluída sem erros; permanecem avisos de campos não utilizados já existentes.
- Compilação Android em Release concluída sem erros; permanecem avisos existentes sobre APIs Android obsoletas.
- Testes com streams locais; a ligação Bluetooth física, reprodução e compatibilidade de cada Android TV devem ser confirmadas nos equipamentos.
- iOS e Mac Catalyst não foram compilados neste ambiente Windows.

## Distribuição

Esta publicação inclui **PSiptv.v1.6.2.apk**, assinado com a mesma identidade de distribuição das versões anteriores. O identificador do pacote mantém-se **com.PS.PSiptv** e o código de versão Android aumenta para permitir atualização das instalações existentes. O APK inclui ARM de 32 bits, ARM64 e x64.

- Código de versão Android: **298638491**.
- Assinatura e alinhamento do APK verificados antes da publicação.
- SHA-256: `9C3E09D3BBC6C86902A96EC5302F73893B276BF43A1254F041B15255B8A58F40`.

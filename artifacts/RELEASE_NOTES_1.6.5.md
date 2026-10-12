# PSiptv 1.6.5

## Correções e melhorias

- **Ligação Bluetooth persistente**: consultas de estado, volume, ativação e troca de canal reutilizam a ligação ao equipamento selecionado, evitando abrir e fechar uma ligação a cada consulta automática.
- **Comandos de canal protegidos das consultas periódicas**: enquanto um comando está em curso, o ecrã não inicia novas consultas de estado nem aplica respostas antigas sobre o resultado do comando. A ativação do tablet/TV deixa de repetir a procura de equipamentos.
- **Mais tempo para trocar de canal**: o comando de canal tem até 45 segundos para concluir; o recetor mantém a ligação entre pedidos e renova o limite de inatividade após cada troca.
- **Reconexão controlada**: uma consulta de estado pode repetir-se uma vez após falha de ligação. Comandos de reprodução não são repetidos automaticamente, evitando executar a mesma ação duas vezes.
- **Sessões independentes no recetor**: uma ligação aberta não bloqueia a aceitação de outra ligação Bluetooth. Fechar o comando remoto, trocar de equipamento, desativar Bluetooth ou colocar a aplicação em segundo plano encerra a ligação correspondente.
- **Compatibilidade com versões anteriores**: o recetor anuncia se suporta ligação persistente; equipamentos antigos continuam a usar uma ligação por pedido. Erros de reprodução não encerram uma ligação saudável com um recetor atualizado.
- Mantidas a lista completa de equipamentos emparelhados e a posição da lista de canais.

## Atualização e utilização

**Atualize o telemóvel e o tablet/Android TV para a 1.6.5** para utilizar a ligação persistente. Emparelhe-os no Android, abra a mesma lista/perfil e ative **Permitir controlo por Bluetooth** em ambos. No telemóvel, escolha o equipamento e **Ativar dispositivo selecionado**, se necessário.

As aplicações devem permanecer abertas e dentro do alcance Bluetooth. Recetores antigos mantêm o comportamento de ligar/desligar entre pedidos até serem atualizados.

## Validação

- **249 verificações automatizadas**: 214 verificações do núcleo e 35 regressões/verificações adicionais.
- Quatro novas verificações exercitam múltiplos comandos e consultas na mesma ligação, troca de canal, continuidade após erro de reprodução, ordem/execução única e anúncio de compatibilidade.
- Android e Windows compilados em Release; permanecem os avisos existentes.
- Os testes do protocolo usam streams locais. A ligação Bluetooth física e a reprodução real devem ser confirmadas nos equipamentos. iOS e Mac Catalyst não foram compilados neste ambiente Windows.

## Distribuição

**PSiptv.v1.6.5.apk** mantém o pacote **com.PS.PSiptv**, inclui ARM de 32 bits, ARM64 e x64 e usa a mesma identidade de assinatura da 1.6.4, com código de versão Android superior. Assinatura, alinhamento e metadados verificados antes da publicação.

- Código de versão Android: **298640091**.
- SHA-256: `D8889BB672C27987C3B5F1E8FB357E77C047C227CDCE5373B7289B9A35BB0E6A`.

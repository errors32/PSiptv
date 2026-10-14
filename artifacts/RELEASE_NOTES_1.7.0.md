# PSiptv 1.7.0

## Equipamentos memorizados e sincronização ao abrir

- Os equipamentos que concluíram uma sincronização Bluetooth ficam memorizados por lista e perfil, tanto ao enviar como ao receber. A página de sincronização mostra os nomes guardados.
- Abrir ou voltar à aplicação depois de a deixar em segundo plano inicia uma sincronização em segundo plano com os equipamentos conhecidos, mesmo sem alterações locais pendentes.
- O limite é de **cinco tentativas no total por abertura**, repartidas pelos equipamentos memorizados. Cada equipamento que responder é sincronizado uma vez nessa campanha.
- As tentativas são espaçadas por três segundos. A ligação inicial automática permite até dez segundos por tentativa; as transferências mantêm a compressão e os prazos da 1.6.10.
- Se nenhum responder, as tentativas automáticas param até à próxima abertura. As marcações ficam pendentes; uma sincronização manual ou recebida bem-sucedida também comprova disponibilidade e permite retomar o envio das marcações.
- Navegação interna, diálogos simples, alternar o Bluetooth e reativar a opção não renovam o limite da mesma abertura. Sair da aplicação ou mudar/bloquear o perfil cancela a campanha.
- Apenas equipamentos memorizados e ainda emparelhados são contactados. A escolha única das versões anteriores é reutilizada na atualização. Não existem consultas periódicas depois da campanha.

## Validação

- 302 verificações automatizadas: 214 do núcleo, 38 regressões, 24 de consumo, 13 de transferências grandes e 13 de sincronização ao abrir.
- Verificados cinco pedidos no total, vários equipamentos, deduplicação, sucesso parcial, nova abertura, cancelamento, pedidos concorrentes e manutenção da memória de equipamentos.
- Mantido o protocolo Bluetooth v3, compatível com a 1.6.10. Atualize ambos os equipamentos para obter a sincronização limitada ao abrir em ambos.
- A comunicação física e o ciclo de vida Android precisam de confirmação nos equipamentos reais.
- Compilações Release Android e Windows concluídas sem erros.
- APK assinado com a mesma chave da 1.6.10; versão Android `298662871`.
- SHA-256 do APK: `1284EFE526446404D4725F331399E21CEDC7F4016419A7A0BF3371202D8E1200`.

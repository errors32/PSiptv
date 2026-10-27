# PSiptv 1.7.17 — Expiração em As Suas Listas

- Cada cartão em **Definições → As Suas Listas** passa a mostrar a expiração
  fornecida pelo servidor, com data/hora local e tempo restante em dias, horas
  ou minutos.
- Consulta contas Xtream e links M3U Xtream `get.php` com credenciais, usando
  o mesmo servidor e caminho configurados na lista.
- Apresenta **Sem expiração** quando o fornecedor indica validade ilimitada e
  **Expirada em…** para datas passadas. Dados ausentes ou inválidos não são
  interpretados como validade ilimitada.
- As outras fontes mostram **Expiração não informada pela fonte**. Se a consulta
  falhar, a validade fica indisponível e pode ser consultada novamente ao abrir
  a página.
- Cartões apresentados imediatamente; consultas limitadas a duas em simultâneo,
  oito segundos por consulta e 1 MB de metadados. Sair da página cancela o trabalho.
- Inclui os pacotes de telemóvel/Android TV, Wear OS e Windows na versão 1.7.17.

## Validação

Testes automáticos aprovados para datas em texto/número, ausência de expiração,
contas expiradas, autenticação recusada, dados inválidos e fora de intervalo,
links M3U, codificação de credenciais, limite de resposta e cancelamento.
Compilação Release e assinaturas dos APKs verificadas.

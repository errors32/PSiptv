# PSiptv 1.7.19 — Correção dos botões do preview no Android TV

- Corrige os botões de **maximizar** e **reproduzir/parar** no preview de
  **TV ao Vivo** e **Favoritos** quando ativados com OK/Enter do comando Android TV.
- O tratamento da navegação passa as teclas de ativação ao botão nativo,
  incluindo os eventos de premir e soltar, permitindo executar a ação normal.
- Mantém a navegação entre preview e canais, os saltos por página e a ativação
  dos cartões. Nos limites da navegação explícita, permite procurar o próximo
  controlo através da navegação nativa do Android.
- Inclui os pacotes Android/Android TV, Wear OS e Windows na versão 1.7.19.

## Validação

Testes de regressão executam o código de navegação de produção com o estado
inicial dos eventos usado pelo Android: OK/Enter, repetição, premir/soltar,
teclas direcionais, saltos por página, limites e ativação dos cartões.
O teste reproduz a falha sem a correção e passa com a correção.
Compilações Release Android, Wear OS e Windows verificadas; APKs assinados com
o certificado de produção da 1.7.18 e códigos de versão superiores e distintos.
Verificação do pacote Wear OS e testes de substituição, reversão e rejeição de
pacotes inválidos do atualizador Windows concluídos.
A validação física com comando numa Android TV continua pendente.

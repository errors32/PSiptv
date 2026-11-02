# PSiptv 1.7.18 — Podcasts, preview e navegação Android TV

- **Podcasts:** botões de episódio anterior e seguinte na barra do leitor,
  no mini-leitor e nos controlos multimédia do Android. O anterior reinicia
  o episódio após os primeiros três segundos; no início, abre o anterior.
  O seguinte funciona independentemente da reprodução automática, por ordem
  de publicação, incluindo episódios publicados no mesmo dia. Quando existe
  um download completo, a navegação usa esse ficheiro.
- **TV ao Vivo e Favoritos:** novo botão de reproduzir/parar junto de maximizar
  no preview. Parar conserva o canal selecionado para voltar a reproduzi-lo.
  Ambos os botões são maiores, com descrições acessíveis e dicas.
- **Android TV:** navegação explícita na grelha de canais. Esquerda na primeira
  coluna leva ao preview; direita no botão de maximizar regressa à lista.
  O foco regressa à posição anterior ao sair do ecrã inteiro, incluindo canais
  que ficaram fora da área visível. Manter cima/baixo premido acelera o percurso;
  CH+/CH− e Page Up/Down permitem saltos por página quando o comando os oferece.
- O foco dos botões é mais visível e mantém-se durante as ações, com proteção
  contra ativações repetidas enquanto a mesma ação está em curso.
- Inclui os pacotes de telemóvel/Android TV, Wear OS e Windows na versão 1.7.18.

## Validação

Testes automáticos para a ordem e os limites dos episódios, reinício do podcast,
navegação em grelhas, linhas incompletas, listas vazias e saltos por página.
Compilações Android e Windows verificadas. A navegação com comando ainda não
foi validada numa Android TV física.

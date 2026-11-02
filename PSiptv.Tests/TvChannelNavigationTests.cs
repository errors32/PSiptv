using PSiptv.Core;

internal static class TvChannelNavigationTests
{
    public static void Run(Action<bool, string> check)
    {
        check(TvChannelNavigation.Target(4, 20, 3, TvNavigationDirection.Down) == 7 &&
            TvChannelNavigation.Target(7, 20, 3, TvNavigationDirection.Up) == 4,
            "Comando TV preserva a coluna ao subir e descer na lista de canais");
        check(TvChannelNavigation.Target(3, 20, 3, TvNavigationDirection.Left) == -1 &&
            TvChannelNavigation.Target(2, 20, 3, TvNavigationDirection.Right) == -1,
            "Extremos horizontais da grelha não saltam para outra linha e permitem aceder ao preview");
        check(TvChannelNavigation.Target(4, 20, 3, TvNavigationDirection.Left) == 3 &&
            TvChannelNavigation.Target(4, 20, 3, TvNavigationDirection.Right) == 5,
            "Comando TV percorre os canais da mesma linha sem alterar a seleção");
        check(TvChannelNavigation.Target(2, 20, 3, TvNavigationDirection.Up) == -1 &&
            TvChannelNavigation.Target(19, 20, 3, TvNavigationDirection.Down) == -1,
            "Primeira e última linhas deixam sair para os controlos exteriores à lista");
        check(TvChannelNavigation.Target(17, 20, 3, TvNavigationDirection.Down) == 19 &&
            TvChannelNavigation.Target(19, 20, 3, TvNavigationDirection.Right) == -1,
            "Última linha incompleta recebe foco num canal existente");
        check(TvChannelNavigation.Target(4, 1000, 3, TvNavigationDirection.PageDown, 4) == 16 &&
            TvChannelNavigation.Target(16, 1000, 3, TvNavigationDirection.PageUp, 4) == 4,
            "Listas longas avançam e recuam uma página de canais com o comando TV");
        check(TvChannelNavigation.Target(4, 20, 3, TvNavigationDirection.PageUp, 4) == 0 &&
            TvChannelNavigation.Target(16, 20, 3, TvNavigationDirection.PageDown, 4) == 19,
            "Saltos por página respeitam o início e o fim da lista");
        check(TvChannelNavigation.Target(0, 0, 3, TvNavigationDirection.Down) == -1 &&
            TvChannelNavigation.Target(0, 20, 0, TvNavigationDirection.Down) == -1 &&
            TvChannelNavigation.Target(20, 20, 3, TvNavigationDirection.Up) == -1,
            "Navegação TV ignora listas vazias e posições inválidas");
        check(TvChannelNavigation.Target(3, 8, 1, TvNavigationDirection.Down) == 4 &&
            TvChannelNavigation.Target(3, 8, 1, TvNavigationDirection.Left) == -1 &&
            TvChannelNavigation.Target(3, 8, 1, TvNavigationDirection.PageDown, 3) == 6,
            "Vista Clean com uma coluna permite navegação vertical, páginas e acesso direto ao preview");
    }
}

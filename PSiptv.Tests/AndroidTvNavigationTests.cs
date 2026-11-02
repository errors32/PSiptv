using Android.Views;
using PSiptv;
using PSiptv.Core;

internal static class AndroidTvNavigationTests
{
    public static void Run(Action<bool, string> check)
    {
        var view = new View();
        var movements = new List<(TvNavigationDirection Direction, int Repeat)>();
        var bridge = new AndroidTvNavigationBridge(view)
        {
            Navigate = (direction, repeat) => { movements.Add((direction, repeat)); return true; }
        };
        foreach (var key in new[] { Keycode.DpadCenter, Keycode.Enter, Keycode.NumpadEnter, Keycode.Space })
        {
            var down = view.Dispatch(key, KeyEventActions.Down);
            var repeated = view.Dispatch(key, KeyEventActions.Down, 1);
            var up = view.Dispatch(key, KeyEventActions.Up);
            check(!down && !repeated && !up && movements.Count == 0,
                $"Preview TV deixa {key} chegar ao botão nativo ao premir, repetir e soltar");
        }
        foreach (var key in new[] { Keycode.Back, Keycode.MediaPlayPause })
            check(!view.Dispatch(key, KeyEventActions.Down) && !view.Dispatch(key, KeyEventActions.Up),
                $"Navegação TV deixa {key} seguir para o tratamento normal");

        foreach (var (key, expected) in new[]
        {
            (Keycode.DpadLeft, TvNavigationDirection.Left), (Keycode.DpadRight, TvNavigationDirection.Right),
            (Keycode.DpadUp, TvNavigationDirection.Up), (Keycode.DpadDown, TvNavigationDirection.Down),
            (Keycode.PageUp, TvNavigationDirection.PageUp), (Keycode.ChannelUp, TvNavigationDirection.PageUp),
            (Keycode.PageDown, TvNavigationDirection.PageDown), (Keycode.ChannelDown, TvNavigationDirection.PageDown)
        })
        {
            var count = movements.Count;
            var down = view.Dispatch(key, KeyEventActions.Down);
            var up = view.Dispatch(key, KeyEventActions.Up);
            check(down && up && movements.Count == count + 1 && movements[^1] == (expected, 0),
                $"Navegação TV mantém {key} e consome apenas o movimento efetuado");
        }

        bridge.Navigate = (_, _) => false;
        check(!view.Dispatch(Keycode.DpadUp, KeyEventActions.Down) && !view.Dispatch(Keycode.DpadUp, KeyEventActions.Up),
            "Limite da grelha TV permite que a navegação nativa procure outro controlo");
        bridge.Navigate = null;
        check(!view.Dispatch(Keycode.DpadRight, KeyEventActions.Down) && !view.Dispatch(Keycode.DpadRight, KeyEventActions.Up),
            "Sem destino de navegação TV não captura as teclas");

        // Card activation is attached after navigation in AndroidTvFocus.Attach.
        // Its handled result must survive, including a long-press release.
        var activations = 0;
        view.KeyPress += (_, args) =>
        {
            if (args.KeyCode != Keycode.DpadCenter || args.Event?.Action != KeyEventActions.Up) return;
            activations++;
            args.Handled = true;
        };
        var cardDown = view.Dispatch(Keycode.DpadCenter, KeyEventActions.Down);
        var cardUp = view.Dispatch(Keycode.DpadCenter, KeyEventActions.Up);
        check(!cardDown && cardUp && activations == 1,
            "Navegação TV preserva a ativação dos cartões ao soltar OK");
    }
}

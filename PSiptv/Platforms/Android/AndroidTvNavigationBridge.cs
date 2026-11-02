using Android.Views;
using PSiptv.Core;
using NativeView = Android.Views.View;

namespace PSiptv;

internal sealed class AndroidTvNavigationBridge
{
    internal Func<TvNavigationDirection, int, bool>? Navigate { get; set; }
    private Keycode? consumedKey;
    private long lastMovement;

    internal AndroidTvNavigationBridge(NativeView view) => view.KeyPress += OnKeyPress;

    private void OnKeyPress(object? sender, NativeView.KeyEventArgs e)
    {
        // Android's generated KeyPress listener starts with Handled = true.
        // Release keys we do not navigate so native buttons receive OK/Enter
        // on both Down and Up and can raise their normal Click event.
        e.Handled = false;
        if (e.Event?.Action == KeyEventActions.Up && consumedKey == e.KeyCode)
        {
            consumedKey = null;
            e.Handled = true;
            return;
        }
        if (e.Event?.Action != KeyEventActions.Down) return;
        TvNavigationDirection? direction = e.KeyCode switch
        {
            Keycode.DpadLeft => TvNavigationDirection.Left,
            Keycode.DpadRight => TvNavigationDirection.Right,
            Keycode.DpadUp => TvNavigationDirection.Up,
            Keycode.DpadDown => TvNavigationDirection.Down,
            Keycode.PageUp or Keycode.ChannelUp => TvNavigationDirection.PageUp,
            Keycode.PageDown or Keycode.ChannelDown => TvNavigationDirection.PageDown,
            _ => null
        };
        if (direction is null) return;
        var now = Environment.TickCount64;
        if (e.Event.RepeatCount > 0 && consumedKey == e.KeyCode && now - lastMovement < 80)
        {
            e.Handled = true;
            return;
        }
        if (Navigate?.Invoke(direction.Value, e.Event.RepeatCount) != true)
        {
            consumedKey = null;
            return;
        }
        lastMovement = now;
        consumedKey = e.KeyCode;
        e.Handled = true;
    }
}

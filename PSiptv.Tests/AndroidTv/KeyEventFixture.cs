// Minimal Android event boundary for running the production navigation bridge
// without a device. Mono.Android's IOnKeyListenerImplementor.OnKey constructs
// View.KeyEventArgs with handled: true before invoking KeyPress subscribers.
namespace Android.Views;

internal enum Keycode
{
    DpadCenter, Enter, NumpadEnter, Space, Back, MediaPlayPause,
    DpadLeft, DpadRight, DpadUp, DpadDown, PageUp, ChannelUp, PageDown, ChannelDown
}

internal enum KeyEventActions { Down, Up }
internal sealed record KeyEvent(KeyEventActions Action, int RepeatCount = 0);

internal sealed class View
{
    internal sealed class KeyEventArgs(bool handled, Keycode keyCode, KeyEvent? keyEvent) : EventArgs
    {
        public bool Handled { get; set; } = handled;
        public Keycode KeyCode { get; } = keyCode;
        public KeyEvent? Event { get; } = keyEvent;
    }

    public event EventHandler<KeyEventArgs>? KeyPress;

    internal bool Dispatch(Keycode key, KeyEventActions action, int repeat = 0)
    {
        var args = new KeyEventArgs(true, key, new(action, repeat));
        KeyPress?.Invoke(this, args);
        return args.Handled;
    }
}

namespace PSiptv.Services;

public static class ShortcutService
{
    private static WeakReference<Page>? owner;
    private static Func<string, Task>? action;
    public static void Register(Page page, Func<string, Task> handler) { owner = new(page); action = handler; }
    public static bool Dispatch(string key)
    {
        if (owner is null || !owner.TryGetTarget(out var page) || action is null || page.Navigation.ModalStack.Count > 0 || page.Navigation.NavigationStack.LastOrDefault() != page) return false;
        var command = key == Preferences.Default.Get("shortcut.search", "S") ? "search"
            : key == Preferences.Default.Get("shortcut.home", "H") ? "home"
            : key == Preferences.Default.Get("shortcut.fullscreen", "F") ? "fullscreen" : key == "Space" ? "playpause" : null;
        if (command is null) return false;
        var handler = action;
        MainThread.BeginInvokeOnMainThread(async () => { try { await handler(command); } catch (Exception ex) { await PSiptv.Views.Ui.ErrorAsync(page, ex); } });
        return true;
    }
    public static void Attach(Page page)
    {
#if WINDOWS
        page.Loaded += (_, _) =>
        {
            if (page.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement native) return;
            native.KeyDown -= HandleWindowsKey; native.KeyDown += HandleWindowsKey;
        };
        page.Unloaded += (_, _) => { if (page.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement native) native.KeyDown -= HandleWindowsKey; };
#endif
    }
#if WINDOWS
    private static void HandleWindowsKey(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
    {
        if (sender is not Microsoft.UI.Xaml.FrameworkElement native || native.XamlRoot is null) return;
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(native.XamlRoot);
        if (focused is Microsoft.UI.Xaml.Controls.TextBox or Microsoft.UI.Xaml.Controls.PasswordBox or Microsoft.UI.Xaml.Controls.RichEditBox or Microsoft.UI.Xaml.Controls.ComboBox) return;
        args.Handled = Dispatch(args.Key.ToString());
    }
#endif
}

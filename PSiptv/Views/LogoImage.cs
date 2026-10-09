using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class LogoImage : Image
{
    private CancellationTokenSource? loading;
    public LogoImage() { Unloaded += (_, _) => loading?.Cancel(); Loaded += (_, _) => Load(); }
    protected override void OnBindingContextChanged() { base.OnBindingContextChanged(); Load(); }
    private async void Load()
    {
        loading?.Cancel(); Source = null;
        if (BindingContext is not MediaItem item || item.Logo.Length == 0) return;
        var cancel = new CancellationTokenSource(); loading = cancel;
        try
        {
            var path = await LogoCacheService.LoadAsync(item.Logo, cancel.Token);
            if (!cancel.IsCancellationRequested && path is not null) Source = ImageSource.FromFile(path);
        }
        catch (Exception) { /* Missing artwork must not stop browsing. */ }
        finally { if (loading == cancel) loading = null; cancel.Dispose(); }
    }
}

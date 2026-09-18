using PSiptv.Core;
using PSiptv.Views;

namespace PSiptv.Services;

public static class PlaybackService
{
    public static async Task PlayAsync(Page owner, MediaItem item, IReadOnlyList<MediaItem>? episodes = null, double resume = 0)
    {
        if (!await CatalogOptionsService.AuthorizePlaybackAsync(owner, item)) return;
        if (!DeviceProfile.IsAutomotive && Preferences.Default.Get("externalPlayer", false))
        {
            if (!ExternalPlayerService.IsConfigured && !await ExternalPlayerService.ChooseAsync(owner))
            {
                Preferences.Default.Set("externalPlayer", false);
                await owner.Navigation.PushAsync(new PlayerPage(item, episodes, resume));
                return;
            }
            try { await OpenExternalAsync(item, useSelectedPlayer: true); }
            catch (Exception)
            {
                if (await LanguageService.ConfirmAsync(owner, "Leitor externo indisponível", "Não foi possível abrir um leitor externo. Reproduzir no leitor integrado?", "Reproduzir", "Cancelar")
                    && AppServices.ActiveAccount is not null)
                    await owner.Navigation.PushAsync(new PlayerPage(item, episodes, resume));
            }
        }
        else await owner.Navigation.PushAsync(new PlayerPage(item, episodes, resume));
    }

    public static async Task OpenExternalAsync(MediaItem item, bool useSelectedPlayer = true)
    {
        if (AppServices.ActiveAccount is not { } account) return;
        item = StreamPreferences.ApplyFormat(account, item, AppOptions.StreamFormat);
        item = await AppServices.Client.ResolveStreamAsync(account, item, CancellationToken.None);
        var uri = WebAddress.Require(item.Url);
#if ANDROID
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("Não foi possível abrir o seletor de leitores.");
            var packageManager = activity.PackageManager ?? throw new InvalidOperationException("Não foi possível consultar os leitores instalados.");
            using var intent = new Android.Content.Intent(Android.Content.Intent.ActionView);
            intent.SetDataAndType(Android.Net.Uri.Parse(uri.AbsoluteUri), "video/*");
            intent.PutExtra("title", item.Name);
            try
            {
                var selectedPackage = useSelectedPlayer ? ExternalPlayerService.SelectedPlayerId : "";
                if (selectedPackage.Length > 0)
                {
                    intent.SetPackage(selectedPackage);
                    if (intent.ResolveActivity(packageManager) is null)
                        throw new Android.Content.ActivityNotFoundException();
                    activity.StartActivity(intent);
                }
                else
                {
                    using var chooser = Android.Content.Intent.CreateChooser(intent, "Abrir com leitor externo");
                    activity.StartActivity(chooser);
                }
            }
            catch (Android.Content.ActivityNotFoundException)
            {
                throw new InvalidOperationException("Instale um leitor de vídeo compatível, como VLC ou MX Player.");
            }
        });
#elif WINDOWS
        var path = await CreateTemporaryPlaylistAsync(item);
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        var opened = await Windows.System.Launcher.LaunchFileAsync(file,
            new Windows.System.LauncherOptions { DisplayApplicationPicker = true });
        if (!opened) throw new InvalidOperationException("Selecione um leitor que suporte listas M3U, como VLC.");
#elif IOS
        if (useSelectedPlayer && ExternalPlayerService.SelectedPlayerId == ExternalPlayerService.VlcPlayerId)
        {
            var opened = await Launcher.Default.TryOpenAsync(new Uri($"vlc-x-callback://x-callback-url/stream?url={Uri.EscapeDataString(uri.AbsoluteUri)}"));
            if (!opened) throw new InvalidOperationException("O VLC deixou de estar disponível. Escolha novamente o leitor externo nas configurações.");
        }
        else
        {
            var path = await CreateTemporaryPlaylistAsync(item);
            if (!await Launcher.Default.OpenAsync(new OpenFileRequest("Abrir com leitor externo", new ReadOnlyFile(path, "audio/x-mpegurl"))))
                throw new InvalidOperationException("Selecione uma aplicação que suporte listas M3U, como VLC.");
        }
#else
        var path = await CreateTemporaryPlaylistAsync(item);
        if (!await Launcher.Default.OpenAsync(new OpenFileRequest("Abrir com leitor externo", new ReadOnlyFile(path, "audio/x-mpegurl"))))
            throw new InvalidOperationException("Associe os ficheiros M3U a um leitor como VLC.");
#endif
    }

    private static async Task<string> CreateTemporaryPlaylistAsync(MediaItem item)
    {
        var folder = Path.Combine(FileSystem.CacheDirectory, "external-playback");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"stream-{Guid.NewGuid():N}.m3u");
        await File.WriteAllTextAsync(path, ExternalPlaylist.Create(item), new System.Text.UTF8Encoding(false));
        return path;
    }

    public static void CleanOldPlaylists()
    {
        // Keep recent files available until the external process has read them.
        var folder = Path.Combine(FileSystem.CacheDirectory, "external-playback");
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder, "stream-*.m3u"))
        {
            try { if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddHours(-24)) File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

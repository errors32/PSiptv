using System.Text.Json;
using PSiptv.Core;

namespace PSiptv.Services;

public static class PodcastAutomationService
{
    private static readonly SemaphoreSlim gate = new(1);
    private static bool started;
    private static Timer? timer;
    private static string OptionsKey(string accountId, string profileId) => "podcast-options." + UserProfileService.Scope(accountId, profileId);
    public static PodcastOptions Options(string accountId, string profileId)
    {
        try { return JsonSerializer.Deserialize<PodcastOptions>(Preferences.Default.Get(OptionsKey(accountId, profileId), "{}")) ?? new(); }
        catch (JsonException) { return new(); }
    }
    public static void Save(PodcastOptions options)
    {
        if (AppServices.ActiveAccount is not { } account) return;
        Preferences.Default.Set(OptionsKey(account.Id, UserProfileService.Active.Id), JsonSerializer.Serialize(options));
        if (!CanRun(options)) _ = OfflineDownloadService.PauseAutomaticAsync(account.Id, UserProfileService.Active.Id);
        _ = RunAsync();
    }
    public static bool CanRun(PodcastOptions options) => PodcastPolicy.CanDownloadAutomatically(options,
        Connectivity.Current.NetworkAccess == NetworkAccess.Internet,
        Connectivity.Current.ConnectionProfiles.Contains(ConnectionProfile.WiFi));

    public static void Start()
    {
        if (!started)
        {
            started = true;
            Connectivity.Current.ConnectivityChanged += (_, _) =>
            {
                if (AppServices.ActiveAccount is { } account && !CanRun(Options(account.Id, UserProfileService.Active.Id)))
                    _ = OfflineDownloadService.PauseAutomaticAsync(account.Id, UserProfileService.Active.Id);
                _ = RunAsync();
            };
            FavoritesService.Changed += () => _ = RunAsync();
            timer = new Timer(_ => _ = RunAsync(), null, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
        }
        _ = RunAsync();
    }

    public static async Task RunAsync()
    {
        if (!await gate.WaitAsync(0)) return;
        try
        {
            if (AppServices.ActiveAccount is not { } account) return;
            var profile = UserProfileService.Active.Id;
            var session = AppServices.SessionVersion;
            var options = Options(account.Id, profile);
            if (!options.AutomaticDownloads) return;
            var snapshot = await FavoritesService.SnapshotAsync();
            if (snapshot is null || session != AppServices.SessionVersion) return;
            if (options.DeletePlayedDownloads)
                foreach (var download in await OfflineDownloadService.LoadAsync(account.Id, profile))
                    if (PodcastFeed.IsPodcast(download.Item) && snapshot.HeardEpisodes.ContainsKey(download.Item.Id))
                        await OfflineDownloadService.RemoveAsync(download);
            if (!CanRun(options)) return;
            foreach (var paused in (await OfflineDownloadService.LoadAsync(account.Id, profile))
                .Where(d => d.Automatic && d.State == OfflineDownloadState.Paused))
            {
                if (session != AppServices.SessionVersion || !CanRun(Options(account.Id, profile))) return;
                await OfflineDownloadService.ResumeAsync(paused);
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            foreach (var feed in snapshot.Entries.Select(e => e.Item).Where(e => e.HasEpisodes && PodcastFeed.IsPodcast(e)))
            {
                options = Options(account.Id, profile);
                if (session != AppServices.SessionVersion || !CanRun(options)) return;
                if (options.SelectedFeeds is { } selected && !selected.Contains(feed.Id)) continue;
                var key = "podcast-auto-check." + UserProfileService.Scope(account.Id, profile) + "." + feed.Id;
                var value = Preferences.Default.Get(key, "");
                var previous = DateTimeOffset.TryParse(value, out var date) ? (DateTimeOffset?)date : null;
                var checkedAt = DateTimeOffset.UtcNow;
                try
                {
                    var episodes = await PodcastService.EpisodesAsync(feed, timeout.Token);
                    var existingIds = (await OfflineDownloadService.LoadAsync(account.Id, profile)).Select(d => d.Item.Id).ToHashSet();
                    var downloadable = episodes.Where(e => e.MediaType.Length == 0 ||
                        e.MediaType.Split(';')[0].Trim().Equals("audio/mpeg", StringComparison.OrdinalIgnoreCase) ||
                        e.MediaType.Split(';')[0].Trim().Equals("audio/mp3", StringComparison.OrdinalIgnoreCase));
                    var candidates = PodcastPolicy.AutomaticCandidates(previous is null ? downloadable :
                        downloadable.Where(e => !existingIds.Contains(e.Id)), snapshot.HeardEpisodes, previous);
                    foreach (var episode in candidates)
                    {
                        if (session != AppServices.SessionVersion || !CanRun(Options(account.Id, profile))) return;
                        var downloads = await OfflineDownloadService.LoadAsync(account.Id, profile);
                        if (downloads.Any(d => d.Item.Id == episode.Id)) continue;
                        if (downloads.Where(d => PodcastFeed.IsPodcast(d.Item)).Sum(d => d.BytesDownloaded) >=
                            Math.Clamp(options.MaximumMegabytes, 128, 10240) * 1024L * 1024L) return;
                        await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            if (session != AppServices.SessionVersion) return;
                            await OfflineDownloadService.QueueAsync(account, episode, feed.Name, automatic: true);
                        });
                    }
                    var cached = await PodcastCacheService.ReadAsync(feed.Url, timeout.Token);
                    if (session != AppServices.SessionVersion) return;
                    // Keep the previous checkpoint until every batch is queued, so a busy feed
                    // cannot lose older new episodes when more than 20 arrive between checks.
                    if (candidates.Count < 20)
                        Preferences.Default.Set(key, (cached?.FetchedAt ?? checkedAt).ToString("O"));
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        finally { gate.Release(); }
    }
}

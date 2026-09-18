using PSiptv.Core;

namespace PSiptv.Services;

public sealed record CatalogUpdateResult(PlaylistAccount Account,
    IReadOnlyDictionary<MediaKind, IReadOnlyList<MediaItem>> Catalogs,
    CatalogChannelChanges ChannelChanges, DateTimeOffset CompletedAt);

public static class CatalogUpdateService
{
    private const string LastUpdateKey = "catalogUpdate.lastCompleted";
    private const string LastSummaryKey = "catalogUpdate.lastSummary";
    private static readonly SemaphoreSlim gate = new(1, 1);
    private static int startupChecked;
    private static Timer? timer;

    public static event Action<CatalogUpdateResult>? Completed;

    public static string LastSummary => Preferences.Default.Get(LastSummaryKey, "Ainda não atualizado automaticamente");

    public static void ConfigureSchedule(bool replacePlatformSchedule = true)
    {
        timer?.Dispose();
        timer = null;
        if (AppOptions.CatalogUpdateInBackground && AppOptions.CatalogUpdateSchedule is
            CatalogUpdateSchedule.Daily or CatalogUpdateSchedule.Weekly)
        {
            timer = new Timer(_ => _ = TryRunDueAsync(false, true), null,
                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30));
        }
        CatalogUpdatePlatformScheduler.Configure(replacePlatformSchedule);
    }

    public static async Task TryRunDueAsync(bool isStartup, bool fromBackground = false,
        CancellationToken cancellationToken = default)
    {
        var schedule = AppOptions.CatalogUpdateSchedule;
        if (schedule == CatalogUpdateSchedule.Manual) return;
        if (schedule == CatalogUpdateSchedule.Startup &&
            (!isStartup || Interlocked.Exchange(ref startupChecked, 1) != 0)) return;
        if (fromBackground && !AppOptions.CatalogUpdateInBackground) return;

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (!CatalogUpdatePolicy.IsDue(schedule, ReadLastUpdate(), now, isStartup)) return;
            if (!CanUseCurrentConnection()) return;

            var accounts = await AppServices.Accounts.LoadAsync().ConfigureAwait(false);
            var added = 0;
            var removed = 0;
            var updated = 0;
            var failed = 0;
            foreach (var account in accounts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var oldCatalogs = await CatalogCacheService.LoadAsync(account.Id).ConfigureAwait(false);
                    var catalogs = await DownloadAllAsync(account, cancellationToken).ConfigureAwait(false);
                    foreach (var (kind, items) in catalogs)
                        await CatalogCacheService.SaveAsync(account.Id, kind, items).ConfigureAwait(false);

                    var changes = CatalogUpdatePolicy.CompareChannels(
                        oldCatalogs.GetValueOrDefault(MediaKind.Channel) ?? [],
                        catalogs.GetValueOrDefault(MediaKind.Channel) ?? [], account.Provider);
                    added += changes.Added;
                    removed += changes.Removed;
                    updated++;
                    var result = new CatalogUpdateResult(account, catalogs, changes, DateTimeOffset.UtcNow);
                    SaveAccountSummary(result);
                    Completed?.Invoke(result);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failed++;
                    System.Diagnostics.Debug.WriteLine($"Automatic catalog update failed for {account.Id}: {ex.GetType().Name}");
                }
            }

            var completedAt = DateTimeOffset.UtcNow;
            Preferences.Default.Set(LastUpdateKey, completedAt.ToUnixTimeSeconds());
            var summary = $"{AppOptions.FormatTime(completedAt)} · +{added} / −{removed} canais";
            if (failed > 0) summary += $" · {failed} lista(s) com erro";
            else if (updated == 0) summary += " · sem listas guardadas";
            Preferences.Default.Set(LastSummaryKey, summary);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"Scheduled catalog update failed: {ex.GetType().Name}");
        }
        finally { gate.Release(); }
    }

    public static string AccountSummary(string accountId) => Preferences.Default.Get(
        $"catalogUpdate.summary.{accountId}", LastSummary);

    private static DateTimeOffset? ReadLastUpdate()
    {
        var seconds = Preferences.Default.Get(LastUpdateKey, 0L);
        if (seconds <= 0) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(seconds); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static bool CanUseCurrentConnection()
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return false;
        return !AppOptions.CatalogUpdateWifiOnly ||
               Connectivity.Current.ConnectionProfiles.Contains(ConnectionProfile.WiFi);
    }

    private static async Task<Dictionary<MediaKind, IReadOnlyList<MediaItem>>> DownloadAllAsync(
        PlaylistAccount account, CancellationToken cancellationToken)
    {
        var result = new Dictionary<MediaKind, IReadOnlyList<MediaItem>>();
        if (account.Provider is ProviderType.M3U or ProviderType.LocalM3U)
        {
            var playlist = await AppServices.Client.LoadM3uAsync(account, cancellationToken).ConfigureAwait(false);
            foreach (var kind in Enum.GetValues<MediaKind>())
                result[kind] = playlist.Items.Where(item => item.Kind == kind).ToArray();
            return result;
        }

        foreach (var kind in Enum.GetValues<MediaKind>())
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            request.CancelAfter(TimeSpan.FromMinutes(3));
            result[kind] = await AppServices.Client.GetCatalogAsync(account, kind, request.Token).ConfigureAwait(false);
        }
        return result;
    }

    private static void SaveAccountSummary(CatalogUpdateResult result) => Preferences.Default.Set(
        $"catalogUpdate.summary.{result.Account.Id}",
        $"{AppOptions.FormatTime(result.CompletedAt)} · +{result.ChannelChanges.Added} / −{result.ChannelChanges.Removed} canais");
}

public static partial class CatalogUpdatePlatformScheduler
{
    public static void Configure(bool replace) => ConfigureCore(replace);
    static partial void ConfigureCore(bool replace);
}

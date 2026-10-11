using Android.Content;
using PSiptv.Core;
using PSiptv.Services;
using System.Security.Cryptography;
using System.Text;

namespace PSiptv;

internal static class AndroidTvDiscovery
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    private static string? pending;
    internal static void Capture(Intent? intent)
    {
        if (intent?.Data?.Scheme == "psiptv" && intent.Data.Host == "watch") pending = intent.Data.ToString();
    }
    internal static string? Consume() => Interlocked.Exchange(ref pending, null);
    internal static string ItemId(MediaItem item) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CatalogPreferences.ItemKey(item))));
    private static string Link(string account, string profile, MediaItem item) => $"psiptv://watch?account={Uri.EscapeDataString(account)}&profile={Uri.EscapeDataString(profile)}&item={ItemId(item)}";
    internal static async Task RefreshAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26) || Android.App.Application.Context.PackageManager?.HasSystemFeature("android.software.leanback") != true) return;
        await gate.WaitAsync();
        try
        {
            var context = Android.App.Application.Context; var resolver = context.ContentResolver!;
            var enabled = Preferences.Default.Get("tv.watchNext", "false") == "true";
            await UserProfileService.LoadAsync();
            var account = AppServices.ActiveAccount ?? (await AppServices.Accounts.LoadAsync()).FirstOrDefault(a => a.Id == Preferences.Default.Get("lastAccountId", ""));
            var profile = UserProfileService.Active.Id; var session = AppServices.SessionVersion;
            var watch = Android.Net.Uri.Parse("content://android.media.tv/watch_next_program")!;
            var preview = Android.Net.Uri.Parse("content://android.media.tv/preview_program")!;
            var channels = Android.Net.Uri.Parse("content://android.media.tv/channel")!;
            var existing = new Dictionary<string, (long Id, bool Visible)>();
            // TvProvider scopes queries to the calling package and rejects SQL selections.
            using (var cursor = resolver.Query(watch, ["_id", "internal_provider_id", "browsable"], null, null, null))
                if (cursor is not null) while (cursor.MoveToNext()) existing[cursor.GetString(1) ?? ""] = (cursor.GetLong(0), cursor.GetInt(2) != 0);
            var keep = new HashSet<string>();
            long channelId = 0;
            using (var cursor = resolver.Query(channels, ["_id", "internal_provider_id"], null, null, null))
                if (cursor is not null) while (cursor.MoveToNext()) if (cursor.GetString(1) == "psiptv-favorites") channelId = cursor.GetLong(0);
            if (enabled && account is not null && !account.IsProtected)
            {
                var library = await ExperienceService.LoadAsync(account.Id, profile);
                var history = await HistoryService.LoadAsync(account.Id);
                var favorites = await FavoritesService.ExportAsync(account.Id, profile);
                var options = System.Text.Json.JsonSerializer.Deserialize<CatalogPreferences>(await SecureStorage.Default.GetAsync($"catalog-options.{account.Id}") ?? "{}") ?? new();
                var candidates = history.Where(e => e.Item.Kind != MediaKind.Channel && !e.Item.HasEpisodes && e.PositionSeconds > 0 && !PodcastFeed.IsPodcast(e.Item))
                    .Select(e => (Item: e.Item, Position: e.PositionSeconds, Time: e.WatchedAt, Type: 0))
                    .Concat(library.Pinned.Where(i => i.Kind != MediaKind.Channel && !i.HasEpisodes && !PodcastFeed.IsPodcast(i))
                        .Select(i => (Item: i, Position: 0d, Time: DateTimeOffset.UtcNow, Type: 3)))
                    .Where(e => library.State(e.Item) != ViewingState.Completed && !options.IsHidden(e.Item) && !options.IsLocked(e.Item))
                    .DistinctBy(e => CatalogPreferences.ItemKey(e.Item)).Take(20).ToArray();
                if (session != AppServices.SessionVersion) return;
                foreach (var entry in candidates)
                {
                    var id = account.Id + ":" + profile + ":" + ItemId(entry.Item); keep.Add(id);
                    if (existing.TryGetValue(id, out var previous) && !previous.Visible) continue;
                    using var values = new ContentValues();
                    values.Put("internal_provider_id", id); values.Put("title", entry.Item.Name);
                    values.Put("intent_uri", Link(account.Id, profile, entry.Item)); values.Put("type", 0);
                    values.Put("watch_next_type", entry.Type); values.Put("last_engagement_time_utc_millis", entry.Time.ToUnixTimeMilliseconds());
                    values.Put("last_playback_position_millis", (long)(entry.Position * 1000));
                    values.Put("poster_art_uri", $"android.resource://{context.PackageName}/mipmap/appicon");
                    if (existing.ContainsKey(id)) resolver.Update(ContentUris.WithAppendedId(watch, previous.Id), values, null, null);
                    else resolver.Insert(watch, values);
                }
                var favoriteChannels = favorites.Select(e => e.Item).Concat(library.Pinned)
                    .Where(i => i.Kind == MediaKind.Channel && !options.IsHidden(i) && !options.IsLocked(i)).DistinctBy(CatalogPreferences.ItemKey).Take(12).ToArray();
                if (channelId == 0 && favoriteChannels.Length > 0)
                {
                    using var values = new ContentValues(); values.Put("type", "TYPE_PREVIEW"); values.Put("display_name", "PSiptv · Favoritos"); values.Put("internal_provider_id", "psiptv-favorites");
                    var created = resolver.Insert(channels, values); if (created is not null) channelId = ContentUris.ParseId(created);
                    if (channelId > 0) Android.Media.TV.TvContract.RequestChannelBrowsable(context, channelId);
                }
                if (channelId > 0)
                {
                    resolver.Delete(preview.BuildUpon()!.AppendQueryParameter("channel", channelId.ToString())!.Build()!, null, null);
                    foreach (var item in favoriteChannels)
                    {
                        using var values = new ContentValues(); values.Put("channel_id", channelId); values.Put("type", 6); values.Put("live", 1);
                        values.Put("title", item.Name); values.Put("internal_provider_id", ItemId(item));
                        values.Put("intent_uri", Link(account.Id, profile, item));
                        values.Put("poster_art_uri", $"android.resource://{context.PackageName}/mipmap/appicon");
                        resolver.Insert(preview, values);
                    }
                }
            }
            else if (channelId > 0) resolver.Delete(ContentUris.WithAppendedId(channels, channelId), null, null);
            foreach (var entry in existing.Where(e => !keep.Contains(e.Key))) resolver.Delete(ContentUris.WithAppendedId(watch, entry.Value.Id), null, null);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        finally { gate.Release(); }
    }
}

[Android.Content.BroadcastReceiver(Enabled = true, Exported = true)]
[Android.App.IntentFilter(["android.media.tv.action.INITIALIZE_PROGRAMS"])]
public sealed class TvDiscoveryReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != "android.media.tv.action.INITIALIZE_PROGRAMS") return;
        var pendingResult = GoAsync();
        _ = Task.Run(async () => { try { await AndroidTvDiscovery.RefreshAsync(); } finally { pendingResult?.Finish(); } });
    }
}

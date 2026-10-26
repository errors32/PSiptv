using System.Globalization;
using System.Text.Json;
using PSiptv.Core;
using PSiptv.Views;

namespace PSiptv.Services;

public static class AppOptions
{
    public static bool Pip => Preferences.Default.Get("pip", true);
    public static string StreamFormat => Preferences.Default.Get("streamFormat", "auto");
    public static bool AutoPlay => Preferences.Default.Get("episodeAutoPlay", false);
    public static bool PodcastAutoPlay => Preferences.Default.Get("podcastAutoPlay", true);
    public static int AutoPlaySeconds => Math.Clamp(Preferences.Default.Get("autoPlaySeconds", 30), 0, 60);
    public static bool AutoClearCache => Preferences.Default.Get("autoClearCache", false);
    public static bool Subtitles => Preferences.Default.Get("subtitles", true);
    public static string SubtitleMode => Preferences.Default.Get("subtitleMode", Subtitles ? "foreign" : "off") is { } mode &&
        mode is "off" or "foreign" or "always" ? mode : "foreign";
    public static string PreferredAudioLanguage => ReadTrackLanguage("preferredAudioLanguage");
    public static string PreferredSubtitleLanguage => ReadTrackLanguage("preferredSubtitleLanguage");
    public static bool RememberTrackSelection => Preferences.Default.Get("rememberTrackSelection", true);
    public static int AudioDelayMs => Math.Clamp(Preferences.Default.Get("audioDelayMs", 0), -5000, 5000);
    public static int SubtitleDelayMs => Math.Clamp(Preferences.Default.Get("subtitleDelayMs", 0), -5000, 5000);
    public static int ReminderMinutesBefore => int.TryParse(
        Preferences.Default.Get("reminderMinutesBefore", "5"), out var minutes) ? Math.Clamp(minutes, 0, 60) : 5;
    public static bool TimeshiftEnabled => Preferences.Default.Get("timeshiftEnabled", true);
    public static int TimeshiftMinutes => int.TryParse(Preferences.Default.Get("timeshiftMinutes", "30"), out var minutes)
        ? Math.Clamp(minutes, 5, 120) : 30;
    public static bool Clock12 => Preferences.Default.Get("clock12", false);
    public static string UserAgent => Preferences.Default.Get("userAgent", "PSiptv/1.0");
    public static int BufferSeconds => Math.Clamp(Preferences.Default.Get("bufferSeconds", 5), 1, 60);
    public static bool NetworkSpeed => Preferences.Default.Get("networkSpeed", false);
    public static string Decoder => Preferences.Default.Get("decoder", "auto");
    public static bool OpenSl => Preferences.Default.Get("openSl", false);
    public static bool OpenGl => Preferences.Default.Get("openGl", false);
    public static CatalogUpdateSchedule CatalogUpdateSchedule =>
        Preferences.Default.Get("catalogUpdateSchedule", "manual") switch
        {
            "startup" => CatalogUpdateSchedule.Startup,
            "daily" => CatalogUpdateSchedule.Daily,
            "weekly" => CatalogUpdateSchedule.Weekly,
            _ => CatalogUpdateSchedule.Manual
        };
    public static bool CatalogUpdateWifiOnly => Preferences.Default.Get("catalogUpdateWifiOnly", false);
    public static bool CatalogUpdateInBackground => Preferences.Default.Get("catalogUpdateInBackground", false);
    public static string VideoAspectRatio
    {
        get => ReadAspectRatio("videoAspectRatio", "fit");
    }
    public static string FullscreenVideoAspectRatio => ReadAspectRatio("lastVideoAspectRatio",
        ReadAspectRatio("fullscreenVideoAspectRatio", VideoAspectRatio));
    private static string ReadAspectRatio(string key, string fallback)
    {
        var value = Preferences.Default.Get(key, fallback);
        return value is "fit" or "fill" or "stretch" or "16:9" or "4:3" or "10:9" or "21:9" ? value : fallback;
    }
    private static string ReadTrackLanguage(string key)
    {
        var value = Preferences.Default.Get(key, "system");
        return value is "system" or "pt" or "en" or "es" or "fr" or "de" or "it" ? value : "system";
    }
    public static string FormatTime(DateTimeOffset time) => time.ToLocalTime().ToString(Clock12 ? "dd/MM hh:mm tt" : "dd/MM HH:mm", CultureInfo.CurrentCulture);
}

public static class CatalogOptionsService
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    private static string? accountId;
    private static CatalogPreferences options = new();
    public static CatalogPreferences Current => AppServices.ActiveAccount?.Id == accountId ? options : new();
    public static readonly Dictionary<MediaKind, IReadOnlyList<MediaItem>> Catalogs = [];
    public static event Action? Changed;
    public static async Task LoadAsync(string id, Action<MediaKind>? onCatalogLoaded = null,
        MediaKind? preferredKind = null)
    {
        var sessionVersion = AppServices.SessionVersion;
        await gate.WaitAsync();
        try
        {
            var json = await SecureStorage.Default.GetAsync($"catalog-options.{id}");
            if (AppServices.SessionVersion != sessionVersion || AppServices.ActiveAccount?.Id != id) return;
            options = json is null ? new() : JsonSerializer.Deserialize<CatalogPreferences>(json) ?? new();
            accountId = id;
            Catalogs.Clear();
            await CatalogCacheService.LoadAsync(id, (kind, items) => MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (AppServices.SessionVersion != sessionVersion || AppServices.ActiveAccount?.Id != id) return;
                Catalogs[kind] = items;
                onCatalogLoaded?.Invoke(kind);
            }), preferredKind);
        }
        finally { gate.Release(); }
    }
    public static async Task SaveAsync()
    {
        var id = AppServices.ActiveAccount?.Id;
        if (id is null || id != accountId) return;
        await gate.WaitAsync();
        try { await SecureStorage.Default.SetAsync($"catalog-options.{id}", JsonSerializer.Serialize(options)); }
        finally { gate.Release(); }
        Changed?.Invoke();
    }
    public static async Task<bool> AuthorizePlaybackAsync(Page owner, MediaItem item)
    {
        if (AppServices.ActiveAccount is not { } account) return false;
        if (!Current.IsLocked(item)) return true;
        if (Current.ParentalPinHash.Length == 0) return false;
        var version = AppServices.SessionVersion;
        var allowed = await PinPage.AuthorizeAsync(owner, new PlaylistAccount
        {
            Id = account.Id + ".parental", Name = "Controlo parental", PinHash = Current.ParentalPinHash
        }, allowBiometrics: false);
        return allowed && version == AppServices.SessionVersion && AppServices.ActiveAccount?.Id == account.Id;
    }
}

public static class HistoryService
{
    private static readonly Dictionary<string, int> counts = [];
    private static readonly Dictionary<string, IReadOnlyList<WatchEntry>> cached = [];
    public static event Action? Changed;
    public static IReadOnlyList<MediaItem> OrderFavoriteChannels(IEnumerable<MediaItem> items) =>
        AppServices.ActiveAccount is { } account
            ? FavoriteOrdering.ChannelsByRecency(items,
                cached.GetValueOrDefault(UserProfileService.Scope(account.Id)) ?? [], account)
            : items.ToArray();
    public static int Count => AppServices.ActiveAccount is { } a
        ? counts.GetValueOrDefault(UserProfileService.Scope(a.Id)) : 0;
    private static readonly SemaphoreSlim gate = new(1, 1);
    public static async Task<List<WatchEntry>> LoadAsync(string id)
    {
        var scope = UserProfileService.Scope(id);
        var json = await SecureStorage.Default.GetAsync($"watch-history.{scope}");
        var result = json is null ? new List<WatchEntry>() : JsonSerializer.Deserialize<List<WatchEntry>>(json) ?? [];
        cached[scope] = result;
        counts[scope] = result.Count; return result;
    }
    public static async Task RecordAsync(string id, int version, MediaItem item, double position)
    {
        if (PodcastFeed.IsPodcast(item)) return;
        await gate.WaitAsync();
        try
        {
            if (AppServices.ActiveAccount?.Id != id || AppServices.SessionVersion != version) return;
            var scope = UserProfileService.Scope(id);
            var entries = await LoadAsync(id);
            if (AppServices.ActiveAccount?.Id != id || AppServices.SessionVersion != version) return;
            var channelOrderChanged = item.Kind == MediaKind.Channel &&
                (entries.FirstOrDefault(e => e.Item.Kind == MediaKind.Channel) is not { } latest ||
                 CatalogPreferences.ItemKey(latest.Item) != CatalogPreferences.ItemKey(item));
            entries.RemoveAll(e => CatalogPreferences.ItemKey(e.Item) == CatalogPreferences.ItemKey(item));
            entries.Insert(0, new(item, DateTimeOffset.UtcNow, Math.Max(0, position)));
            await SecureStorage.Default.SetAsync($"watch-history.{scope}", JsonSerializer.Serialize(entries.Take(100)));
            cached[scope] = entries.Take(100).ToArray();
            counts[scope] = Math.Min(100, entries.Count);
            if (channelOrderChanged) Changed?.Invoke();
            _ = TvDiscoveryService.RefreshAsync();
        }
        finally { gate.Release(); }
    }
    public static async Task ClearAsync(string id)
    {
        var scope = UserProfileService.Scope(id);
        await gate.WaitAsync();
        try { SecureStorage.Default.Remove($"watch-history.{scope}"); counts[scope] = 0; cached.Remove(scope); Changed?.Invoke(); }
        finally { gate.Release(); }
    }
    public static async Task DeleteAccountAsync(string id)
    {
        await gate.WaitAsync();
        try
        {
            foreach (var profile in UserProfileService.Profiles)
            {
                var scope = UserProfileService.Scope(id, profile.Id);
                SecureStorage.Default.Remove($"watch-history.{scope}");
                counts.Remove(scope);
                cached.Remove(scope);
            }
        }
        finally { gate.Release(); }
    }
    public static async Task DeleteProfileAsync(string id, string profileId)
    {
        var scope = UserProfileService.Scope(id, profileId);
        await gate.WaitAsync();
        try { SecureStorage.Default.Remove($"watch-history.{scope}"); counts.Remove(scope); cached.Remove(scope); }
        finally { gate.Release(); }
    }
    public static async Task<IReadOnlyList<WatchEntry>> ExportAsync(string id, string profileId)
    {
        var scope = UserProfileService.Scope(id, profileId);
        var json = await SecureStorage.Default.GetAsync($"watch-history.{scope}");
        return json is null ? [] : JsonSerializer.Deserialize<List<WatchEntry>>(json) ?? [];
    }
    public static async Task ImportAsync(string id, string profileId, IReadOnlyList<WatchEntry> entries)
    {
        var scope = UserProfileService.Scope(id, profileId);
        await gate.WaitAsync();
        try
        {
            await SecureStorage.Default.SetAsync($"watch-history.{scope}", JsonSerializer.Serialize(entries.Take(100)));
            cached[scope] = entries.Take(100).ToArray();
            counts[scope] = Math.Min(100, entries.Count);
            Changed?.Invoke();
        }
        finally { gate.Release(); }
    }
}

public static class EpgService
{
    private static readonly TimeSpan freshness = TimeSpan.FromHours(6);
    private static readonly object sync = new();
    private static readonly Dictionary<string, EpgCacheEntry> cache = [];
    private static readonly Dictionary<string, SemaphoreSlim> refreshGates = [];
    private static readonly Dictionary<string, MediaItem> trackedXtreamChannels = [];
    private static Timer? backgroundTimer;
    private static int cacheGeneration;

    public static DateTimeOffset? LastUpdated(string id)
    {
        DateTimeOffset? memory;
        lock (sync)
            memory = cache.Where(pair => pair.Key.StartsWith(id + ":", StringComparison.Ordinal))
                .Select(pair => (DateTimeOffset?)pair.Value.Updated).Max();
        var persisted = EpgCacheService.LastUpdated(id);
        return memory is null || persisted > memory ? persisted : memory;
    }

    public static void Clear()
    {
        lock (sync) { cacheGeneration++; cache.Clear(); trackedXtreamChannels.Clear(); }
        EpgCacheService.Clear();
    }

    public static async Task DeleteAsync(string accountId)
    {
        lock (sync)
        {
            cacheGeneration++;
            foreach (var key in cache.Keys.Where(key => key.StartsWith(accountId + ":", StringComparison.Ordinal)).ToArray())
                cache.Remove(key);
            foreach (var key in trackedXtreamChannels.Keys.Where(key => key.StartsWith(accountId + ":", StringComparison.Ordinal)).ToArray())
                trackedXtreamChannels.Remove(key);
        }
        await EpgCacheService.DeleteAsync(accountId);
    }

    public static void RefreshInBackground(PlaylistAccount account, IReadOnlyList<MediaItem> channels)
    {
        // XMLTV contains the guide for every channel, so one refresh warms the
        // complete playlist. Xtream's short guide is a request per channel and
        // is refreshed on demand to avoid thousands of background requests.
        if (IsXtreamShortGuide(account))
        {
            MediaItem[] tracked;
            lock (sync) tracked = trackedXtreamChannels
                .Where(pair => pair.Key.StartsWith(account.Id + ":", StringComparison.Ordinal))
                .Select(pair => pair.Value).Take(20).ToArray();
            foreach (var channel in tracked) _ = RefreshIfStaleAsync(account, channel);
            return;
        }
        var sample = channels.FirstOrDefault(channel => channel.Kind == MediaKind.Channel && channel.EpgId.Length > 0);
        if (sample is null) return;
        _ = RefreshIfStaleAsync(account, sample);
    }

    public static void StartBackgroundUpdates()
    {
        lock (sync) backgroundTimer ??= new Timer(_ => RefreshActiveInBackground(), null,
            TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30));
    }

    public static void RefreshActiveInBackground()
    {
        var account = AppServices.ActiveAccount;
        if (account is null) return;
        var channels = CatalogOptionsService.Catalogs.GetValueOrDefault(MediaKind.Channel) ?? [];
        RefreshInBackground(account, channels);
    }

    public static async Task<IReadOnlyList<TvProgramme>> GetAsync(PlaylistAccount account, MediaItem channel, CancellationToken token, bool refresh = false)
    {
        var config = AppServices.ActiveAccount?.Id == account.Id ? CatalogOptionsService.Current.Epg :
            System.Text.Json.JsonSerializer.Deserialize<CatalogPreferences>(await SecureStorage.Default.GetAsync($"catalog-options.{account.Id}") ?? "{}")?.Epg ?? new();
        if (config.Sources.Count > 0) return await GetConfiguredAsync(account, channel, config, token, refresh);
        var key = SourceKey(account, channel);
        if (IsXtreamShortGuide(account)) lock (sync) trackedXtreamChannels[key] = channel;
        var saved = await LoadAsync(account.Id, key);
        if (!refresh && saved is not null)
        {
            if (DateTimeOffset.UtcNow - saved.Updated >= freshness)
                _ = RefreshIfStaleAsync(account, channel);
            return SelectChannel(saved.Items, account, channel);
        }

        var updated = await RefreshCoreAsync(account, channel, key, token, refresh);
        token.ThrowIfCancellationRequested();
        return SelectChannel(updated.Items, account, channel);
    }

    private static async Task<IReadOnlyList<TvProgramme>> GetConfiguredAsync(PlaylistAccount account, MediaItem channel,
        EpgConfiguration config, CancellationToken token, bool refresh)
    {
        var guides = await GetConfiguredSourcesAsync(account, config, token, refresh);
        var earliest = channel.HasCatchup ? DateTimeOffset.Now.AddDays(-channel.CatchupDays) : DateTimeOffset.Now;
        return config.Select(channel, guides).Where(p => p.End >= earliest).ToArray();
    }

    public static async Task<IReadOnlyList<TvProgramme>> GetFullGuideAsync(PlaylistAccount account,
        IReadOnlyList<MediaItem> channels, CancellationToken token)
    {
        var config = (System.Text.Json.JsonSerializer.Deserialize<CatalogPreferences>(
            await SecureStorage.Default.GetAsync($"catalog-options.{account.Id}") ?? "{}") ?? new()).Epg;
        if (config.Sources.Count == 0) return await AppServices.Client.GetFullGuideAsync(account, token);
        var guides = await GetConfiguredSourcesAsync(account, config, token, false);
        return await Task.Run(() =>
        {
            var indexed = guides.Select(guide => guide.GroupBy(p => p.ChannelId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<TvProgramme>)g.ToArray())).ToArray();
            return channels.SelectMany(channel =>
            {
                token.ThrowIfCancellationRequested();
                var id = channel.EpgId.Length > 0 ? channel.EpgId : channel.Id;
                var sourceId = config.Mappings.GetValueOrDefault(CatalogPreferences.ItemKey(channel))?.ChannelId ?? channel.EpgId;
                var selected = indexed.Select(source => source.GetValueOrDefault(sourceId) ?? []).ToArray();
                return config.Select(channel, selected).Select(p => p with { ChannelId = id });
            }).DistinctBy(p => (p.ChannelId, p.Title, p.Start, p.End)).ToArray();
        }, token);
    }

    private static async Task<IReadOnlyList<IReadOnlyList<TvProgramme>>> GetConfiguredSourcesAsync(PlaylistAccount account,
        EpgConfiguration config, CancellationToken token, bool refresh)
    {
        var guides = new List<IReadOnlyList<TvProgramme>>();
        var successful = 0;
        int generation;
        lock (sync) generation = cacheGeneration;
        foreach (var source in config.Sources.Take(8))
        {
            token.ThrowIfCancellationRequested();
            var key = $"{account.Id}:additional-xmltv:{source.Url}";
            var saved = await LoadAsync(account.Id, key);
            if (refresh || saved is null || DateTimeOffset.UtcNow - saved.Updated > freshness)
            {
                SemaphoreSlim semaphore;
                lock (sync) { if (!refreshGates.TryGetValue(key, out semaphore!)) refreshGates[key] = semaphore = new(1, 1); }
                await semaphore.WaitAsync(token);
                try
                {
                    saved = await LoadAsync(account.Id, key);
                    if (refresh || saved is null || DateTimeOffset.UtcNow - saved.Updated > freshness)
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                        deadline.CancelAfter(TimeSpan.FromSeconds(45));
                        var xml = await AppServices.Client.DownloadGuideAsync(account, source.Url, deadline.Token);
                        var programmes = await Task.Run(() => XmlTvParser.Parse(xml), deadline.Token);
                        saved = new(DateTimeOffset.UtcNow, programmes);
                        lock (sync)
                        {
                            if (generation != cacheGeneration) throw new OperationCanceledException();
                            cache[key] = saved;
                        }
                        await EpgCacheService.SaveAsync(account.Id, key, saved);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested || generation != cacheGeneration) { throw; }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                finally { semaphore.Release(); }
            }
            if (saved is not null) successful++;
            guides.Add(saved?.Items ?? []);
        }
        if (successful == 0) throw new InvalidOperationException("Nenhuma fonte XMLTV respondeu e não existe guia em cache.");
        return guides;
    }

    private static async Task RefreshIfStaleAsync(PlaylistAccount account, MediaItem channel)
    {
        try
        {
            var key = SourceKey(account, channel);
            var saved = await LoadAsync(account.Id, key);
            if (saved is not null && DateTimeOffset.UtcNow - saved.Updated < freshness) return;
            await RefreshCoreAsync(account, channel, key, CancellationToken.None, true);
        }
        catch { /* A background refresh must never interrupt navigation or playback. */ }
    }

    private static async Task<EpgCacheEntry?> LoadAsync(string accountId, string key)
    {
        lock (sync) if (cache.TryGetValue(key, out var memory)) return memory;
        var persisted = await EpgCacheService.LoadAsync(accountId, key);
        if (persisted is not null) lock (sync) cache[key] = persisted;
        return persisted;
    }

    private static async Task<EpgCacheEntry> RefreshCoreAsync(PlaylistAccount account, MediaItem channel,
        string key, CancellationToken token, bool force)
    {
        SemaphoreSlim refreshGate;
        int requestGeneration;
        lock (sync)
        {
            requestGeneration = cacheGeneration;
            if (!refreshGates.TryGetValue(key, out refreshGate!))
                refreshGates[key] = refreshGate = new SemaphoreSlim(1, 1);
        }
        await refreshGate.WaitAsync(token);
        try
        {
            var saved = await LoadAsync(account.Id, key);
            if (!force && saved is not null && DateTimeOffset.UtcNow - saved.Updated < freshness) return saved;

            IReadOnlyList<TvProgramme> result;
            if (IsXtreamShortGuide(account))
            {
                var earliest = channel.HasCatchup ? DateTimeOffset.Now.AddDays(-channel.CatchupDays) : DateTimeOffset.Now;
                result = (await AppServices.Client.GetShortGuideAsync(account, channel, token))
                    .Where(programme => programme.End >= earliest).OrderBy(programme => programme.Start).ToArray();
            }
            else
            {
                var url = account.EpgUrl;
                if (url.Length == 0 && account.Provider is ProviderType.M3U or ProviderType.LocalM3U)
                    url = (await AppServices.Client.LoadM3uAsync(account, token)).EpgUrl;
                if (url.Length == 0 && account.Provider == ProviderType.Tvheadend)
                    url = account.Url.TrimEnd('/') + "/xmltv/channels";
                if (url.Length == 0) throw new InvalidOperationException("Esta lista não tem fonte EPG. Adicione um endereço XMLTV nas configurações.");
                if (channel.EpgId.Length == 0) throw new InvalidOperationException("Este canal não tem tvg-id para associar ao guia XMLTV.");
                var xml = await AppServices.Client.DownloadGuideAsync(account, url, token);
                var now = DateTimeOffset.Now;
                result = (await Task.Run(() => XmlTvParser.Parse(xml), token))
                    .Where(programme => programme.End >= now.AddDays(-30) && programme.Start <= now.AddDays(8))
                    .OrderBy(programme => programme.Start).ToArray();
            }

            token.ThrowIfCancellationRequested();
            var updated = new EpgCacheEntry(DateTimeOffset.UtcNow, result);
            lock (sync)
            {
                if (requestGeneration != cacheGeneration) throw new OperationCanceledException();
                cache[key] = updated;
            }
            try { await EpgCacheService.SaveAsync(account.Id, key, updated); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
            return updated;
        }
        finally { refreshGate.Release(); }
    }

    private static IReadOnlyList<TvProgramme> SelectChannel(IReadOnlyList<TvProgramme> items,
        PlaylistAccount account, MediaItem channel)
    {
        var earliest = channel.HasCatchup ? DateTimeOffset.Now.AddDays(-channel.CatchupDays) : DateTimeOffset.Now;
        var selected = IsXtreamShortGuide(account) ? items : items.Where(programme => programme.ChannelId == channel.EpgId);
        return selected.Where(programme => programme.End >= earliest).OrderBy(programme => programme.Start).ToArray();
    }

    private static bool IsXtreamShortGuide(PlaylistAccount account) =>
        account.Provider == ProviderType.Xtream && account.EpgUrl.Length == 0;

    private static string SourceKey(PlaylistAccount account, MediaItem channel) => IsXtreamShortGuide(account)
        ? $"{account.Id}:xtream:{account.Url}:{channel.Id}"
        : $"{account.Id}:xmltv:{account.Url}:{account.EpgUrl}";
}

public static class CacheService
{
    public static void Clear()
    {
        EpgService.Clear();
        // Only files owned by this app; leave recent playlists available to external players.
        PlaybackService.CleanOldPlaylists();
        var folder = Path.Combine(FileSystem.CacheDirectory, "logos");
        if (Directory.Exists(folder)) foreach (var file in Directory.EnumerateFiles(folder))
        {
            try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

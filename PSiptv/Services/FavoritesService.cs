using PSiptv.Core;

namespace PSiptv.Services;

public static class FavoritesService
{
    private static readonly FavoriteStore Store = new(
        key => SecureStorage.Default.GetAsync(key),
        (key, value) => SecureStorage.Default.SetAsync(key, value),
        key => { SecureStorage.Default.Remove(key); return Task.CompletedTask; });
    private static string? loadedScope;
    private static IReadOnlyList<FavoriteEntry> entries = [];
    private static HashSet<string> keys = [];
    public static event Action? Changed;
    public static IReadOnlyList<MediaItem> Items => AppServices.ActiveAccount is { } account &&
        loadedScope == UserProfileService.Scope(account.Id)
        ? entries.Select(e => e.Item).ToArray() : [];

    public static bool Contains(MediaItem item) => AppServices.ActiveAccount is { } account
        && loadedScope == UserProfileService.Scope(account.Id) && keys.Contains(FavoriteStore.ItemKey(account, item));

    public static async Task LoadAsync()
    {
        if (AppServices.ActiveAccount is not { } account) return;
        var version = AppServices.SessionVersion;
        var scope = UserProfileService.Scope(account.Id);
        var saved = await Store.LoadAsync(scope);
        if (AppServices.ActiveAccount?.Id == account.Id && AppServices.SessionVersion == version) Apply(scope, saved);
    }

    public static async Task ToggleAsync(MediaItem item)
    {
        if (AppServices.ActiveAccount is not { } account) return;
        var version = AppServices.SessionVersion;
        var scope = UserProfileService.Scope(account.Id);
        var saved = await Store.SetAsync(account, item, !Contains(item), scope);
        if (AppServices.ActiveAccount?.Id == account.Id && AppServices.SessionVersion == version) Apply(scope, saved);
    }

    public static async Task<FavoriteSnapshot?> SnapshotAsync()
    {
        if (AppServices.ActiveAccount is not { } account) return null;
        return await Store.SnapshotAsync(UserProfileService.Scope(account.Id));
    }

    public static async Task SetHeardAsync(MediaItem item, bool heard, string? accountId = null, int? session = null)
    {
        if (AppServices.ActiveAccount is not { } account ||
            accountId is not null && account.Id != accountId ||
            session is not null && AppServices.SessionVersion != session) return;
        await Store.SetHeardAsync(UserProfileService.Scope(account.Id), item, heard);
        Changed?.Invoke();
    }

    public static async Task<bool> ImportNewerAsync(PlaylistAccount account, string profileId, FavoriteSnapshot snapshot)
    {
        var updated = await Store.ImportNewerAsync(UserProfileService.Scope(account.Id, profileId), account, snapshot);
        if (updated && AppServices.ActiveAccount?.Id == account.Id && UserProfileService.Active.Id == profileId)
            await LoadAsync();
        return updated;
    }

    public static async Task SetAllHeardAsync(IReadOnlyList<MediaItem> items)
    {
        if (AppServices.ActiveAccount is not { } account) return;
        var scope = UserProfileService.Scope(account.Id);
        await Store.SetHeardAsync(scope, items, true);
        if (UserProfileService.Scope(AppServices.ActiveAccount?.Id ?? "") == scope) Changed?.Invoke();
    }

    public static async Task DeleteAsync(string accountId)
    {
        foreach (var profile in UserProfileService.Profiles)
            await Store.DeleteAsync(UserProfileService.Scope(accountId, profile.Id));
    }
    public static Task DeleteProfileAsync(string accountId, string profileId) =>
        Store.DeleteAsync(UserProfileService.Scope(accountId, profileId));
    public static Task<IReadOnlyList<FavoriteEntry>> ExportAsync(string accountId, string profileId) =>
        Store.LoadAsync(UserProfileService.Scope(accountId, profileId));
    public static Task<FavoriteSnapshot> ExportSnapshotAsync(string accountId, string profileId) =>
        Store.SnapshotAsync(UserProfileService.Scope(accountId, profileId));
    public static Task ImportAsync(string accountId, string profileId, IReadOnlyList<FavoriteEntry> entries,
        IReadOnlyDictionary<string, PodcastHeardEntry>? heard = null) =>
        Store.ReplaceAsync(UserProfileService.Scope(accountId, profileId), entries, heard);
    public static void Clear() => Apply(null, []);
    private static void Apply(string? scope, IReadOnlyList<FavoriteEntry> saved)
    {
        loadedScope = scope; entries = saved; keys = saved.Select(e => e.Key).ToHashSet();
        Changed?.Invoke();
    }
}

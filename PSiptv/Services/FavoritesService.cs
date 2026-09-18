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

    public static async Task DeleteAsync(string accountId)
    {
        foreach (var profile in UserProfileService.Profiles)
            await Store.DeleteAsync(UserProfileService.Scope(accountId, profile.Id));
    }
    public static Task DeleteProfileAsync(string accountId, string profileId) =>
        Store.DeleteAsync(UserProfileService.Scope(accountId, profileId));
    public static Task<IReadOnlyList<FavoriteEntry>> ExportAsync(string accountId, string profileId) =>
        Store.LoadAsync(UserProfileService.Scope(accountId, profileId));
    public static Task ImportAsync(string accountId, string profileId, IReadOnlyList<FavoriteEntry> entries) =>
        Store.ReplaceAsync(UserProfileService.Scope(accountId, profileId), entries);
    public static void Clear() => Apply(null, []);
    private static void Apply(string? scope, IReadOnlyList<FavoriteEntry> saved)
    {
        loadedScope = scope; entries = saved; keys = saved.Select(e => e.Key).ToHashSet();
        Changed?.Invoke();
    }
}

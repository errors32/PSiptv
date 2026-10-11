using System.Text.Json;
using PSiptv.Core;

namespace PSiptv.Services;

public static class ExperienceService
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    public static event Action? Changed;
    public static readonly string[] HomeSections = ["Fixados", "Continuar a ver", "Canais recentes", "Favoritos", "A começar", "Gravações recentes"];
    public static readonly string[] OptionKeys = ["ui.textScale", "ui.density", "ui.reduceMotion", "channels.fallback", "channels.quality", "dvr.before", "dvr.after", "dvr.limit", "dvr.retention", "tv.watchNext", "shortcut.search", "shortcut.home", "shortcut.fullscreen"];
    public static double TextScale => double.TryParse(Preferences.Default.Get("ui.textScale", "1"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, .85, 1.4) : 1;
    public static bool Compact => Preferences.Default.Get("ui.density", "normal") == "compact";
    public static bool ReduceMotion => Preferences.Default.Get("ui.reduceMotion", "false") == "true";
    public static bool AutoFallback => Preferences.Default.Get("channels.fallback", "false") == "true";
    public static int Number(string key, int fallback) => int.TryParse(Preferences.Default.Get(key, fallback.ToString()), out var v) ? v : fallback;
    private static string Key(string account, string profile) => "experience." + ProfileStorageScope.ForAccount(account, profile);
    public static async Task<PersonalLibrary> LoadAsync(string account, string? profile = null)
    {
        var json = await SecureStorage.Default.GetAsync(Key(account, profile ?? UserProfileService.Active.Id));
        return json is null ? new() : JsonSerializer.Deserialize<PersonalLibrary>(json) ?? new();
    }
    public static async Task ChangeAsync(Action<PersonalLibrary> update)
    {
        if (AppServices.ActiveAccount is not { } account) throw new InvalidOperationException("Abra uma lista.");
        var profile = UserProfileService.Active.Id;
        var version = AppServices.SessionVersion;
        await gate.WaitAsync();
        try
        {
            var data = await LoadAsync(account.Id, profile);
            if (version != AppServices.SessionVersion) throw new OperationCanceledException();
            update(data);
            await SecureStorage.Default.SetAsync(Key(account.Id, profile), JsonSerializer.Serialize(data));
        }
        finally { gate.Release(); }
        Changed?.Invoke();
        _ = TvDiscoveryService.RefreshAsync();
    }
    public static async Task ImportAsync(string account, string profile, PersonalLibrary imported)
    {
        await gate.WaitAsync();
        try { await SecureStorage.Default.SetAsync(Key(account, profile), JsonSerializer.Serialize(PersonalLibrary.Merge(await LoadAsync(account, profile), imported))); }
        finally { gate.Release(); }
        Changed?.Invoke();
    }
    public static Dictionary<string, string> ExportOptions() => OptionKeys.Where(k => Preferences.Default.ContainsKey(k)).ToDictionary(k => k, k => Preferences.Default.Get(k, ""));
    public static void ImportOptions(IReadOnlyDictionary<string, string>? options)
    {
        if (options is null) return;
        foreach (var pair in options.Where(p => OptionKeys.Contains(p.Key))) Preferences.Default.Set(pair.Key, pair.Value);
        Changed?.Invoke();
    }
    public static async Task DeleteAsync(string account, string profile)
    {
        await gate.WaitAsync();
        try { SecureStorage.Default.Remove(Key(account, profile)); }
        finally { gate.Release(); }
        PlaybackDiagnosticsService.Delete(account, profile);
    }
}

public static class PlaybackDiagnosticsService
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    public static void Delete(string account, string profile) => SecureStorage.Default.Remove("playback-diagnostics." + ProfileStorageScope.ForAccount(account, profile));
    public static async Task<IReadOnlyList<PlaybackDiagnostic>> LoadAsync(string account, string profile)
    {
        var json = await SecureStorage.Default.GetAsync("playback-diagnostics." + ProfileStorageScope.ForAccount(account, profile));
        return json is null ? [] : JsonSerializer.Deserialize<List<PlaybackDiagnostic>>(json) ?? [];
    }
    public static async Task SaveAsync(PlaybackDiagnostic entry)
    {
        if (entry.AccountId.Length == 0) return;
        await gate.WaitAsync();
        try
        {
            var existing = await LoadAsync(entry.AccountId, entry.ProfileId);
            await SecureStorage.Default.SetAsync("playback-diagnostics." + ProfileStorageScope.ForAccount(entry.AccountId, entry.ProfileId),
                JsonSerializer.Serialize(existing.Where(e => e.Id != entry.Id).Prepend(entry).Take(100)));
        }
        finally { gate.Release(); }
    }
}

public static partial class TvDiscoveryService
{
    public static Task RefreshAsync() => RefreshCoreAsync();
    private static Task RefreshCoreAsync()
    {
#if ANDROID
        return PSiptv.AndroidTvDiscovery.RefreshAsync();
#else
        return Task.CompletedTask;
#endif
    }
}

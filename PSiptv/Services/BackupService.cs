using System.Text.Json;
using PSiptv.Core;

namespace PSiptv.Services;

public static class BackupService
{
    private const int Version = 1;
    public const int MaximumFileSize = 50_000_000;

    public static async Task<byte[]> CreateAsync()
    {
        await UserProfileService.LoadAsync();
        var accounts = await AppServices.Accounts.LoadAsync();
        var personal = new List<ProfileBackupData>();
        foreach (var profile in UserProfileService.Profiles)
        foreach (var account in accounts)
        {
            var favorites = await FavoritesService.ExportAsync(account.Id, profile.Id);
            var history = await HistoryService.ExportAsync(account.Id, profile.Id);
            var reminders = await ReminderService.ExportAsync(account.Id, profile.Id);
            var seriesRules = await DvrService.ExportSeriesRulesAsync(account.Id, profile.Id);
            if (favorites.Count > 0 || history.Count > 0 || reminders.Count > 0 || seriesRules.Count > 0)
                personal.Add(new(profile.Id, account.Id, favorites, history, reminders, seriesRules));
        }

        var catalogOptions = new Dictionary<string, string>();
        foreach (var account in accounts)
            if (await SecureStorage.Default.GetAsync($"catalog-options.{account.Id}") is { } json)
                catalogOptions[account.Id] = json;

        var document = new BackupDocument(Version, DateTimeOffset.UtcNow, accounts,
            UserProfileService.Profiles.ToArray(),
            personal, catalogOptions, ReadSettings(), BrowserSourcesService.Sources,
            BrowserSourcesService.Default?.Id ?? "");
        return await Task.Run(() => JsonSerializer.SerializeToUtf8Bytes(document));
    }

    public static async Task RestoreAsync(byte[] backupData)
    {
        if (backupData.Length > MaximumFileSize)
            throw new InvalidOperationException("A cópia de segurança excede o limite de 50 MB.");
        BackupDocument document;
        try
        {
            document = await Task.Run(() => JsonSerializer.Deserialize<BackupDocument>(backupData))
                ?? throw new InvalidOperationException("A cópia de segurança está vazia.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Não foi possível ler a cópia de segurança.", ex);
        }
        if (document.Version != Version)
            throw new InvalidOperationException("Esta versão da cópia de segurança não é suportada.");

        await UserProfileService.MergeAsync(document.Profiles);
        foreach (var account in document.Accounts) await AppServices.Accounts.SaveAsync(account);
        var profileIds = UserProfileService.Profiles.Select(profile => profile.Id).ToHashSet();
        var accountIds = document.Accounts.Select(account => account.Id).ToHashSet();
        foreach (var data in document.PersonalData.Where(data =>
                     profileIds.Contains(data.ProfileId) && accountIds.Contains(data.AccountId)))
        {
            var localFavorites = await FavoritesService.ExportAsync(data.AccountId, data.ProfileId);
            var mergedFavorites = localFavorites.Concat(data.Favorites)
                .GroupBy(entry => entry.Key).Select(group => group.Last()).ToArray();
            await FavoritesService.ImportAsync(data.AccountId, data.ProfileId, mergedFavorites);
            var localHistory = await HistoryService.ExportAsync(data.AccountId, data.ProfileId);
            var mergedHistory = localHistory.Concat(data.History)
                .GroupBy(entry => CatalogPreferences.ItemKey(entry.Item))
                .Select(group => group.OrderByDescending(entry => entry.WatchedAt).First())
                .OrderByDescending(entry => entry.WatchedAt).Take(100).ToArray();
            await HistoryService.ImportAsync(data.AccountId, data.ProfileId, mergedHistory);
            await ReminderService.ImportAsync(data.AccountId, data.ProfileId, data.Reminders ?? []);
            await DvrService.ImportSeriesRulesAsync(data.AccountId, data.ProfileId, data.SeriesRules ?? []);
        }
        foreach (var (accountId, options) in document.CatalogOptions)
            if (accountIds.Contains(accountId))
                await SecureStorage.Default.SetAsync($"catalog-options.{accountId}", options);
        var browserSources = BrowserSourcesService.Sources.Concat(document.BrowserSources)
            .GroupBy(source => source.Id).Select(group => group.Last()).ToArray();
        BrowserSourcesService.Replace(browserSources, document.DefaultBrowserSourceId);
        ApplySettings(document.Settings, accountIds);
        AppServices.Lock();
    }

    private static BackupSettings ReadSettings() => new(
        Preferences.Default.Get("pip", true), Preferences.Default.Get("streamFormat", "auto"),
        Preferences.Default.Get("episodeAutoPlay", false), Preferences.Default.Get("autoPlaySeconds", 30),
        Preferences.Default.Get("autoClearCache", false), Preferences.Default.Get("subtitles", true),
        Preferences.Default.Get("clock12", false), Preferences.Default.Get("userAgent", "PSiptv/1.0"),
        Preferences.Default.Get("bufferSeconds", 5), Preferences.Default.Get("networkSpeed", false),
        Preferences.Default.Get("decoder", "auto"), Preferences.Default.Get("openSl", false),
        Preferences.Default.Get("openGl", false), Preferences.Default.Get("language", "system"),
        Preferences.Default.Get("themeMode", ThemeMode.System.ToString()),
        Preferences.Default.Get("accent", "#36D6B0"), Preferences.Default.Get("openLastPlaylist", true),
        Preferences.Default.Get("lastAccountId", ""),
        Preferences.Default.Get("catalogUpdateSchedule", "manual"),
        Preferences.Default.Get("catalogUpdateWifiOnly", false),
        Preferences.Default.Get("catalogUpdateInBackground", false),
        Preferences.Default.Get("subtitleMode", "foreign"),
        Preferences.Default.Get("preferredAudioLanguage", "system"),
        Preferences.Default.Get("preferredSubtitleLanguage", "system"),
        Preferences.Default.Get("rememberTrackSelection", true),
        Preferences.Default.Get("audioDelayMs", 0), Preferences.Default.Get("subtitleDelayMs", 0),
        Preferences.Default.Get("rememberedAudioTrack", ""),
        Preferences.Default.Get("rememberedSubtitleTrack", ""),
        Preferences.Default.Get("reminderMinutesBefore", "5"),
        Preferences.Default.Get("timeshiftEnabled", true), Preferences.Default.Get("timeshiftMinutes", "30"));

    private static void ApplySettings(BackupSettings settings, IReadOnlySet<string> accountIds)
    {
        Preferences.Default.Set("pip", settings.Pip);
        Preferences.Default.Set("streamFormat", settings.StreamFormat);
        Preferences.Default.Set("episodeAutoPlay", settings.AutoPlay);
        Preferences.Default.Set("autoPlaySeconds", Math.Clamp(settings.AutoPlaySeconds, 0, 60));
        Preferences.Default.Set("autoClearCache", settings.AutoClearCache);
        Preferences.Default.Set("subtitles", settings.Subtitles);
        Preferences.Default.Set("clock12", settings.Clock12);
        Preferences.Default.Set("userAgent", settings.UserAgent);
        Preferences.Default.Set("bufferSeconds", Math.Clamp(settings.BufferSeconds, 1, 60));
        Preferences.Default.Set("networkSpeed", settings.NetworkSpeed);
        Preferences.Default.Set("decoder", settings.Decoder);
        Preferences.Default.Set("openSl", settings.OpenSl);
        Preferences.Default.Set("openGl", settings.OpenGl);
        Preferences.Default.Set("openLastPlaylist", settings.OpenLastPlaylist);
        Preferences.Default.Set("catalogUpdateSchedule", settings.CatalogUpdateSchedule is
            "startup" or "daily" or "weekly" ? settings.CatalogUpdateSchedule : "manual");
        Preferences.Default.Set("catalogUpdateWifiOnly", settings.CatalogUpdateWifiOnly);
        Preferences.Default.Set("catalogUpdateInBackground", settings.CatalogUpdateInBackground);
        var subtitleMode = settings.SubtitleMode is "off" or "foreign" or "always"
            ? settings.SubtitleMode : settings.Subtitles ? "foreign" : "off";
        Preferences.Default.Set("subtitleMode", subtitleMode);
        Preferences.Default.Set("subtitles", subtitleMode != "off");
        Preferences.Default.Set("preferredAudioLanguage", ValidTrackLanguage(settings.PreferredAudioLanguage));
        Preferences.Default.Set("preferredSubtitleLanguage", ValidTrackLanguage(settings.PreferredSubtitleLanguage));
        Preferences.Default.Set("rememberTrackSelection", settings.RememberTrackSelection ?? true);
        Preferences.Default.Set("audioDelayMs", Math.Clamp(settings.AudioDelayMs, -5000, 5000));
        Preferences.Default.Set("subtitleDelayMs", Math.Clamp(settings.SubtitleDelayMs, -5000, 5000));
        if (!string.IsNullOrWhiteSpace(settings.RememberedAudioTrack))
            Preferences.Default.Set("rememberedAudioTrack", settings.RememberedAudioTrack);
        if (!string.IsNullOrWhiteSpace(settings.RememberedSubtitleTrack))
            Preferences.Default.Set("rememberedSubtitleTrack", settings.RememberedSubtitleTrack);
        Preferences.Default.Set("reminderMinutesBefore", int.TryParse(settings.ReminderMinutesBefore, out var reminderMinutes)
            ? Math.Clamp(reminderMinutes, 0, 60).ToString() : "5");
        Preferences.Default.Set("timeshiftEnabled", settings.TimeshiftEnabled ?? true);
        Preferences.Default.Set("timeshiftMinutes", int.TryParse(settings.TimeshiftMinutes, out var timeshiftMinutes)
            ? Math.Clamp(timeshiftMinutes, 5, 120).ToString() : "30");
        if (accountIds.Contains(settings.LastAccountId)) Preferences.Default.Set("lastAccountId", settings.LastAccountId);
        LanguageService.Apply(settings.Language);
        ThemeService.Apply(settings.Accent,
            Enum.TryParse<ThemeMode>(settings.Theme, out var theme) && Enum.IsDefined(theme) ? theme : ThemeMode.System);
        AppServices.Client.UserAgent = settings.UserAgent;
        CatalogUpdateService.ConfigureSchedule();
        _ = ReminderService.RescheduleAllAsync();
        _ = DvrService.RefreshAllRecurringAsync();
    }

    private static string ValidTrackLanguage(string? value) => value is
        "pt" or "en" or "es" or "fr" or "de" or "it" ? value : "system";

    private sealed record BackupDocument(int Version, DateTimeOffset CreatedAt,
        IReadOnlyList<PlaylistAccount> Accounts, IReadOnlyList<UserProfile> Profiles,
        IReadOnlyList<ProfileBackupData> PersonalData, IReadOnlyDictionary<string, string> CatalogOptions,
        BackupSettings Settings, IReadOnlyList<BrowserSource> BrowserSources, string DefaultBrowserSourceId);
    private sealed record ProfileBackupData(string ProfileId, string AccountId,
        IReadOnlyList<FavoriteEntry> Favorites, IReadOnlyList<WatchEntry> History,
        IReadOnlyList<ProgrammeReminder>? Reminders, IReadOnlyList<DvrSeriesRule>? SeriesRules = null);
    private sealed record BackupSettings(bool Pip, string StreamFormat, bool AutoPlay, int AutoPlaySeconds,
        bool AutoClearCache, bool Subtitles, bool Clock12, string UserAgent, int BufferSeconds,
        bool NetworkSpeed, string Decoder, bool OpenSl, bool OpenGl, string Language, string Theme,
        string Accent, bool OpenLastPlaylist, string LastAccountId, string CatalogUpdateSchedule,
        bool CatalogUpdateWifiOnly, bool CatalogUpdateInBackground, string SubtitleMode,
        string PreferredAudioLanguage, string PreferredSubtitleLanguage, bool? RememberTrackSelection,
        int AudioDelayMs, int SubtitleDelayMs, string RememberedAudioTrack, string RememberedSubtitleTrack,
        string ReminderMinutesBefore, bool? TimeshiftEnabled, string TimeshiftMinutes);
}

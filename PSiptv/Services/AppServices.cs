using System.Text.Json;
using System.Security.Cryptography;
using PSiptv.Core;

namespace PSiptv.Services;

public static class AppServices
{
    public static AccountStore Accounts { get; } = new();
    public static IptvClient Client { get; } = new(new HttpClient { Timeout = TimeSpan.FromMinutes(3) });
    public static PlaylistAccount? ActiveAccount { get; private set; }
    public static int SessionVersion { get; private set; }
    public static event Action? Locked;
    public static event Action? PlaybackSuspended;
    public static void SuspendPlayback() => PlaybackSuspended?.Invoke();
    public static void Activate(PlaylistAccount account)
    {
        SessionVersion++;
        ActiveAccount = account;
        Preferences.Default.Set("lastAccountId", account.Id);
        FavoritesService.Clear();
    }
    public static void UpdateAccount(PlaylistAccount account) { if (ActiveAccount?.Id == account.Id) ActiveAccount = account; }
    public static void ActivateProfile(string profileId)
    {
        UserProfileService.Activate(profileId);
        SessionVersion++;
        SuspendPlayback();
        FavoritesService.Clear();
    }
    public static void Lock()
    {
        SessionVersion++;
        ActiveAccount = null;
        FavoritesService.Clear();
        Locked?.Invoke();
    }
}

public sealed class AccountStore
{
    private const string LegacyAccountsKey = "psiptv.accounts.v1";
    private const string AccountsKey = "psiptv.accounts.v2";
    private const string EncryptionKey = "psiptv.accounts.encryption-key.v1";
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<List<PlaylistAccount>> LoadAsync()
    {
        await gate.WaitAsync();
        try { return await LoadCoreAsync(true); }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(PlaylistAccount account)
    {
        await gate.WaitAsync();
        try
        {
            var accounts = await LoadCoreAsync(false);
            accounts.RemoveAll(a => a.Id == account.Id);
            accounts.Add(account);
            await SaveCoreAsync(accounts);
        }
        finally { gate.Release(); }
    }

    public async Task DeleteAsync(string id)
    {
        await gate.WaitAsync();
        try
        {
            var accounts = await LoadCoreAsync(false);
            var removed = accounts.FirstOrDefault(a => a.Id == id);
            accounts.RemoveAll(a => a.Id == id);
            await SaveCoreAsync(accounts);
            if (removed?.Provider == ProviderType.LocalM3U) DeleteLocalPlaylist(removed.Url);
            await CatalogCacheService.DeleteAsync(id);
            await EpgService.DeleteAsync(id);
            await FavoritesService.DeleteAsync(id);
            await HistoryService.DeleteAccountAsync(id);
            await ReminderService.DeleteAccountAsync(id);
            await DvrService.DeleteAccountAsync(id);
            await OfflineDownloadService.DeleteAccountAsync(id);
            SecureStorage.Default.Remove($"catalog-options.{id}");
            SecureStorage.Default.Remove($"psiptv.biometric.{id}");
            foreach (var kind in Enum.GetValues<MediaKind>())
                Preferences.Default.Remove($"catalog.category.{id}.{(int)kind}");
            Preferences.Default.Remove($"automotive.category.{id}");
            Preferences.Default.Remove($"catalogUpdate.summary.{id}");
            if (Preferences.Default.Get("lastAccountId", "") == id) Preferences.Default.Remove("lastAccountId");
        }
        finally { gate.Release(); }
    }

    private static void DeleteLocalPlaylist(string path)
    {
        try
        {
            var folder = Path.GetFullPath(Path.Combine(FileSystem.AppDataDirectory, "local-playlists"))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(path);
            if (target.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) File.Delete(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    private static async Task<List<PlaylistAccount>> LoadCoreAsync(bool migrate)
    {
        var json = await SecureStorage.Default.GetAsync(AccountsKey);
        if (!string.IsNullOrWhiteSpace(json))
        {
            var envelope = JsonSerializer.Deserialize<AccountEnvelope>(json)
                ?? throw new InvalidOperationException("Não foi possível ler as listas guardadas.");
            if (envelope.Version != 1)
                throw new InvalidOperationException("A versão dos dados das listas não é suportada.");

            var key = await ReadKeyAsync();
            try { return envelope.Accounts.Select(a => AccountDataProtection.Unprotect(a, key)).ToList(); }
            finally { CryptographicOperations.ZeroMemory(key); }
        }

        var legacyJson = await SecureStorage.Default.GetAsync(LegacyAccountsKey);
        if (string.IsNullOrWhiteSpace(legacyJson)) return [];

        var accounts = JsonSerializer.Deserialize<List<PlaylistAccount>>(legacyJson)
            ?? throw new InvalidOperationException("Não foi possível ler as listas guardadas.");
        if (migrate)
        {
            await SaveCoreAsync(accounts);
            SecureStorage.Default.Remove(LegacyAccountsKey);
        }
        return accounts;
    }

    private static async Task SaveCoreAsync(IReadOnlyCollection<PlaylistAccount> accounts)
    {
        var key = await GetOrCreateKeyAsync();
        try
        {
            var protectedAccounts = accounts.Select(a => AccountDataProtection.Protect(a, key)).ToList();
            await SecureStorage.Default.SetAsync(AccountsKey,
                JsonSerializer.Serialize(new AccountEnvelope { Accounts = protectedAccounts }));
            SecureStorage.Default.Remove(LegacyAccountsKey);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static async Task<byte[]> GetOrCreateKeyAsync()
    {
        var encoded = await SecureStorage.Default.GetAsync(EncryptionKey);
        if (!string.IsNullOrWhiteSpace(encoded)) return DecodeKey(encoded);

        var key = AccountDataProtection.CreateKey();
        await SecureStorage.Default.SetAsync(EncryptionKey, Convert.ToBase64String(key));
        return key;
    }

    private static async Task<byte[]> ReadKeyAsync()
    {
        var encoded = await SecureStorage.Default.GetAsync(EncryptionKey);
        if (string.IsNullOrWhiteSpace(encoded))
            throw new InvalidOperationException("A chave de encriptação das listas não está disponível.");
        return DecodeKey(encoded);
    }

    private static byte[] DecodeKey(string encoded)
    {
        try
        {
            var key = Convert.FromBase64String(encoded);
            return key.Length == AccountDataProtection.KeySize
                ? key
                : throw new InvalidOperationException("A chave de encriptação das listas é inválida.");
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("A chave de encriptação das listas é inválida.", ex);
        }
    }

    private sealed class AccountEnvelope
    {
        public int Version { get; init; } = 1;
        public List<PlaylistAccount> Accounts { get; init; } = [];
    }
}

public enum ThemeMode { System, Dark, Light }

public static class ThemeService
{
    public static ThemeMode Mode => Enum.TryParse<ThemeMode>(Preferences.Default.Get("themeMode", ""), out var mode)
        && Enum.IsDefined(mode) ? mode
        : Preferences.Default.ContainsKey("dark")
            ? (Preferences.Default.Get("dark", true) ? ThemeMode.Dark : ThemeMode.Light)
            : ThemeMode.System;

    public static void Apply(string? accent = null, ThemeMode? mode = null)
    {
        var selected = mode ?? Mode;
        Preferences.Default.Set("themeMode", selected.ToString());
        if (accent is not null) Preferences.Default.Set("accent", accent);
        var app = Application.Current!;
        app.UserAppTheme = selected switch
        {
            ThemeMode.Dark => AppTheme.Dark,
            ThemeMode.Light => AppTheme.Light,
            _ => AppTheme.Unspecified
        };
        RefreshColors();
    }

    public static void RefreshColors()
    {
        var app = Application.Current!;
        var isDark = Mode == ThemeMode.Dark || (Mode == ThemeMode.System && app.RequestedTheme == AppTheme.Dark);
        var resources = app.Resources;
        resources["Accent"] = Color.FromArgb(Preferences.Default.Get("accent", "#36D6B0"));
        resources["Canvas"] = Color.FromArgb(isDark ? "#0F172C" : "#F2F5FA");
        resources["Surface"] = Color.FromArgb(isDark ? "#172235" : "#FFFFFF");
        resources["ProfileTile"] = Color.FromArgb(isDark ? "#30366D" : "#E2E5FA");
        resources["Ink"] = Color.FromArgb(isDark ? "#F3F7FF" : "#142238");
        resources["Muted"] = Color.FromArgb(isDark ? "#A9B8CD" : "#52647A");
    }
}


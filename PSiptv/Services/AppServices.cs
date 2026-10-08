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
        SuspendPlayback();
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
public enum ThemePalette { Default, Snow, Graphite, Sand, Indigo }
public enum InterfaceDesign { Current, Clean }

public static class DesignService
{
    public static InterfaceDesign Mode => Enum.TryParse<InterfaceDesign>(Preferences.Default.Get("designMode", ""), out var mode)
        && Enum.IsDefined(mode) ? mode : InterfaceDesign.Current;

    public static bool IsClean => Mode == InterfaceDesign.Clean;
    public static event Action? Changed;

    public static void Apply(InterfaceDesign mode)
    {
        if (!Enum.IsDefined(mode) || Mode == mode) return;
        Preferences.Default.Set("designMode", mode.ToString());
        Changed?.Invoke();
    }
}

public static class ThemeService
{
    public static ThemeMode Mode => Enum.TryParse<ThemeMode>(Preferences.Default.Get("themeMode", ""), out var mode)
        && Enum.IsDefined(mode) ? mode
        : Preferences.Default.ContainsKey("dark")
            ? (Preferences.Default.Get("dark", true) ? ThemeMode.Dark : ThemeMode.Light)
            : ThemeMode.System;

    public static ThemePalette Palette => Enum.TryParse<ThemePalette>(Preferences.Default.Get("themePalette", ""), out var palette)
        && Enum.IsDefined(palette) ? palette : ThemePalette.Default;

    public static void Apply(string? accent = null, ThemeMode? mode = null, ThemePalette? palette = null)
    {
        var selected = mode ?? Mode;
        Preferences.Default.Set("themeMode", selected.ToString());
        if (palette is not null) Preferences.Default.Set("themePalette", palette.Value.ToString());
        if (accent is not null) Preferences.Default.Set("accent", accent);
        var app = Application.Current!;
        app.UserAppTheme = Palette switch
        {
            ThemePalette.Snow or ThemePalette.Sand => AppTheme.Light,
            ThemePalette.Graphite or ThemePalette.Indigo => AppTheme.Dark,
            _ => selected switch
            {
                ThemeMode.Dark => AppTheme.Dark,
                ThemeMode.Light => AppTheme.Light,
                _ => AppTheme.Unspecified
            }
        };
        RefreshColors();
    }

    public static void RefreshColors()
    {
        var app = Application.Current!;
        var isDark = Palette is ThemePalette.Graphite or ThemePalette.Indigo ||
                     (Palette == ThemePalette.Default &&
                      (Mode == ThemeMode.Dark || (Mode == ThemeMode.System && app.RequestedTheme == AppTheme.Dark)));
        var resources = app.Resources;
        var colors = Palette switch
        {
            ThemePalette.Snow => ("#F8FAFC", "#FFFFFF", "#EAF2FF", "#15202B", "#677586", "#E6EBF0", "#2563EB"),
            ThemePalette.Graphite => ("#111419", "#1D232B", "#203B35", "#F3F5F7", "#AAB6C2", "#303942", "#65E0BB"),
            ThemePalette.Sand => ("#F8F3EB", "#FFFDF9", "#F6E4D6", "#2F2822", "#776C62", "#E8DCCD", "#C77444"),
            ThemePalette.Indigo => ("#151A31", "#202743", "#343864", "#F2F3FF", "#B3BAD8", "#364063", "#9CA3FF"),
            _ => isDark
                ? ("#0F172C", "#172235", "#30366D", "#F3F7FF", "#A9B8CD", "#30435A", "#36D6B0")
                : ("#F2F5FA", "#FFFFFF", "#E2E5FA", "#142238", "#52647A", "#D9E1EC", "#36D6B0")
        };
        resources["Canvas"] = Color.FromArgb(colors.Item1);
        resources["Surface"] = Color.FromArgb(colors.Item2);
        resources["ProfileTile"] = Color.FromArgb(colors.Item3);
        resources["Ink"] = Color.FromArgb(colors.Item4);
        resources["Muted"] = Color.FromArgb(colors.Item5);
        resources["Line"] = Color.FromArgb(colors.Item6);
        var accent = Color.FromArgb(Preferences.Default.Get("accent", colors.Item7));
        resources["Accent"] = accent;
        resources["OnAccent"] = Color.FromArgb(0.2126 * accent.Red + 0.7152 * accent.Green +
            0.0722 * accent.Blue < 0.58 ? "#FFFFFF" : "#071520");
    }
}


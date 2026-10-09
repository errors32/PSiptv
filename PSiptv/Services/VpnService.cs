using System.Text.Json;
using System.Text.RegularExpressions;

namespace PSiptv.Services;

public sealed record VpnProvider(string Id, string Name, string AndroidPackage);
public sealed record VpnConnectionProfile(string Id, string ProviderId, string Name, string Method,
    string Server, string Identity, string Username, string Password, string TunnelName,
    string RemoteIdentity);

public static class VpnService
{
    private const string ProviderKey = "vpn.defaultProvider";
    private const string StartKey = "vpn.openOnStartup";
    private const string ConnectKey = "vpn.connectOnStartup";
    private const string ProfilesKey = "vpn.connection.profiles";
    private const string LastProfileKey = "vpn.connection.lastProfile";
    private static bool startupHandled;
    private static int connecting;

    public static IReadOnlyList<VpnProvider> Providers { get; } =
    [
        new("proton", "Proton VPN", "com.protonvpn.android"),
        new("pure", "PureVPN", "com.gaditek.purevpnics"),
        new("wireguard", "WireGuard", "com.wireguard.android"),
        new("openvpn", "OpenVPN Connect", "net.openvpn.openvpn")
    ];

    public static VpnProvider? DefaultProvider =>
        Providers.FirstOrDefault(p => p.Id == Preferences.Default.Get(ProviderKey, ""));

    public static bool OpenOnStartup
    {
        get => Preferences.Default.Get(StartKey, false);
        set => Preferences.Default.Set(StartKey, value);
    }

    public static bool ConnectOnStartup
    {
        get => Preferences.Default.Get(ConnectKey, false);
        set => Preferences.Default.Set(ConnectKey, value);
    }

    public static bool SupportsDirectConnection =>
#if ANDROID
        true;
#else
        false;
#endif

    public static bool SupportsIkev2 =>
#if ANDROID
        OperatingSystem.IsAndroidVersionAtLeast(30);
#else
        false;
#endif

    // WireGuard uses this name to look up an imported tunnel; configuration text is not a name.
    public static bool IsValidWireGuardTunnelName(string? name) =>
        name is not null && Regex.IsMatch(name, @"\A[a-zA-Z0-9_=+.-]{1,15}\z");

    private static void ValidateWireGuardTunnelName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Introduza o nome do túnel importado na aplicação WireGuard.");
        if (!IsValidWireGuardTunnelName(name.Trim()))
            throw new ArgumentException("Este campo aceita apenas o nome do túnel já importado no WireGuard (até 15 caracteres). Importe o ficheiro .conf no WireGuard; não cole aqui o seu conteúdo.");
    }

    public static async Task<IReadOnlyList<VpnConnectionProfile>> LoadProfilesAsync()
    {
        var json = await SecureStorage.Default.GetAsync(ProfilesKey);
        return json is null ? [] : JsonSerializer.Deserialize<List<VpnConnectionProfile>>(json) ?? [];
    }

    public static async Task SaveProfileAsync(VpnConnectionProfile profile)
    {
        if (Providers.All(provider => provider.Id != profile.ProviderId) || profile.ProviderId == "openvpn")
            throw new ArgumentException("Escolha um serviço VPN válido.");
        if (string.IsNullOrWhiteSpace(profile.Name))
            throw new ArgumentException("Introduza o nome do perfil VPN.");
        if (profile.Method == "wireguard")
        {
            ValidateWireGuardTunnelName(profile.TunnelName);
        }
        else if (profile.Method == "ikev2")
        {
            if (!SupportsIkev2) throw new NotSupportedException("A ligação IKEv2 requer Android 11 ou superior.");
            if (string.IsNullOrWhiteSpace(profile.Server) || string.IsNullOrWhiteSpace(profile.Identity) ||
                string.IsNullOrWhiteSpace(profile.Username) || string.IsNullOrWhiteSpace(profile.Password))
                throw new ArgumentException("Preencha todos os campos IKEv2 do perfil VPN.");
        }
        else throw new ArgumentException("Escolha um método de ligação VPN válido.");
        if (profile.Method == "ikev2" && (profile.Server.Contains('/') || profile.Server.Any(char.IsWhiteSpace)))
            throw new ArgumentException("Introduza apenas o nome ou endereço do servidor VPN.");
        if (profile.Method == "ikev2" && !string.IsNullOrWhiteSpace(profile.RemoteIdentity) &&
            !OperatingSystem.IsAndroidVersionAtLeast(33))
            throw new NotSupportedException("Uma identidade remota diferente do servidor requer Android 13 ou superior.");
        var profiles = (await LoadProfilesAsync()).ToList();
        if (profiles.FirstOrDefault(existing => existing.Id == profile.Id)?.Method == "ikev2" &&
            profile.Method != "ikev2")
        {
#if ANDROID
            AndroidVpnConnection.DeleteProvisionedProfile(profile.Id);
#endif
        }
        profiles.RemoveAll(existing => existing.Id == profile.Id);
        profiles.Add(profile with { Name = profile.Name.Trim(), Server = profile.Server.Trim(),
            Identity = profile.Identity.Trim(), Username = profile.Username.Trim(), TunnelName = profile.TunnelName.Trim(),
            RemoteIdentity = profile.RemoteIdentity.Trim() });
        await SecureStorage.Default.SetAsync(ProfilesKey, JsonSerializer.Serialize(profiles));
    }

    public static async Task DeleteProfileAsync(string id)
    {
        var profiles = (await LoadProfilesAsync()).ToList();
        if (profiles.FirstOrDefault(profile => profile.Id == id)?.Method == "ikev2")
        {
#if ANDROID
            AndroidVpnConnection.DeleteProvisionedProfile(id);
#endif
        }
        profiles.RemoveAll(profile => profile.Id == id);
        await SecureStorage.Default.SetAsync(ProfilesKey, JsonSerializer.Serialize(profiles));
        if (Preferences.Default.Get(LastProfileKey, "") == id) Preferences.Default.Remove(LastProfileKey);
    }

    public static async Task<VpnConnectionProfile?> StartupProfileAsync()
    {
        if (DefaultProvider is not { } provider) return null;
        var profiles = (await LoadProfilesAsync()).Where(profile => profile.ProviderId == provider.Id).ToList();
        var last = Preferences.Default.Get(LastProfileKey, "");
        return profiles.FirstOrDefault(profile => profile.Id == last) ?? profiles.FirstOrDefault();
    }

    public static async Task ConnectAsync(VpnConnectionProfile profile)
    {
        if (!SupportsDirectConnection)
            throw new NotSupportedException("A ligação VPN integrada está disponível no Android.");
        if (profile.Method == "wireguard") ValidateWireGuardTunnelName(profile.TunnelName);
        if (Interlocked.CompareExchange(ref connecting, 1, 0) != 0)
            throw new InvalidOperationException("Já existe uma ligação VPN em curso.");
        try
        {
#if ANDROID
            if (profile.Method == "wireguard") await AndroidVpnConnection.ConnectWireGuardAsync(profile);
            else await AndroidVpnConnection.ConnectIkev2Async(profile);
            Preferences.Default.Set(LastProfileKey, profile.Id);
            Preferences.Default.Remove("vpn.lastStartupError");
#endif
        }
        finally { Volatile.Write(ref connecting, 0); }
    }

    public static void SetDefaultProvider(VpnProvider? provider) =>
        Preferences.Default.Set(ProviderKey, provider?.Id ?? "");

    public static bool IsAvailable(VpnProvider provider)
    {
#if ANDROID
        try
        {
            using var intent = Android.App.Application.Context.PackageManager?
                .GetLaunchIntentForPackage(provider.AndroidPackage);
            return intent is not null;
        }
        catch (Android.Content.PM.PackageManager.NameNotFoundException) { return false; }
#else
        return false;
#endif
    }

    // Android reports the active VPN transport, but not its owner or server to other apps.
    public static bool IsVpnActive()
    {
#if ANDROID
        var context = Android.App.Application.Context;
        var manager = context.GetSystemService(Android.Content.Context.ConnectivityService)
            as Android.Net.ConnectivityManager;
        if (manager is null) return false;
        var network = manager.ActiveNetwork;
        return network is not null &&
            manager.GetNetworkCapabilities(network)?.HasTransport(Android.Net.TransportType.Vpn) == true;
#else
        return false;
#endif
    }

    public static bool OpenProvider(VpnProvider provider)
    {
#if ANDROID
        var context = Android.App.Application.Context;
        using var intent = context.PackageManager?.GetLaunchIntentForPackage(provider.AndroidPackage);
        if (intent is null) return false;
        intent.AddFlags(Android.Content.ActivityFlags.NewTask);
        context.StartActivity(intent);
        return true;
#else
        return false;
#endif
    }

    public static bool OpenAndroidVpnSettings()
    {
#if ANDROID
        var context = Android.App.Application.Context;
        using var intent = new Android.Content.Intent(Android.Provider.Settings.ActionVpnSettings);
        intent.AddFlags(Android.Content.ActivityFlags.NewTask);
        if (intent.ResolveActivity(context.PackageManager!) is null) return false;
        context.StartActivity(intent);
        return true;
#else
        return false;
#endif
    }

    public static async Task HandleStartupAsync()
    {
        if (startupHandled)
        {
            await TryReconnectOnResumeAsync();
            return;
        }
        startupHandled = true;
        if (ConnectOnStartup)
        {
            try
            {
                if (await StartupProfileAsync() is { } profile) await ConnectAsync(profile);
                else Preferences.Default.Set("vpn.lastStartupError", "Não existe um perfil VPN para o serviço predefinido.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"VPN automatic connection failed: {ex}");
                Preferences.Default.Set("vpn.lastStartupError", ex.Message);
            }
            return;
        }
        if (!OpenOnStartup || DefaultProvider is not { } provider || IsVpnActive()) return;
        try { OpenProvider(provider); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"VPN app launch failed: {ex}"); }
    }

    public static async Task TryReconnectOnResumeAsync()
    {
        if (!startupHandled || !ConnectOnStartup || Volatile.Read(ref connecting) != 0 || IsVpnActive()) return;
        try
        {
            if (await StartupProfileAsync() is { } profile) await ConnectAsync(profile);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"VPN resume connection failed: {ex}");
            Preferences.Default.Set("vpn.lastStartupError", ex.Message);
        }
    }
}

using Android.App;
using Android.Content;
using Android.Net;
using Android.Net.Eap;
using Android.Net.IpSec.Ike;
using Android.Systems;
using System.Runtime.Versioning;
using PSiptv.Services;

namespace PSiptv;

internal static class AndroidVpnConnection
{
    private const string ProvisionedProfileKey = "vpn.ikev2.provisionedProfile";
    private const int ConsentRequestCode = 0x5650;
    private const int WireGuardPermissionRequestCode = 0x5651;
    private const string WireGuardPermission = "com.wireguard.android.permission.CONTROL_TUNNELS";
    private static TaskCompletionSource<bool>? pendingConsent;
    private static TaskCompletionSource<bool>? pendingWireGuardPermission;
    private static readonly SemaphoreSlim connectionGate = new(1, 1);

    internal static async Task ConnectIkev2Async(VpnConnectionProfile profile)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30))
            throw new NotSupportedException("A ligação IKEv2 integrada requer Android 11 ou superior.");

        await connectionGate.WaitAsync();
        try
        {
            var previousVpn = ActiveVpnNetworkHandle();
            var manager = Android.App.Application.Context.GetSystemService(Context.VpnManagementService) as VpnManager
                ?? throw new InvalidOperationException("O gestor de VPN do Android não está disponível.");
            var vpnProfile = BuildIkev2Profile(profile);
            var consent = manager.ProvisionVpnProfile(vpnProfile);
            Preferences.Default.Set(ProvisionedProfileKey, profile.Id);
            if (consent is not null && !await RequestConsentAsync(consent))
                throw new OperationCanceledException("A autorização da ligação VPN foi recusada.");

            if (OperatingSystem.IsAndroidVersionAtLeast(33)) manager.StartProvisionedVpnProfileSession();
            else manager.StartProvisionedVpnProfile();

            for (var attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(1000);
                if (OperatingSystem.IsAndroidVersionAtLeast(33))
                {
                    var state = manager.ProvisionedVpnProfileState?.State;
                    if (state == (int)VpnState.Connected) return;
                    if (state == (int)VpnState.Failed)
                        throw new InvalidOperationException("O Android não conseguiu estabelecer a ligação VPN.");
                }
                else if (ActiveVpnNetworkHandle() is { } currentVpn && currentVpn != previousVpn) return;
            }
            throw new TimeoutException("A ligação VPN não ficou ativa dentro do tempo esperado.");
        }
        finally { connectionGate.Release(); }
    }

    internal static void DeleteProvisionedProfile(string profileId)
    {
        if (Preferences.Default.Get(ProvisionedProfileKey, "") != profileId) return;
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var manager = Android.App.Application.Context.GetSystemService(Context.VpnManagementService) as VpnManager;
            manager?.DeleteProvisionedVpnProfile();
        }
        Preferences.Default.Remove(ProvisionedProfileKey);
    }

    [SupportedOSPlatform("android30.0")]
    private static Ikev2VpnProfile BuildIkev2Profile(VpnConnectionProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.RemoteIdentity))
            return new Ikev2VpnProfile.Builder(profile.Server, profile.Identity)
                .SetAuthUsernamePassword(profile.Username, profile.Password, null)
                .Build();
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            throw new NotSupportedException("Uma identidade remota diferente do servidor requer Android 13 ou superior.");

        var ikeProposal = new IkeSaProposal.Builder()
            .AddEncryptionAlgorithm((int)SaProposalEncryptionAlgorithm.AesCbc, (int)SaProposalKeyLength.Aes256)
            .AddEncryptionAlgorithm((int)SaProposalEncryptionAlgorithm.AesCbc, (int)SaProposalKeyLength.Aes128)
            .AddIntegrityAlgorithm((int)SaProposalIntegrityAlgorithm.HmacSha2256128)
            .AddDhGroup((int)SaProposalDhGroup.Group2048BitModp)
            .AddPseudorandomFunction((int)SaProposalPseudorandomFunction.Sha2256)
            .Build();
        var childProposal = new ChildSaProposal.Builder()
            .AddEncryptionAlgorithm((int)SaProposalEncryptionAlgorithm.AesCbc, (int)SaProposalKeyLength.Aes256)
            .AddEncryptionAlgorithm((int)SaProposalEncryptionAlgorithm.AesCbc, (int)SaProposalKeyLength.Aes128)
            .AddIntegrityAlgorithm((int)SaProposalIntegrityAlgorithm.HmacSha2256128)
            .Build();
        var eap = new EapSessionConfig.Builder()
            .SetEapMsChapV2Config(profile.Username, profile.Password)
            .Build();
        IkeIdentification localId = profile.Identity.Contains('@')
            ? new IkeRfc822AddrIdentification(profile.Identity)
            : new IkeFqdnIdentification(profile.Identity);
        var ike = new IkeSessionParams.Builder()
            .SetServerHostname(profile.Server)
            .SetLocalIdentification(localId)
            .SetRemoteIdentification(new IkeFqdnIdentification(profile.RemoteIdentity))
            .SetAuthEap(null, eap)
            .AddIkeSaProposal(ikeProposal)
            .Build();
        var child = new TunnelModeChildSessionParams.Builder()
            .AddChildSaProposal(childProposal)
            .AddInternalAddressRequest(OsConstants.AfInet)
            .AddInternalDnsServerRequest(OsConstants.AfInet)
            .Build();
        return new Ikev2VpnProfile.Builder(new IkeTunnelConnectionParams(ike, child)).Build();
    }

    internal static async Task ConnectWireGuardAsync(VpnConnectionProfile profile)
    {
        if (!PSiptv.Services.VpnService.IsAvailable(PSiptv.Services.VpnService.Providers.First(p => p.Id == "wireguard")))
            throw new InvalidOperationException("Instale a aplicação WireGuard e importe nela o perfil do servidor.");
        if (!await EnsureWireGuardPermissionAsync())
            throw new OperationCanceledException("A autorização para controlar túneis WireGuard foi recusada.");

        var previousVpn = ActiveVpnNetworkHandle();
        using var intent = new Intent("com.wireguard.android.action.SET_TUNNEL_UP");
        intent.SetClassName("com.wireguard.android", "com.wireguard.android.model.TunnelManager$IntentReceiver");
        intent.AddFlags(ActivityFlags.IncludeStoppedPackages);
        intent.PutExtra("tunnel", profile.TunnelName);
        Android.App.Application.Context.SendBroadcast(intent);

        for (var attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(1000);
            if (ActiveVpnNetworkHandle() is not { } currentVpn || currentVpn == previousVpn) continue;
            await Task.Delay(1500);
            if (ActiveVpnNetworkHandle() == currentVpn) return;
        }
        throw new TimeoutException("Não foi possível confirmar uma nova rede VPN após pedir a ativação do túnel WireGuard. Confirme no WireGuard se o túnel está ativo, se o nome coincide exatamente e se o controlo por aplicações externas está ativado.");
    }

    private static async Task<bool> EnsureWireGuardPermissionAsync()
    {
        var activity = Platform.CurrentActivity as MainActivity
            ?? throw new InvalidOperationException("Abra a aplicação para autorizar o WireGuard.");
        if (activity.CheckSelfPermission(WireGuardPermission) == Android.Content.PM.Permission.Granted) return true;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref pendingWireGuardPermission, completion, null) is not null)
            throw new InvalidOperationException("Já existe um pedido de autorização WireGuard em curso.");
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                activity.RequestPermissions([WireGuardPermission], WireGuardPermissionRequestCode));
            return await completion.Task.WaitAsync(TimeSpan.FromMinutes(2));
        }
        finally { Interlocked.CompareExchange(ref pendingWireGuardPermission, null, completion); }
    }

    private static async Task<bool> RequestConsentAsync(Intent consent)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref pendingConsent, completion, null) is not null)
            throw new InvalidOperationException("Já existe um pedido de autorização VPN em curso.");
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var activity = Platform.CurrentActivity as MainActivity
                    ?? throw new InvalidOperationException("Abra a aplicação para autorizar a ligação VPN.");
                activity.StartActivityForResult(consent, ConsentRequestCode);
            });
            return await completion.Task.WaitAsync(TimeSpan.FromMinutes(2));
        }
        finally
        {
            Interlocked.CompareExchange(ref pendingConsent, null, completion);
            consent.Dispose();
        }
    }

    internal static bool HandleResult(int requestCode, Result resultCode)
    {
        if (requestCode != ConsentRequestCode) return false;
        pendingConsent?.TrySetResult(resultCode == Result.Ok);
        return true;
    }

    internal static bool HandlePermissionResult(int requestCode, Android.Content.PM.Permission[] grantResults)
    {
        if (requestCode != WireGuardPermissionRequestCode) return false;
        pendingWireGuardPermission?.TrySetResult(grantResults.Length > 0 &&
            grantResults[0] == Android.Content.PM.Permission.Granted);
        return true;
    }

    private static long? ActiveVpnNetworkHandle()
    {
        var context = Android.App.Application.Context;
        var manager = context.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
        var network = manager?.ActiveNetwork;
        return network is not null && manager?.GetNetworkCapabilities(network)?.HasTransport(TransportType.Vpn) == true
            ? network.NetworkHandle : null;
    }
}

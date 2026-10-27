using Android.App;
using Android.Content;
using Android.Net;

namespace PSiptv.Wear;

// Wear OS normally proxies traffic through the paired phone's Bluetooth link.
// LAN broadcasts need a direct Wi-Fi route while the remote is on screen.
internal sealed class WearWifiConnection(Activity owner, Action<string> report) : ConnectivityManager.NetworkCallback
{
    private readonly ConnectivityManager? manager = owner.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
    private bool requested;
    private Network? boundNetwork;

    public void Start()
    {
        if (manager is null || requested) return;
        using var request = new NetworkRequest.Builder().AddTransportType(TransportType.Wifi)!.Build()!;
        try
        {
            requested = true;
            manager.RequestNetwork(request, this);
        }
        catch (Java.Lang.SecurityException)
        {
            requested = false;
            report("Não foi possível pedir Wi-Fi. Ligue-o nas definições do relógio.");
        }
    }

    public override void OnAvailable(Network network) => owner.RunOnUiThread(() =>
    {
        if (!requested || manager is null) return;
        if (manager.BindProcessToNetwork(network)) boundNetwork = network;
    });

    public override void OnLost(Network network) => owner.RunOnUiThread(() =>
    {
        if (!requested || manager is null || !network.Equals(boundNetwork)) return;
        manager.BindProcessToNetwork(null);
        boundNetwork = null;
        report("Wi-Fi desligado. Volte a ligar o relógio à mesma rede do PSiptv.");
    });

    public void Stop()
    {
        if (!requested || manager is null) return;
        requested = false;
        manager.BindProcessToNetwork(null);
        boundNetwork = null;
        manager.UnregisterNetworkCallback(this);
    }
}

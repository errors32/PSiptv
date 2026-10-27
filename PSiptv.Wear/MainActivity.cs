using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using PSiptv.Core;

namespace PSiptv.Wear;

[Activity(MainLauncher = true, Exported = true, Label = "PSiptv Remote",
    Theme = "@android:style/Theme.Material.NoActionBar")]
public sealed class MainActivity : Activity
{
    private readonly WearRemoteClient client = new();
    private readonly WearBluetoothClient bluetooth = new();
    private readonly List<RemoteDevice> devices = [];
    private readonly List<RemoteChannel> channels = [];
    private readonly SemaphoreSlim discoveryLock = new(1, 1);
    private CancellationTokenSource? lifetime;
    private CancellationTokenSource? selection;
    private WearWifiConnection? wifi;
    private TextView status = null!;
    private Spinner devicePicker = null!;
    private SeekBar volume = null!;
    private ListView channelList = null!;
    private Button activate = null!;
    private Button wifiSettings = null!;
    private Button connect = null!;
    private ArrayAdapter<string> deviceAdapter = null!;
    private ArrayAdapter<string> channelAdapter = null!;
    private RemoteDevice? selectedDevice;
    private bool updatingVolume;
    private bool updatingDevices;
    private bool useBluetooth;
    private bool foreground;
    private bool connected;
    private bool permissionAsked;
    private const int BluetoothPermissionRequest = 42;
    private string displayedCurrentChannelId = "";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        useBluetooth = GetPreferences(Android.Content.FileCreationMode.Private)!.GetBoolean("bluetooth", true);
        SetContentView(BuildInterface());
        SetConnected(false);
    }

    protected override void OnResume()
    {
        base.OnResume();
        foreground = true;
        StartSession();
    }

    protected override void OnPause()
    {
        foreground = false;
        StopSession();
        base.OnPause();
    }

    private void StopSession()
    {
        lifetime?.Cancel();
        lifetime?.Dispose();
        lifetime = null;
        selection?.Cancel();
        selection?.Dispose();
        selection = null;
        wifi?.Stop();
        wifi?.Dispose();
        wifi = null;
        bluetooth.Disconnect();
        connected = false;
    }

    private void StartSession()
    {
        if (!foreground) return;
        StopSession();
        selectedDevice = null;
        devices.Clear();
        deviceAdapter.Clear();
        devicePicker.Visibility = ViewStates.Gone;
        SetConnected(false);
        lifetime = new CancellationTokenSource();
        if (useBluetooth && !WearBluetoothClient.HasPermission)
        {
            SetStatus("Autorize os dispositivos próximos para usar Bluetooth. Pode também escolher Wi-Fi.");
            if (!permissionAsked && OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                permissionAsked = true;
                RequestPermissions([Android.Manifest.Permission.BluetoothConnect], BluetoothPermissionRequest);
            }
            return;
        }
        if (!useBluetooth)
        {
            wifi = new WearWifiConnection(this, SetStatus);
            wifi.Start();
        }
        _ = DiscoverAndPollAsync(lifetime.Token);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Android.Content.PM.Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == BluetoothPermissionRequest && useBluetooth && foreground) StartSession();
    }

    private View BuildInterface()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetGravity(GravityFlags.CenterHorizontal);
        // Scroll controls with the channels so they all remain reachable on a
        // small round watch. Padding keeps the first/last row inside the circle.
        channelList = new ListView(this) { DividerHeight = 1 };
        var round = Resources!.Configuration!.IsScreenRound;
        channelList.SetPadding(Dp(round ? 24 : 10), Dp(round ? 26 : 6), Dp(round ? 24 : 10), Dp(round ? 26 : 6));
        channelList.SetClipToPadding(false);
        channelList.SetBackgroundColor(Color.Rgb(15, 23, 44));

        var header = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var title = Label("PSiptv", 16, Color.White);
        title.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
        header.AddView(title, new LinearLayout.LayoutParams(0, Dp(36), 1));
        var refresh = new Button(this) { Text = "↻", ContentDescription = "Procurar dispositivos" };
        refresh.SetAllCaps(false);
        refresh.SetTextColor(Color.White);
        refresh.SetTextSize(Android.Util.ComplexUnitType.Sp, 20);
        refresh.Click += async (_, _) => await DiscoverAsync(lifetime?.Token ?? CancellationToken.None);
        header.AddView(refresh, new LinearLayout.LayoutParams(Dp(44), Dp(36)));
        root.AddView(header, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(36)));

        var transport = new Spinner(this) { ContentDescription = "Ligação do comando" };
        transport.Adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerDropDownItem, new[] { "Bluetooth", "Wi-Fi" });
        transport.SetSelection(useBluetooth ? 0 : 1);
        transport.ItemSelected += (_, e) =>
        {
            var nextBluetooth = e.Position == 0;
            if (nextBluetooth == useBluetooth) return;
            useBluetooth = nextBluetooth;
            GetPreferences(Android.Content.FileCreationMode.Private)!.Edit()!.PutBoolean("bluetooth", useBluetooth)!.Apply();
            StartSession();
        };
        root.AddView(transport, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(40)));

        status = Label("A procurar PSiptv no Wi-Fi…", 12, Color.Rgb(203, 213, 225));
        status.Gravity = GravityFlags.Center;
        status.SetMinLines(2);
        root.AddView(status, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        wifiSettings = new Button(this) { Text = "Ligar Wi-Fi", ContentDescription = "Abrir definições de Wi-Fi" };
        wifiSettings.SetAllCaps(false);
        wifiSettings.Click += (_, _) =>
        {
            if (useBluetooth && !WearBluetoothClient.HasPermission)
            {
                if (permissionAsked && OperatingSystem.IsAndroidVersionAtLeast(31) &&
                    !ShouldShowRequestPermissionRationale(Android.Manifest.Permission.BluetoothConnect))
                {
                    StartActivity(new Android.Content.Intent(Android.Provider.Settings.ActionApplicationDetailsSettings,
                        Android.Net.Uri.Parse($"package:{PackageName}")));
                    return;
                }
                permissionAsked = false;
                StartSession();
                return;
            }
            try { StartActivity(new Android.Content.Intent(useBluetooth ? Android.Provider.Settings.ActionBluetoothSettings : Android.Provider.Settings.ActionWifiSettings)); }
            catch (Android.Content.ActivityNotFoundException) { SetStatus("Abra as definições de ligação do relógio."); }
        };
        root.AddView(wifiSettings, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        deviceAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerDropDownItem, []);
        devicePicker = new Spinner(this) { Adapter = deviceAdapter, Visibility = ViewStates.Gone };
        devicePicker.ItemSelected += async (_, e) =>
        {
            if (updatingDevices || e.Position < 0 || e.Position >= devices.Count) return;
            if (selectedDevice == devices[e.Position]) return;
            SelectDevice(devices[e.Position]);
            SetConnected(false);
            await RefreshStateAsync(lifetime?.Token ?? CancellationToken.None);
        };
        root.AddView(devicePicker, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(36)));

        connect = new Button(this) { Text = "Ligar ao dispositivo", Visibility = ViewStates.Gone };
        connect.SetAllCaps(false);
        connect.Click += async (_, _) => await RefreshStateAsync(lifetime?.Token ?? CancellationToken.None);
        root.AddView(connect, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        activate = new Button(this) { Text = "Ativar dispositivo", Visibility = ViewStates.Gone };
        activate.SetAllCaps(false);
        activate.Click += async (_, _) => await SendCommandAsync("activate", "");
        root.AddView(activate, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        var volumeRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        volumeRow.SetGravity(GravityFlags.CenterVertical);
        var volumeLabel = Label("Vol.", 12, Color.White);
        volumeRow.AddView(volumeLabel, new LinearLayout.LayoutParams(Dp(34), Dp(38)));
        volume = new SeekBar(this) { Max = 100, Progress = 100, ContentDescription = "Volume da aplicação" };
        volume.StopTrackingTouch += async (_, _) =>
        {
            if (!updatingVolume) await SendCommandAsync("volume", volume.Progress.ToString());
        };
        volumeRow.AddView(volume, new LinearLayout.LayoutParams(0, Dp(38), 1));
        root.AddView(volumeRow, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(38)));

        channelAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleListItem1, []);
        channelList.AddHeaderView(root, null, false);
        channelList.Adapter = channelAdapter;
        channelList.ItemClick += async (_, e) =>
        {
            var index = e.Position - channelList.HeaderViewsCount;
            if (activate.Visibility != ViewStates.Visible && index >= 0 && index < channels.Count)
                await SendCommandAsync("channel", channels[index].Id);
        };
        return channelList;
    }

    private async Task DiscoverAndPollAsync(CancellationToken cancellationToken)
    {
        await DiscoverAsync(cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                if (selectedDevice is not null && connected) await RefreshStateAsync(cancellationToken);
                else if (!useBluetooth) await DiscoverAsync(cancellationToken);
            }
            catch (System.OperationCanceledException) { break; }
        }
    }

    private async Task DiscoverAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || !await discoveryLock.WaitAsync(0)) return;
        try
        {
            SetStatus(useBluetooth ? "Dispositivos Bluetooth emparelhados…" : "A procurar no Wi-Fi…");
            var found = useBluetooth ? WearBluetoothClient.PairedDevices() : await client.DiscoverAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            RunOnUiThread(() =>
            {
                if (cancellationToken.IsCancellationRequested) return;
                updatingDevices = true;
                var previousAddress = selectedDevice?.Key ?? (useBluetooth
                    ? GetPreferences(Android.Content.FileCreationMode.Private)!.GetString("bluetooth.device", "") : null);
                devices.Clear(); devices.AddRange(found);
                deviceAdapter.Clear();
                foreach (var device in devices) deviceAdapter.Add(device.Name);
                deviceAdapter.NotifyDataSetChanged();
                var index = previousAddress is null ? 0 : devices.FindIndex(device => device.Key == previousAddress);
                if (index < 0) index = 0;
                SelectDevice(devices.Count == 0 ? null : devices[index]);
                if (devices.Count > 0) devicePicker.SetSelection(index);
                devicePicker.Visibility = devices.Count > 1 || (useBluetooth && devices.Count > 0) ? ViewStates.Visible : ViewStates.Gone;
                updatingDevices = false;
                SetConnected(false);
                status.Text = useBluetooth
                    ? devices.Count == 0 ? "Emparelhe o relógio com o telemóvel/TV nas definições Bluetooth."
                        : "Escolha o telemóvel/TV e toque em Ligar. Mantenha o PSiptv aberto e o controlo Bluetooth ativo nesse equipamento."
                    : devices.Count == 0 ? "Nenhum PSiptv encontrado. Ligue o relógio ao mesmo Wi-Fi e abra uma lista no PSiptv do telemóvel/TV."
                        : $"{devices.Count} dispositivo(s)";
            });
            var savedBluetooth = GetPreferences(Android.Content.FileCreationMode.Private)!.GetString("bluetooth.device", "");
            if (selectedDevice is not null && (!useBluetooth || selectedDevice.Key == savedBluetooth))
                await RefreshStateAsync(cancellationToken);
        }
        catch (System.OperationCanceledException) { }
        catch (Exception ex) { SetStatus(ex.Message); }
        finally { discoveryLock.Release(); }
    }

    private async Task RefreshStateAsync(CancellationToken cancellationToken)
    {
        if (selectedDevice is not { } device) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, selection?.Token ?? CancellationToken.None);
        cancellationToken = request.Token;
        try
        {
            connect.Enabled = false;
            SetStatus(useBluetooth ? "A ligar por Bluetooth…" : "A ligar por Wi-Fi…");
            var state = await SendAsync(device, "state", "", cancellationToken);
            if (!cancellationToken.IsCancellationRequested && selectedDevice == device) ApplyState(state);
        }
        catch (System.OperationCanceledException) { }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested || selectedDevice != device) return;
            if (!useBluetooth) selectedDevice = null;
            SetConnected(false);
            SetStatus(ex.Message);
        }
        finally { if (selectedDevice == device && !cancellationToken.IsCancellationRequested) connect.Enabled = true; }
    }

    private async Task SendCommandAsync(string command, string value)
    {
        if (selectedDevice is not { } device) return;
        var cancellationToken = selection?.Token ?? lifetime?.Token ?? CancellationToken.None;
        try
        {
            var state = await SendAsync(device, command, value, cancellationToken);
            if (!cancellationToken.IsCancellationRequested && selectedDevice == device) ApplyState(state);
        }
        catch (System.OperationCanceledException) { }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private Task<RemoteControlState> SendAsync(RemoteDevice device, string command, string value, CancellationToken token) =>
        device.BluetoothAddress.Length > 0 ? bluetooth.SendAsync(device.BluetoothAddress, command, value, token)
            : client.SendAsync(device, command, value, token);

    private void SelectDevice(RemoteDevice? device)
    {
        selection?.Cancel();
        selection?.Dispose();
        selection = CancellationTokenSource.CreateLinkedTokenSource(lifetime?.Token ?? CancellationToken.None);
        bluetooth.Disconnect();
        selectedDevice = device;
    }

    private void ApplyState(RemoteControlState state) => RunOnUiThread(() =>
    {
        SetConnected(true);
        if (useBluetooth && selectedDevice is { } device)
            GetPreferences(Android.Content.FileCreationMode.Private)!.Edit()!.PutString("bluetooth.device", device.Key)!.Apply();
        activate.Visibility = state.IsActive ? ViewStates.Gone : ViewStates.Visible;
        volume.Enabled = state.IsActive;
        status.Text = !state.IsActive ? $"{state.DeviceName}\nToque em Ativar dispositivo para o controlar."
            : state.Channels.Count == 0 ? $"{state.DeviceName}\nSem canais. Abra TV ou Favoritos no PSiptv."
            : $"{state.DeviceName}\n{state.ActiveTab} · {state.Category}";
        updatingVolume = true;
        volume.Progress = Math.Clamp(state.Volume, 0, 100);
        updatingVolume = false;
        var signatureChanged = displayedCurrentChannelId != state.CurrentChannelId ||
            channels.Count != state.Channels.Count ||
            !channels.SequenceEqual(state.Channels);
        if (!signatureChanged) return;
        displayedCurrentChannelId = state.CurrentChannelId;
        channels.Clear(); channels.AddRange(state.Channels);
        channelAdapter.Clear();
        foreach (var channel in channels)
            channelAdapter.Add(channel.Id == state.CurrentChannelId ? $"▶ {channel.Name}" : channel.Name);
        channelAdapter.NotifyDataSetChanged();
    });

    private void SetConnected(bool connected)
    {
        this.connected = connected;
        connect.Enabled = true;
        volume.Enabled = connected;
        wifiSettings.Text = useBluetooth ? WearBluetoothClient.HasPermission ? "Definições Bluetooth" : "Autorizar Bluetooth" : "Ligar Wi-Fi";
        wifiSettings.ContentDescription = wifiSettings.Text;
        wifiSettings.Visibility = !connected ? ViewStates.Visible : ViewStates.Gone;
        connect.Visibility = useBluetooth && !connected && selectedDevice is not null ? ViewStates.Visible : ViewStates.Gone;
        if (connected) return;
        activate.Visibility = ViewStates.Gone;
        channels.Clear();
        channelAdapter.Clear();
        channelAdapter.NotifyDataSetChanged();
        displayedCurrentChannelId = "";
    }

    private void SetStatus(string message) => RunOnUiThread(() => status.Text = message);

    private TextView Label(string text, float size, Color color) => new TextView(this)
    {
        Text = text,
        TextSize = size,
        Gravity = GravityFlags.CenterVertical
    }.Also(view => view.SetTextColor(color));

    private int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density + 0.5f);
}

internal static class ViewExtensions
{
    public static T Also<T>(this T value, Action<T> action) { action(value); return value; }
}

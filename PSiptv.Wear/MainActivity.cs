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
    private readonly List<RemoteDevice> devices = [];
    private readonly List<RemoteChannel> channels = [];
    private CancellationTokenSource? lifetime;
    private TextView status = null!;
    private TextView context = null!;
    private Spinner devicePicker = null!;
    private SeekBar volume = null!;
    private ListView channelList = null!;
    private ArrayAdapter<string> deviceAdapter = null!;
    private ArrayAdapter<string> channelAdapter = null!;
    private RemoteDevice? selectedDevice;
    private bool updatingVolume;
    private string displayedCurrentChannelId = "";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(BuildInterface());
    }

    protected override void OnResume()
    {
        base.OnResume();
        lifetime?.Cancel();
        lifetime = new CancellationTokenSource();
        _ = DiscoverAndPollAsync(lifetime.Token);
    }

    protected override void OnPause()
    {
        lifetime?.Cancel();
        lifetime?.Dispose();
        lifetime = null;
        base.OnPause();
    }

    private View BuildInterface()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetGravity(GravityFlags.CenterHorizontal);
        root.SetPadding(Dp(12), Dp(10), Dp(12), Dp(8));
        root.SetBackgroundColor(Color.Rgb(15, 23, 44));

        var title = Label("PSiptv", 20, Color.White);
        title.Gravity = GravityFlags.Center;
        root.AddView(title, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(34)));

        status = Label("A procurar dispositivos…", 12, Color.Rgb(148, 163, 184));
        status.Gravity = GravityFlags.Center;
        root.AddView(status, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(28)));

        deviceAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerDropDownItem, []);
        devicePicker = new Spinner(this) { Adapter = deviceAdapter };
        devicePicker.ItemSelected += async (_, e) =>
        {
            if (e.Position < 0 || e.Position >= devices.Count) return;
            selectedDevice = devices[e.Position];
            await RefreshStateAsync(lifetime?.Token ?? CancellationToken.None);
        };
        root.AddView(devicePicker, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(42)));

        var search = new Button(this) { Text = "Procurar novamente" };
        search.SetAllCaps(false);
        search.SetTextColor(Color.White);
        search.Click += async (_, _) => await DiscoverAsync(lifetime?.Token ?? CancellationToken.None);
        root.AddView(search, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(42)));

        context = Label("Abra o PSiptv no dispositivo de destino", 12, Color.White);
        context.Gravity = GravityFlags.Center;
        root.AddView(context, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(36)));

        volume = new SeekBar(this) { Max = 100, Progress = 100, ContentDescription = "Volume da aplicação" };
        volume.StopTrackingTouch += async (_, _) =>
        {
            if (!updatingVolume) await SendCommandAsync("volume", volume.Progress.ToString());
        };
        root.AddView(volume, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(38)));

        channelAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleListItem1, []);
        channelList = new ListView(this) { Adapter = channelAdapter, DividerHeight = 1 };
        channelList.ItemClick += async (_, e) =>
        {
            if (e.Position >= 0 && e.Position < channels.Count)
                await SendCommandAsync("channel", channels[e.Position].Id);
        };
        root.AddView(channelList, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        return root;
    }

    private async Task DiscoverAndPollAsync(CancellationToken cancellationToken)
    {
        await DiscoverAsync(cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                if (selectedDevice is not null) await RefreshStateAsync(cancellationToken);
                else await DiscoverAsync(cancellationToken);
            }
            catch (System.OperationCanceledException) { break; }
        }
    }

    private async Task DiscoverAsync(CancellationToken cancellationToken)
    {
        SetStatus("A procurar no Wi-Fi…");
        try
        {
            var found = await client.DiscoverAsync(cancellationToken);
            RunOnUiThread(() =>
            {
                var previousAddress = selectedDevice?.Address;
                devices.Clear(); devices.AddRange(found);
                deviceAdapter.Clear();
                foreach (var device in devices) deviceAdapter.Add(device.Name);
                deviceAdapter.NotifyDataSetChanged();
                var index = previousAddress is null ? 0 : devices.FindIndex(device => device.Address.Equals(previousAddress));
                if (index < 0) index = 0;
                selectedDevice = devices.Count == 0 ? null : devices[index];
                if (devices.Count > 0) devicePicker.SetSelection(index);
                status.Text = devices.Count == 0 ? "Nenhum PSiptv encontrado" : $"{devices.Count} dispositivo(s)";
            });
            if (selectedDevice is not null) await RefreshStateAsync(cancellationToken);
        }
        catch (System.OperationCanceledException) { }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private async Task RefreshStateAsync(CancellationToken cancellationToken)
    {
        if (selectedDevice is not { } device) return;
        try { ApplyState(await client.SendAsync(device, "state", "", cancellationToken)); }
        catch (System.OperationCanceledException) { }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private async Task SendCommandAsync(string command, string value)
    {
        if (selectedDevice is not { } device) return;
        try { ApplyState(await client.SendAsync(device, command, value, lifetime?.Token ?? CancellationToken.None)); }
        catch (System.OperationCanceledException) { }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private void ApplyState(RemoteControlState state) => RunOnUiThread(() =>
    {
        status.Text = state.DeviceName;
        context.Text = $"{state.ActiveTab} · {state.Category}";
        updatingVolume = true;
        volume.Progress = Math.Clamp(state.Volume, 0, 100);
        updatingVolume = false;
        var signatureChanged = displayedCurrentChannelId != state.CurrentChannelId ||
            channels.Count != state.Channels.Count ||
            !channels.Select(channel => channel.Id).SequenceEqual(state.Channels.Select(channel => channel.Id));
        if (!signatureChanged) return;
        displayedCurrentChannelId = state.CurrentChannelId;
        channels.Clear(); channels.AddRange(state.Channels);
        channelAdapter.Clear();
        foreach (var channel in channels)
            channelAdapter.Add(channel.Id == state.CurrentChannelId ? $"▶ {channel.Name}" : channel.Name);
        channelAdapter.NotifyDataSetChanged();
    });

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

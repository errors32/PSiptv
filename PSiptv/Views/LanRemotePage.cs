using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

/// <summary>Compact phone/tablet remote for another PSiptv instance on the same Wi-Fi.</summary>
public sealed class LanRemotePage : LocalizedPage
{
    private readonly LanRemoteClient client = new();
    private readonly List<LanRemoteDevice> devices = [];
    private readonly Label status = Ui.Text("A procurar dispositivos no Wi-Fi…", 14, true);
    private readonly Label nowPlaying = Ui.Text("", 16);
    private readonly Picker devicePicker = new() { Title = "Dispositivo" };
    private readonly Button activateRemote;
    private readonly Button activateHere;
    private readonly Slider volume = new() { Minimum = 0, Maximum = 100 };
    private readonly Label volumeValue = Ui.Text("100%", 13, true);
    private readonly CollectionView channels;
    private readonly Grid controls;
    private CancellationTokenSource? lifetime;
    private LanRemoteDevice? selectedDevice;
    private bool applyingState;
    private bool refreshing;
    private IReadOnlyList<RemoteChannel> displayedChannels = [];

    public LanRemotePage()
    {
        Title = "Comando remoto";
        Ui.Page(this, Title);
        activateRemote = Ui.Button("Ativar dispositivo selecionado", ActivateRemoteAsync);
        activateHere = Ui.Button("Usar este dispositivo", ActivateHereAsync);
        var refresh = Ui.Button("Procurar novamente", DiscoverAsync);

        devicePicker.SelectedIndexChanged += async (_, _) =>
        {
            if (devicePicker.SelectedIndex < 0 || devicePicker.SelectedIndex >= devices.Count) return;
            selectedDevice = devices[devicePicker.SelectedIndex];
            await RefreshStateAsync();
        };

        volume.DragCompleted += async (_, _) =>
        {
            if (!applyingState) await SendAsync("volume", Math.Round(volume.Value).ToString());
        };
        volume.ValueChanged += (_, _) => volumeValue.Text = $"{Math.Round(volume.Value)}%";

        channels = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            EmptyView = Ui.Text("O dispositivo ativo ainda não tem canais favoritos.", 14, true),
            ItemTemplate = new DataTemplate(() =>
            {
                var name = Ui.Text("", 15);
                name.SetBinding(Label.TextProperty, nameof(RemoteChannel.Name));
                var card = Ui.FocusableCard(name, value => channels!.SelectedItem = value);
                card.Padding = new Thickness(14, 12);
                card.Margin = new Thickness(0, 0, 0, 6);
                return card;
            })
        };
        channels.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not RemoteChannel channel) return;
            channels.SelectedItem = null;
            await SendAsync("channel", channel.Id);
        };

        var volumeRow = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(50))],
            ColumnSpacing = 8
        };
        volumeRow.Add(new Label
        {
            Text = FaIcons.VolumeHigh,
            FontFamily = "FontAwesomeFreeSolid",
            FontSize = 20,
            VerticalTextAlignment = TextAlignment.Center
        });
        volumeRow.Add(volume, 1);
        volumeRow.Add(volumeValue, 2);

        controls = new Grid
        {
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)],
            RowSpacing = 10
        };
        controls.Add(nowPlaying);
        controls.Add(volumeRow, 0, 1);
        controls.Add(Ui.Text("Canais favoritos", 18), 0, 2);
        controls.Add(channels, 0, 3);

        var deviceActions = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 8
        };
        deviceActions.Add(activateHere);
        deviceActions.Add(activateRemote, 1);

        var root = new Grid
        {
            Padding = 16,
            RowSpacing = 10,
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)],
            SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container)
        };
        root.Add(Ui.Text("Comando remoto por Wi-Fi", 24));
        root.Add(status, 0, 1);
        root.Add(devicePicker, 0, 2);
        root.Add(deviceActions, 0, 3);
        root.Add(controls, 0, 4);
        Content = root;
        RefreshActiveButtons();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        lifetime?.Cancel();
        lifetime = new CancellationTokenSource();
        RemoteControlService.ActiveDeviceChanged += OnActiveDeviceChanged;
        await DiscoverAsync();
        _ = PollAsync(lifetime.Token);
    }

    protected override void OnDisappearing()
    {
        RemoteControlService.ActiveDeviceChanged -= OnActiveDeviceChanged;
        lifetime?.Cancel();
        lifetime?.Dispose();
        lifetime = null;
        base.OnDisappearing();
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                if (selectedDevice is null) await DiscoverAsync();
                else await RefreshStateAsync();
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task DiscoverAsync()
    {
        if (refreshing || lifetime?.IsCancellationRequested != false) return;
        refreshing = true;
        status.Text = "A procurar dispositivos com o perfil ativo…";
        try
        {
            var previousId = selectedDevice?.DeviceId;
            var found = await client.DiscoverAsync(RemoteControlService.CurrentProfileKey, lifetime.Token);
            devices.Clear(); devices.AddRange(found);
            devicePicker.ItemsSource = devices.Select(DeviceLabel).ToArray();
            var index = previousId is null ? devices.FindIndex(device => device.IsActive) :
                devices.FindIndex(device => device.DeviceId == previousId);
            if (index < 0 && devices.Count > 0) index = 0;
            selectedDevice = index < 0 ? null : devices[index];
            devicePicker.SelectedIndex = index;
            status.Text = devices.Count == 0
                ? "Nenhum outro dispositivo com este perfil foi encontrado. Confirme que ambos estão no mesmo Wi-Fi."
                : $"{devices.Count} dispositivo(s) encontrado(s) · Perfil {UserProfileService.Active.Name}";
            if (selectedDevice is not null) await RefreshStateAsync();
            else ClearRemoteState();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { refreshing = false; }
    }

    private async Task RefreshStateAsync()
    {
        if (selectedDevice is not { } device || lifetime?.IsCancellationRequested != false) return;
        try { ApplyState(await client.SendAsync(device, "state", "", lifetime.Token)); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    private async Task SendAsync(string command, string value)
    {
        if (selectedDevice is not { } device || lifetime?.IsCancellationRequested != false) return;
        try { ApplyState(await client.SendAsync(device, command, value, lifetime.Token)); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    private async Task ActivateRemoteAsync()
    {
        if (selectedDevice is not { } device) return;
        try
        {
            var state = await client.SendAsync(device, "activate", "", lifetime?.Token ?? CancellationToken.None);
            RemoteControlService.ObserveLeader(state.LeaderDeviceId, state.LeaderEpoch);
            ApplyState(state);
            status.Text = $"{state.DeviceName} é agora o dispositivo ativo.";
            await DiscoverAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { status.Text = ex.Message; }
    }

    private Task ActivateHereAsync()
    {
        RemoteControlService.ClaimActive();
        status.Text = "Este dispositivo é agora o dispositivo ativo.";
        RefreshActiveButtons();
        return Task.CompletedTask;
    }

    private void ApplyState(RemoteControlState state)
    {
        RemoteControlService.ObserveLeader(state.LeaderDeviceId, state.LeaderEpoch);
        nowPlaying.Text = string.IsNullOrWhiteSpace(state.CurrentChannelId)
            ? $"{state.DeviceName} · {state.ActiveTab}"
            : $"{state.DeviceName} · {state.Channels.FirstOrDefault(channel => channel.Id == state.CurrentChannelId)?.Name ?? state.ActiveTab}";
        applyingState = true;
        volume.Value = Math.Clamp(state.Volume, 0, 100);
        applyingState = false;
        if (!SameChannels(displayedChannels, state.Channels))
        {
            displayedChannels = state.Channels.ToArray();
            channels.ItemsSource = displayedChannels;
        }
        controls.IsEnabled = state.IsActive;
        activateRemote.IsEnabled = !state.IsActive;
        RefreshActiveButtons();
    }

    private void ClearRemoteState()
    {
        nowPlaying.Text = "";
        displayedChannels = [];
        channels.ItemsSource = null;
        controls.IsEnabled = false;
        activateRemote.IsEnabled = false;
        RefreshActiveButtons();
    }

    private void OnActiveDeviceChanged() => Dispatcher.Dispatch(RefreshActiveButtons);

    private void RefreshActiveButtons()
    {
        activateHere.IsEnabled = !RemoteControlService.IsActive;
        activateHere.Text = RemoteControlService.IsActive ? "✓ Este dispositivo está ativo" : "Usar este dispositivo";
    }

    private static string DeviceLabel(LanRemoteDevice device) =>
        device.IsActive ? $"● {device.Name} (ativo)" : device.Name;

    private static bool SameChannels(IReadOnlyList<RemoteChannel> current, IReadOnlyList<RemoteChannel> updated)
    {
        if (current.Count != updated.Count) return false;
        for (var index = 0; index < current.Count; index++)
            if (current[index].Id != updated[index].Id || current[index].Name != updated[index].Name)
                return false;
        return true;
    }
}

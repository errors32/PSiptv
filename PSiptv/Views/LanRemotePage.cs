using PSiptv.Core;
using PSiptv.Services;
using System.Collections.ObjectModel;

namespace PSiptv.Views;

/// <summary>Phone/tablet remote over the local network or paired Android Bluetooth devices.</summary>
public sealed class LanRemotePage : LocalizedPage
{
    private readonly LanRemoteClient client = new();
    private readonly List<LanRemoteDevice> devices = [];
    private readonly Label status = Ui.Text("A procurar dispositivos no Wi-Fi…", 14, true);
    private readonly Label nowPlaying = Ui.Text("", 16);
    private readonly Picker devicePicker = new() { Title = "Dispositivo" };
    private readonly Picker transportPicker = new() { Title = "Ligação", ItemsSource = new[] { "Wi-Fi", "Bluetooth" }, SelectedIndex = 0 };
    private readonly Switch bluetoothEnabled = new() { IsToggled = BluetoothRemoteService.Enabled };
    private readonly Label bluetoothStatus = Ui.Text("", 13, true);
    private bool updatingBluetooth;
    private bool UsingBluetooth => BluetoothRemoteService.Supported && transportPicker.SelectedIndex == 1;
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
    private bool discoveryPending;
    private readonly ObservableCollection<RemoteChannel> displayedChannels = [];

    public LanRemotePage()
    {
        Title = "Comando remoto";
        Ui.Page(this, Title);
        activateRemote = Ui.Button("Ativar dispositivo selecionado", ActivateRemoteAsync);
        activateHere = Ui.Button("Usar este dispositivo", ActivateHereAsync);
        var refresh = Ui.Button("Procurar novamente", DiscoverAsync);
        transportPicker.IsVisible = BluetoothRemoteService.Supported;
        transportPicker.SelectedIndexChanged += async (_, _) =>
        {
            selectedDevice = null;
            devices.Clear();
            devicePicker.ItemsSource = null;
            ClearRemoteState();
            await DiscoverAsync();
        };
        bluetoothEnabled.Toggled += async (_, e) =>
        {
            if (updatingBluetooth) return;
            try { await BluetoothRemoteService.SetEnabledAsync(e.Value); }
            catch (Exception ex) { status.Text = ex.Message; }
            updatingBluetooth = true;
            bluetoothEnabled.IsToggled = BluetoothRemoteService.Enabled;
            updatingBluetooth = false;
            if (UsingBluetooth) await DiscoverAsync();
        };

        devicePicker.SelectedIndexChanged += async (_, _) =>
        {
            if (refreshing) return;
            if (devicePicker.SelectedIndex < 0 || devicePicker.SelectedIndex >= devices.Count) return;
            var device = devices[devicePicker.SelectedIndex];
            if (selectedDevice != device) ClearRemoteState();
            selectedDevice = device;
            await RefreshStateAsync();
        };

        volume.DragCompleted += async (_, _) =>
        {
            if (!applyingState) await SendAsync("volume", Math.Round(volume.Value).ToString());
        };
        volume.ValueChanged += (_, _) => volumeValue.Text = $"{Math.Round(volume.Value)}%";

        channels = new CollectionView
        {
            ItemsSource = displayedChannels,
            ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepScrollOffset,
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
        var bluetoothRow = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = 8
        };
        bluetoothRow.Add(Ui.Text("Permitir controlo por Bluetooth", 14));
        bluetoothRow.Add(bluetoothEnabled, 1);
        bluetoothRow.Add(Ui.Button("Ajuda", () => LanguageService.AlertAsync(this, "Bluetooth",
            "Emparelhe os equipamentos nas definições Bluetooth do Android. Ative esta opção em ambos e mantenha as aplicações abertas com a mesma lista e perfil. Pode usar redes Wi-Fi diferentes.")), 2);
        var bluetoothOptions = Ui.Stack(bluetoothRow, bluetoothStatus);
        bluetoothOptions.IsVisible = BluetoothRemoteService.Supported;
        var connectionRow = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = 8
        };
        connectionRow.Add(transportPicker);
        connectionRow.Add(refresh, 1);
        root.Add(Ui.Stack(Ui.Text("Comando remoto", 24), connectionRow, bluetoothOptions));
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
        var cancellationToken = lifetime.Token;
        RemoteControlService.ActiveDeviceChanged += OnActiveDeviceChanged;
        BluetoothRemoteService.Changed += RefreshBluetoothStatus;
        RefreshBluetoothStatus();
        await DiscoverAsync();
        if (!cancellationToken.IsCancellationRequested) _ = PollAsync(cancellationToken);
    }

    protected override void OnDisappearing()
    {
        RemoteControlService.ActiveDeviceChanged -= OnActiveDeviceChanged;
        BluetoothRemoteService.Changed -= RefreshBluetoothStatus;
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
                if (selectedDevice is null && !UsingBluetooth) await DiscoverAsync();
                else await RefreshStateAsync();
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task DiscoverAsync()
    {
        if (lifetime?.IsCancellationRequested != false) return;
        if (refreshing) { discoveryPending = true; return; }
        refreshing = true;
        status.Text = "A procurar dispositivos com o perfil ativo…";
        try
        {
            var bluetooth = UsingBluetooth;
            var previousId = selectedDevice?.DeviceId;
            var found = bluetooth ? BluetoothRemoteService.PairedDevices() :
                await client.DiscoverAsync(RemoteControlService.CurrentProfileKey, lifetime.Token);
            if (bluetooth != UsingBluetooth || lifetime?.IsCancellationRequested != false) return;
            devices.Clear(); devices.AddRange(found);
            devicePicker.ItemsSource = devices.Select(DeviceLabel).ToArray();
            var index = previousId is null ? devices.FindIndex(device => device.IsActive) :
                devices.FindIndex(device => device.DeviceId == previousId);
            if (index < 0 && devices.Count > 0 && !bluetooth) index = 0;
            selectedDevice = index < 0 ? null : devices[index];
            devicePicker.SelectedIndex = index;
            status.Text = devices.Count == 0
                ? (bluetooth ? "Ative o controlo Bluetooth e emparelhe os equipamentos nas definições do Android." :
                    "Nenhum outro dispositivo com este perfil foi encontrado. Confirme que ambos estão no mesmo Wi-Fi.")
                : bluetooth ? "Escolha o tablet ou a Android TV emparelhados com a aplicação aberta e o controlo Bluetooth ativo." :
                    $"{devices.Count} dispositivo(s) encontrado(s) · Perfil {UserProfileService.Active.Name}";
            if (selectedDevice is not null) await RefreshStateAsync();
            else ClearRemoteState();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { status.Text = ex.Message; }
        finally
        {
            refreshing = false;
            if (discoveryPending)
            {
                discoveryPending = false;
                await DiscoverAsync();
            }
        }
    }

    private async Task RefreshStateAsync()
    {
        if (selectedDevice is not { } device || lifetime?.IsCancellationRequested != false) return;
        try
        {
            var state = await client.SendAsync(device, "state", "", lifetime.Token);
            if (selectedDevice == device && lifetime?.IsCancellationRequested == false) ApplyState(state);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (selectedDevice != device) return;
            status.Text = ex.Message;
            // Preserve the list and its scroll position while the next poll retries the connection.
            controls.IsEnabled = false;
            activateRemote.IsEnabled = false;
        }
    }

    private async Task SendAsync(string command, string value)
    {
        if (selectedDevice is not { } device || lifetime?.IsCancellationRequested != false) return;
        try
        {
            var state = await client.SendAsync(device, command, value, lifetime.Token);
            if (selectedDevice == device && lifetime?.IsCancellationRequested == false) ApplyState(state);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    private async Task ActivateRemoteAsync()
    {
        if (selectedDevice is not { } device) return;
        try
        {
            var state = await client.SendAsync(device, "activate", "", lifetime?.Token ?? CancellationToken.None);
            if (selectedDevice != device || lifetime?.IsCancellationRequested != false) return;
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
        RemoteChannelList.Update(displayedChannels, state.Channels);
        controls.IsEnabled = state.IsActive;
        activateRemote.IsEnabled = !state.IsActive;
        RefreshActiveButtons();
    }

    private void ClearRemoteState()
    {
        nowPlaying.Text = "";
        displayedChannels.Clear();
        controls.IsEnabled = false;
        activateRemote.IsEnabled = false;
        RefreshActiveButtons();
    }

    private void OnActiveDeviceChanged() => Dispatcher.Dispatch(RefreshActiveButtons);
    private void RefreshBluetoothStatus()
    {
        bluetoothStatus.Text = LanguageService.Text(BluetoothRemoteService.Status);
        bluetoothStatus.IsVisible = BluetoothRemoteService.Status.Length > 0;
    }

    private void RefreshActiveButtons()
    {
        activateHere.IsEnabled = !RemoteControlService.IsActive;
        activateHere.Text = RemoteControlService.IsActive ? "✓ Este dispositivo está ativo" : "Usar este dispositivo";
    }

    private static string DeviceLabel(LanRemoteDevice device) => device.BluetoothAddress is { } address
        ? $"{device.Name} · {address[^Math.Min(5, address.Length)..]} · Bluetooth"
        : device.IsActive ? $"● {device.Name} (ativo)" : device.Name;

}

namespace PSiptv.Core;

public static class RemoteControlProtocol
{
    public const string Magic = "PSiptv-REMOTE";
    public const int Version = 1;
    public const int DiscoveryPort = 45872;
    public const int ControlPort = 45873;
}

public sealed record RemoteDiscoveryRequest(string Magic, int Version);
public sealed record RemoteDiscoveryResponse(string Magic, int Version, string DeviceName, int Port, string Token);
public sealed record RemoteControlRequest(string Token, string Command, string Value = "");
public sealed record RemoteChannel(string Id, string Name);
public sealed record RemoteControlState(string DeviceName, string ActiveTab, string Category,
    int Volume, string CurrentChannelId, IReadOnlyList<RemoteChannel> Channels);
public sealed record RemoteControlResponse(bool Success, string Error, RemoteControlState? State);

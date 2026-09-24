namespace PSiptv.Core;

public static class RemoteControlProtocol
{
    public const string Magic = "PSiptv-REMOTE";
    public const int Version = 2;
    public const int DiscoveryPort = 45872;
    public const int ControlPort = 45873;
}

// Optional trailing fields preserve source compatibility with the original Wear client.
public sealed record RemoteDiscoveryRequest(string Magic, int Version, string ProfileKey = "",
    string DeviceId = "", string LeaderDeviceId = "", long LeaderEpoch = 0);
public sealed record RemoteDiscoveryResponse(string Magic, int Version, string DeviceName, int Port, string Token,
    string DeviceId = "", string ProfileKey = "", string ProfileName = "", bool IsActive = true,
    string LeaderDeviceId = "", long LeaderEpoch = 0);
public sealed record RemoteControlRequest(string Token, string Command, string Value = "");
public sealed record RemoteChannel(string Id, string Name);
public sealed record RemoteControlState(string DeviceName, string ActiveTab, string Category,
    int Volume, string CurrentChannelId, IReadOnlyList<RemoteChannel> Channels,
    bool IsActive = true, string ProfileName = "", string DeviceId = "",
    string LeaderDeviceId = "", long LeaderEpoch = 0, IReadOnlyList<string>? Categories = null);
public sealed record RemoteControlResponse(bool Success, string Error, RemoteControlState? State);

public static class RemoteLeadership
{
    /// <summary>Newer explicit choices win; equal epochs converge deterministically by device id.</summary>
    public static (string DeviceId, long Epoch) Resolve(string currentDeviceId, long currentEpoch,
        string candidateDeviceId, long candidateEpoch)
    {
        if (string.IsNullOrWhiteSpace(candidateDeviceId)) return (currentDeviceId, currentEpoch);
        if (string.IsNullOrWhiteSpace(currentDeviceId) || candidateEpoch > currentEpoch)
            return (candidateDeviceId, candidateEpoch);
        if (candidateEpoch < currentEpoch) return (currentDeviceId, currentEpoch);
        return string.CompareOrdinal(candidateDeviceId, currentDeviceId) < 0
            ? (candidateDeviceId, candidateEpoch)
            : (currentDeviceId, currentEpoch);
    }
}

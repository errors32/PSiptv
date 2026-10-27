using System.Text.Json.Serialization;
using PSiptv.Core;

namespace PSiptv.Wear;

// Keep the wire contract available in trimmed Release APKs without reflection.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(RemoteDiscoveryRequest))]
[JsonSerializable(typeof(RemoteDiscoveryResponse))]
[JsonSerializable(typeof(RemoteControlRequest))]
[JsonSerializable(typeof(RemoteControlResponse))]
internal partial class WearJsonContext : JsonSerializerContext;

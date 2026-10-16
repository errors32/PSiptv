using System.Text.Json.Serialization;

namespace PSiptv.Core;

public sealed class WindowsRelease
{
    [JsonPropertyName("tag_name")] public string Tag { get; init; } = "";
    [JsonPropertyName("body")] public string Notes { get; init; } = "";
    [JsonPropertyName("draft")] public bool Draft { get; init; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; init; }
    [JsonPropertyName("assets")] public List<WindowsReleaseAsset> Assets { get; init; } = [];

    public WindowsReleaseAsset? Package(string architecture) => Assets.FirstOrDefault(a =>
        a.Name == $"PSiptv.v{Tag.TrimStart('v', 'V')}.windows-{architecture}.zip" &&
        a.State == "uploaded" && a.Size > 0 && a.HasValidDigest &&
        Uri.TryCreate(a.Url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "api.github.com");

    public static WindowsRelease? Latest(IEnumerable<WindowsRelease> releases, string installed, string architecture)
    {
        if (!ReleaseVersion.TryParse(installed, out var current)) throw new InvalidOperationException("Versão instalada inválida.");
        return releases.Where(r => !r.Draft && !r.Prerelease && r.Package(architecture) is not null &&
                ReleaseVersion.TryParse(r.Tag, out var version) && version.CompareTo(current) > 0)
            .OrderByDescending(r => { ReleaseVersion.TryParse(r.Tag, out var v); return v; }).FirstOrDefault();
    }
}

public sealed class WindowsReleaseAsset
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("state")] public string State { get; init; } = "";
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("url")] public string Url { get; init; } = "";
    [JsonPropertyName("digest")] public string Digest { get; init; } = "";
    public bool HasValidDigest => Digest.StartsWith("sha256:", StringComparison.Ordinal) &&
        Digest.Length == 71 && Digest[7..].All(Uri.IsHexDigit);
}

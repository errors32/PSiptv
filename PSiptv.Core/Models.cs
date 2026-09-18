namespace PSiptv.Core;

public enum ProviderType { Xtream, M3U, Stalker, Jellyfin, Plex, Tvheadend, HDHomeRun, LocalM3U }
public enum MediaKind { Channel, Movie, Series }

public sealed record PlaylistAccount
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public ProviderType Provider { get; init; }
    public string Url { get; init; } = "";
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public string EpgUrl { get; init; } = "";
    public string PinHash { get; init; } = "";
    public bool IsProtected => PinHash.Length > 0;
    public string ProviderName => Provider switch
    {
        ProviderType.Xtream => "Xtream Codes", ProviderType.M3U => "Link M3U",
        ProviderType.LocalM3U => "Ficheiro M3U local", ProviderType.Stalker => "Stalker Portal",
        ProviderType.HDHomeRun => "HDHomeRun", _ => Provider.ToString()
    };
    public string Description => $"{ProviderName} · {(IsProtected ? "Protegida com PIN" : "Sem PIN")}";
    public override string ToString() => Name;
}

public sealed record MediaItem(string Id, string Name, string Category, MediaKind Kind,
    string Url = "", string Logo = "", string EpgId = "", bool HasEpisodes = false, string ParentSeriesId = "",
    bool HasCatchup = false, int CatchupDays = 0, string CatchupMode = "", string CatchupSource = "", bool IsCatchup = false,
    string HttpReferer = "", string HttpUserAgent = "", string HttpCookie = "", string SourceCommand = "");

public sealed record CatalogCategory(string Id, string Name);

public sealed record TvProgramme(string ChannelId, string Title, string Description,
    DateTimeOffset Start, DateTimeOffset End, string ArchiveStart = "", bool? HasArchive = null)
{
    public string Schedule => $"{Start.ToLocalTime():dd/MM HH:mm} – {End.ToLocalTime():HH:mm}";
    public string Status => Start <= DateTimeOffset.Now && End > DateTimeOffset.Now ? "EM DIRETO" : "";
}

public sealed record MediaDetails(MediaItem Item, string Plot = "", string ReleaseDate = "", string Duration = "",
    string Genre = "", string Cast = "", string Director = "", string Rating = "", string TrailerUrl = "",
    string Backdrop = "", IReadOnlyList<MediaItem>? Episodes = null)
{
    public IReadOnlyList<MediaItem> EpisodeItems => Episodes ?? [];
}

public static class WebAddress
{
    public static Uri Require(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host))
            throw new InvalidOperationException("Introduza um endereço HTTP ou HTTPS válido.");
        return uri;
    }
}

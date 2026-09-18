using System.Globalization;

namespace PSiptv.Core;

public static class CatchupStream
{
    public static bool IsAvailable(MediaItem channel, TvProgramme programme, DateTimeOffset now) =>
        channel.Kind == MediaKind.Channel && channel.HasCatchup && channel.CatchupDays > 0 &&
        programme.HasArchive != false && programme.End <= now && programme.Start >= now.AddDays(-channel.CatchupDays);

    public static MediaItem? Create(PlaylistAccount account, MediaItem channel, TvProgramme programme, DateTimeOffset now)
    {
        if (!IsAvailable(channel, programme, now)) return null;
        var url = account.Provider == ProviderType.Xtream || IsXtreamMode(channel.CatchupMode)
            ? XtreamUrl(account, channel, programme)
            : TemplateUrl(channel, programme);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        return channel with
        {
            Name = programme.Title,
            Url = uri.AbsoluteUri,
            IsCatchup = true,
            HasCatchup = false,
            CatchupDays = 0,
            CatchupMode = "",
            CatchupSource = ""
        };
    }

    private static string XtreamUrl(PlaylistAccount account, MediaItem channel, TvProgramme programme)
    {
        var duration = Math.Max(1, (int)Math.Ceiling((programme.End - programme.Start).TotalMinutes));
        var start = ArchiveStart(programme);
        if (account.Provider == ProviderType.Xtream)
            return $"{account.Url.TrimEnd('/')}/timeshift/{Uri.EscapeDataString(account.Username)}/{Uri.EscapeDataString(account.Password)}/{duration}/{start}/{Uri.EscapeDataString(channel.Id)}.ts";

        if (!Uri.TryCreate(channel.Url, UriKind.Absolute, out var live)) return "";
        var parts = live.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var liveIndex = Array.FindIndex(parts, p => p.Equals("live", StringComparison.OrdinalIgnoreCase));
        if (liveIndex < 0 || parts.Length < liveIndex + 4) return "";
        var user = Uri.UnescapeDataString(parts[liveIndex + 1]);
        var password = Uri.UnescapeDataString(parts[liveIndex + 2]);
        var id = Path.GetFileNameWithoutExtension(parts[liveIndex + 3]);
        return $"{live.Scheme}://{live.Authority}/timeshift/{Uri.EscapeDataString(user)}/{Uri.EscapeDataString(password)}/{duration}/{start}/{Uri.EscapeDataString(id)}.ts";
    }

    private static string TemplateUrl(MediaItem channel, TvProgramme programme)
    {
        var seconds = Math.Max(1, (long)Math.Ceiling((programme.End - programme.Start).TotalSeconds));
        var start = programme.Start.ToUniversalTime();
        var end = programme.End.ToUniversalTime();
        var value = channel.CatchupSource
            .Replace("${start}", start.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{start}", start.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{utc}", start.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("${end}", end.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{end}", end.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{utcend}", end.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("${duration}", seconds.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{duration}", seconds.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        foreach (var (token, format) in new[] { ("{Y}", "yyyy"), ("{m}", "MM"), ("{d}", "dd"), ("{H}", "HH"), ("{M}", "mm"), ("{S}", "ss") })
            value = value.Replace(token, start.ToString(format, CultureInfo.InvariantCulture), StringComparison.Ordinal);
        if (channel.CatchupMode == "append" || value.StartsWith('?') || value.StartsWith('&')) return channel.Url + value;
        return Uri.TryCreate(new Uri(channel.Url), value, out var resolved) ? resolved.AbsoluteUri : value;
    }

    private static string ArchiveStart(TvProgramme programme)
    {
        if (DateTime.TryParse(programme.ArchiveStart, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var supplied))
            return supplied.ToString("yyyy-MM-dd:HH-mm", CultureInfo.InvariantCulture);
        return programme.Start.ToLocalTime().ToString("yyyy-MM-dd:HH-mm", CultureInfo.InvariantCulture);
    }

    private static bool IsXtreamMode(string mode) => mode is "xc" or "xtream" or "xstream" or "xtream-codes";
}

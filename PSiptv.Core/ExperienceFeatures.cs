using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PSiptv.Core;

public enum ViewingState { Unwatched, Watching, Completed }
public sealed class PersonalCollection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<MediaItem> Items { get; set; } = [];
}
public sealed class PersonalLibrary
{
    public List<string> HomeOrder { get; set; } = ["Fixados", "Continuar a ver", "Canais recentes", "Favoritos", "A começar", "Gravações recentes"];
    public HashSet<string> HiddenHome { get; set; } = [];
    public List<MediaItem> Pinned { get; set; } = [];
    public List<PersonalCollection> Collections { get; set; } = [];
    public Dictionary<string, ViewingState> States { get; set; } = [];
    public Dictionary<string, ViewingStateMutation> StateChanges { get; set; } = [];
    public Dictionary<string, string> PreferredChannels { get; set; } = [];
    public ViewingState State(MediaItem item) => States.TryGetValue(CatalogPreferences.ItemKey(item), out var state) && Enum.IsDefined(state) ? state : ViewingState.Unwatched;
    public void SetState(MediaItem item, ViewingState state) => States[CatalogPreferences.ItemKey(item)] = state;
    public static PersonalLibrary Merge(PersonalLibrary local, PersonalLibrary imported)
    {
        local.HomeOrder = imported.HomeOrder.Concat(local.HomeOrder).Distinct().ToList();
        local.HiddenHome = imported.HiddenHome;
        local.Pinned = local.Pinned.Concat(imported.Pinned).DistinctBy(CatalogPreferences.ItemKey).Take(100).ToList();
        foreach (var collection in imported.Collections)
        {
            var current = local.Collections.FirstOrDefault(c => c.Id == collection.Id);
            if (current is null) local.Collections.Add(collection);
            else { current.Name = collection.Name; current.Items = current.Items.Concat(collection.Items).DistinctBy(CatalogPreferences.ItemKey).ToList(); }
        }
        ViewingStateSync.Merge(local, ViewingStateSync.Snapshot(imported));
        foreach (var pair in imported.PreferredChannels) local.PreferredChannels[pair.Key] = pair.Value;
        return local;
    }
}

public sealed record EpgSource(string Url, int OffsetMinutes = 0);
public sealed record EpgChannelMapping(string ChannelId, int SourceIndex = 0);
public sealed class EpgConfiguration
{
    public List<EpgSource> Sources { get; set; } = [];
    public Dictionary<string, EpgChannelMapping> Mappings { get; set; } = [];
    public string CacheIdentity => string.Join("|", Sources.Select(s => $"{s.Url}:{s.OffsetMinutes}"));
    public IReadOnlyList<TvProgramme> Select(MediaItem channel, IReadOnlyList<IReadOnlyList<TvProgramme>> guides)
    {
        var mapping = Mappings.GetValueOrDefault(CatalogPreferences.ItemKey(channel));
        var id = mapping?.ChannelId ?? channel.EpgId;
        return guides.SelectMany((guide, index) => mapping is not null && mapping.SourceIndex != index ? [] :
            guide.Where(p => p.ChannelId == id).Select(p => p with
            {
                Start = p.Start.AddMinutes(Math.Clamp(Sources.ElementAtOrDefault(index)?.OffsetMinutes ?? 0, -1440, 1440)),
                End = p.End.AddMinutes(Math.Clamp(Sources.ElementAtOrDefault(index)?.OffsetMinutes ?? 0, -1440, 1440))
            }))
            .DistinctBy(p => (p.Title, p.Start, p.End)).OrderBy(p => p.Start).ToArray();
    }
}

public static partial class ChannelGroupPolicy
{
    [GeneratedRegex(@"(?:[\s\[\]()|._-]+(?:4K|UHD|FHD|FULL\s*HD|HD|SD|HEVC|H[ .]?26[45]))+[\s\[\]()|._-]*$", RegexOptions.IgnoreCase)]
    private static partial Regex QualitySuffix();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
    public static string Key(MediaItem item)
    {
        var name = QualitySuffix().Replace(item.Name.Trim(), "");
        var normalized = new string(name.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return Spaces().Replace(normalized, " ").Trim().ToUpperInvariant();
    }
    public static IReadOnlyList<MediaItem> Alternatives(MediaItem channel, IEnumerable<MediaItem> catalog,
        string? preferredKey = null, string quality = "auto") => catalog
        .Where(c => c.Kind == MediaKind.Channel && !c.IsCatchup && c.Url.Length > 0 && Key(c) == Key(channel))
        .DistinctBy(CatalogPreferences.ItemKey)
        .OrderByDescending(c => CatalogPreferences.ItemKey(c) == preferredKey)
        .ThenByDescending(c => quality != "auto" && Quality(c) == quality)
        .ThenByDescending(c => c == channel).ToArray();
    public static string Quality(MediaItem item)
    {
        var tokens = Regex.Split(item.Name.ToUpperInvariant(), @"[^\p{L}\p{N}]+");
        return tokens.Any(t => t is "4K" or "UHD") ? "4K" : tokens.Contains("FHD") ? "FHD" : tokens.Contains("HD") ? "HD" : tokens.Contains("SD") ? "SD" : "auto";
    }
}

public static class DvrConflictPolicy
{
    // Sweep simultaneous occupancy; sequential overlapping programmes do not add up.
    public static bool ExceedsLimit(DvrRecording proposed, IEnumerable<DvrRecording> recordings, int limit)
    {
        var events = recordings.Where(r => r.AccountId == proposed.AccountId && r.Id != proposed.Id &&
            r.State is DvrRecordingState.Scheduled or DvrRecordingState.Recording && !r.Suppressed &&
            r.Start < proposed.End && r.End > proposed.Start)
            .Append(proposed).SelectMany(r => new[] { (Time: r.Start, Delta: 1), (Time: r.End, Delta: -1) })
            .OrderBy(e => e.Time).ThenBy(e => e.Delta);
        var concurrent = 0;
        foreach (var point in events) { concurrent += point.Delta; if (concurrent > Math.Max(1, limit)) return true; }
        return false;
    }
    public static DvrRecording WithMargins(DvrRecording recording, int before, int after) => recording with
    {
        Start = recording.Start.AddMinutes(-Math.Clamp(before, 0, 60)),
        End = recording.End.AddMinutes(Math.Clamp(after, 0, 120))
    };
    public static bool CanClean(DvrRecording recording, DateTimeOffset now, int retentionDays) =>
        retentionDays > 0 && recording.IsCompleted && recording.End < now.AddDays(-retentionDays);
}

public sealed record PlaybackDiagnostic(string Id, string AccountId, string ProfileId, string Title,
    DateTimeOffset Started, double StartupSeconds, int Interruptions, string Video, string Audio, double BitrateKbps, string Outcome);

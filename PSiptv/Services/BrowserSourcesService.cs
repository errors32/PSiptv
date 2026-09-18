using System.Text.Json;
using PSiptv.Core;

namespace PSiptv.Services;

public sealed record BrowserSource(string Id, string Name, string Url);

public static class BrowserSourcesService
{
    private const string SourcesKey = "browser.sources.v1";
    private const string DefaultKey = "browser.default-source.v1";
    private static readonly BrowserSource Initial = new("rtp-play", "RTP Play", "https://www.rtp.pt/play/");
    public static event Action? Changed;

    public static IReadOnlyList<BrowserSource> Sources
    {
        get
        {
            try
            {
                var json = Preferences.Default.Get(SourcesKey, "");
                if (json.Length == 0) return [Initial];
                return JsonSerializer.Deserialize<List<BrowserSource>>(json) ?? [Initial];
            }
            catch (JsonException) { return [Initial]; }
        }
    }

    public static BrowserSource? Default
    {
        get
        {
            var sources = Sources;
            var id = Preferences.Default.Get(DefaultKey, Initial.Id);
            return sources.FirstOrDefault(source => source.Id == id) ?? sources.FirstOrDefault();
        }
    }

    public static void Save(BrowserSource source, string? existingId = null)
    {
        var name = source.Name.Trim();
        var url = WebAddress.Require(source.Url).AbsoluteUri;
        if (name.Length == 0) throw new InvalidOperationException("Introduza um nome para a fonte.");
        var sources = Sources.ToList();
        var id = existingId ?? source.Id;
        var index = sources.FindIndex(item => item.Id == id);
        var updated = new BrowserSource(id, name, url);
        if (index < 0) sources.Add(updated); else sources[index] = updated;
        Persist(sources);
    }

    public static void Delete(string id)
    {
        var sources = Sources.Where(source => source.Id != id).ToList();
        Persist(sources);
        if (Preferences.Default.Get(DefaultKey, "") == id)
        {
            if (sources.FirstOrDefault() is { } first) Preferences.Default.Set(DefaultKey, first.Id);
            else Preferences.Default.Remove(DefaultKey);
        }
    }

    public static void SetDefault(string id)
    {
        if (!Sources.Any(source => source.Id == id)) return;
        Preferences.Default.Set(DefaultKey, id);
        Changed?.Invoke();
    }

    public static void Replace(IReadOnlyList<BrowserSource> sources, string? defaultId)
    {
        var valid = sources.Where(source => !string.IsNullOrWhiteSpace(source.Name))
            .Select(source => source with { Name = source.Name.Trim(), Url = WebAddress.Require(source.Url).AbsoluteUri })
            .DistinctBy(source => source.Id)
            .ToArray();
        Persist(valid);
        if (defaultId is not null && valid.Any(source => source.Id == defaultId))
            Preferences.Default.Set(DefaultKey, defaultId);
        Changed?.Invoke();
    }

    private static void Persist(IReadOnlyList<BrowserSource> sources)
    {
        Preferences.Default.Set(SourcesKey, JsonSerializer.Serialize(sources));
        if (sources.Count > 0 && !sources.Any(source => source.Id == Preferences.Default.Get(DefaultKey, Initial.Id)))
            Preferences.Default.Set(DefaultKey, sources[0].Id);
        Changed?.Invoke();
    }
}

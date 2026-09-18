using System.Globalization;
using System.Text;

namespace PSiptv.Core;

public sealed record MediaTrackOption(int Id, string Name);

public static class TrackPreferencePolicy
{
    public static string ResolveLanguage(string preference, CultureInfo culture) => preference == "system"
        ? culture.TwoLetterISOLanguageName.ToLowerInvariant()
        : preference.ToLowerInvariant();

    public static MediaTrackOption? Select(IEnumerable<MediaTrackOption> tracks, string language,
        string rememberedName = "")
    {
        var available = tracks.Where(track => track.Id >= 0).ToArray();
        if (rememberedName.Length > 0)
        {
            var remembered = available.FirstOrDefault(track =>
                string.Equals(track.Name, rememberedName, StringComparison.OrdinalIgnoreCase));
            if (remembered is not null) return remembered;
        }
        return available.FirstOrDefault(track => MatchesLanguage(track.Name, language));
    }

    public static bool MatchesLanguage(string trackName, string language)
    {
        var normalized = Normalize(trackName);
        var aliases = language.ToLowerInvariant() switch
        {
            "pt" or "por" => new[] { "portugues", "portuguese", " por ", " pt " },
            "en" or "eng" => new[] { "ingles", "english", " eng ", " en " },
            "es" or "spa" => new[] { "espanhol", "spanish", "espanol", " spa ", " es " },
            "fr" or "fra" or "fre" => new[] { "frances", "french", " fra ", " fre ", " fr " },
            "de" or "deu" or "ger" => new[] { "alemao", "german", " deu ", " ger ", " de " },
            "it" or "ita" => new[] { "italiano", "italian", " ita ", " it " },
            _ => new[] { $" {language.ToLowerInvariant()} " }
        };
        var padded = $" {normalized} ";
        return aliases.Any(alias => padded.Contains(alias, StringComparison.Ordinal));
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        return new string(decomposed.Where(character =>
                CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Select(character => char.ToLowerInvariant(character)).ToArray())
            .Normalize(NormalizationForm.FormC);
    }
}

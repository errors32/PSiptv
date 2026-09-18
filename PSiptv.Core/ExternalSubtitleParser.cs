using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace PSiptv.Core;

public sealed record SubtitleCue(TimeSpan Start, TimeSpan End, string Text);

public static partial class ExternalSubtitleParser
{
    public const int MaximumBytes = 5_000_000;
    public static bool SupportsFileName(string fileName) =>
        Path.GetExtension(fileName) is { } extension &&
        (extension.Equals(".srt", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<SubtitleCue> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("O ficheiro de legendas está vazio.");
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .TrimStart('\uFEFF').Split('\n');
        var result = new List<SubtitleCue>();
        for (var index = 0; index < lines.Length && result.Count < 100_000; index++)
        {
            var match = TimingLine().Match(lines[index].Trim());
            if (!match.Success || !TryTime(match.Groups["start"].Value, out var start) ||
                !TryTime(match.Groups["end"].Value, out var end) || end <= start) continue;
            var content = new List<string>();
            while (++index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]))
                content.Add(lines[index].Trim());
            var value = Clean(string.Join(Environment.NewLine, content));
            if (value.Length > 0) result.Add(new(start, end, value));
        }
        if (result.Count == 0) throw new InvalidOperationException("Não foram encontradas legendas SRT ou WebVTT válidas.");
        return result.OrderBy(cue => cue.Start).ThenBy(cue => cue.End).ToArray();
    }

    public static string TextAt(IReadOnlyList<SubtitleCue> cues, TimeSpan position)
    {
        if (cues.Count == 0) return "";
        var low = 0;
        var high = cues.Count - 1;
        var candidate = -1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (cues[middle].Start <= position) { candidate = middle; low = middle + 1; }
            else high = middle - 1;
        }
        if (candidate < 0) return "";
        var active = new List<string>();
        for (var index = candidate; index >= 0 && cues[index].Start <= position; index--)
        {
            if (cues[index].End > position) active.Add(cues[index].Text);
            if (position - cues[index].Start > TimeSpan.FromMinutes(10)) break;
        }
        active.Reverse();
        return string.Join(Environment.NewLine, active.Distinct());
    }

    private static bool TryTime(string value, out TimeSpan result)
    {
        value = value.Trim().Replace(',', '.');
        var formats = new[] { @"h\:mm\:ss\.fff", @"hh\:mm\:ss\.fff", @"m\:ss\.fff", @"mm\:ss\.fff",
            @"h\:mm\:ss", @"hh\:mm\:ss", @"m\:ss", @"mm\:ss" };
        return TimeSpan.TryParseExact(value, formats, CultureInfo.InvariantCulture, out result);
    }

    private static string Clean(string value)
    {
        value = WebUtility.HtmlDecode(Tag().Replace(value, ""));
        value = AssTag().Replace(value, "");
        return value.Trim();
    }

    [GeneratedRegex(@"^(?<start>(?:\d{1,2}:)?\d{1,2}:\d{2}[,.]\d{3})\s*-->\s*(?<end>(?:\d{1,2}:)?\d{1,2}:\d{2}[,.]\d{3})(?:\s+.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex TimingLine();
    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();
    [GeneratedRegex(@"\{\\[^}]+\}")]
    private static partial Regex AssTag();
}

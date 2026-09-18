using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PSiptv.Core;

public sealed record DvrSeriesRule(string Id, string AccountId, string ProfileId, string Name,
    string TitlePattern, string ChannelId = "", int StartMinutes = -1, int TimeToleranceMinutes = 30,
    bool Enabled = true, DateTimeOffset? CreatedAt = null);

public static class DvrSeriesRulePolicy
{
    public static bool Matches(DvrSeriesRule rule, MediaItem channel, TvProgramme programme,
        DateTimeOffset now, TimeSpan horizon)
    {
        if (!rule.Enabled || programme.End <= now || programme.Start > now.Add(horizon)) return false;
        var pattern = Normalize(rule.TitlePattern);
        if (pattern.Length == 0 || !Normalize(programme.Title).Contains(pattern, StringComparison.Ordinal)) return false;
        if (rule.ChannelId.Length > 0 && rule.ChannelId != channel.Id) return false;
        if (rule.StartMinutes < 0) return true;
        var local = programme.Start.ToLocalTime();
        var minutes = local.Hour * 60 + local.Minute;
        var difference = Math.Abs(minutes - Math.Clamp(rule.StartMinutes, 0, 1439));
        difference = Math.Min(difference, 1440 - difference);
        return difference <= Math.Clamp(rule.TimeToleranceMinutes, 0, 360);
    }

    public static string EpisodeKey(TvProgramme programme)
    {
        var description = Normalize(programme.Description);
        var identity = Normalize(programme.Title) + "\0" +
            (description.Length > 0 ? description : programme.Start.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
    }

    public static string Normalize(string value)
    {
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var spacing = false;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                spacing = false;
            }
            else if (!spacing && builder.Length > 0)
            {
                builder.Append(' ');
                spacing = true;
            }
        }
        return builder.ToString().Trim();
    }
}

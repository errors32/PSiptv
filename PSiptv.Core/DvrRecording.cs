using System.Security.Cryptography;
using System.Text;

namespace PSiptv.Core;

public enum DvrRecordingState { Scheduled, Recording, Completed, Failed }

public sealed record DvrRecording(string Id, string AccountId, string ProfileId,
    MediaItem Channel, string ProgrammeTitle, DateTimeOffset Start, DateTimeOffset End,
    DvrRecordingState State = DvrRecordingState.Scheduled, string FileName = "",
    long Bytes = 0, string Error = "", string SeriesRuleId = "", string EpisodeKey = "",
    bool Suppressed = false)
{
    public bool IsScheduled => State == DvrRecordingState.Scheduled;
    public bool IsRecording => State == DvrRecordingState.Recording;
    public bool IsCompleted => State == DvrRecordingState.Completed;
}

public static class DvrRecordingPolicy
{
    public static string IdFor(string accountId, string profileId, string channelId,
        string title, DateTimeOffset start)
    {
        var value = $"{accountId}\0{profileId}\0{channelId}\0{title}\0{start.ToUnixTimeSeconds()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
    }

    public static bool CanSchedule(DateTimeOffset start, DateTimeOffset end, DateTimeOffset now) =>
        end > now && end > start;

    public static bool IsDue(DvrRecording recording, DateTimeOffset now) =>
        recording.State == DvrRecordingState.Scheduled && recording.Start <= now && recording.End > now;

    public static bool IsMissed(DvrRecording recording, DateTimeOffset now) =>
        recording.State == DvrRecordingState.Scheduled && recording.End <= now;

    public static TimeSpan Remaining(DvrRecording recording, DateTimeOffset now) =>
        recording.End > now ? recording.End - now : TimeSpan.Zero;

    public static string SafeFileStem(string title, DateTimeOffset start)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(title.Trim().Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray());
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "Gravacao";
        if (cleaned.Length > 80) cleaned = cleaned[..80].Trim();
        return $"{start.ToLocalTime():yyyy-MM-dd_HH-mm}_{cleaned}";
    }
}

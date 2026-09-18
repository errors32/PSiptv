using System.Security.Cryptography;
using System.Text;

namespace PSiptv.Core;

public sealed record ProgrammeReminder(string Id, string AccountId, string ProfileId,
    string ChannelId, string ChannelName, string ProgrammeTitle,
    DateTimeOffset Start, DateTimeOffset End, bool Notified = false);

public static class ProgrammeReminderPolicy
{
    public static string IdFor(string accountId, string profileId, string channelId,
        string title, DateTimeOffset start)
    {
        var value = $"{accountId}\0{profileId}\0{channelId}\0{title}\0{start.ToUnixTimeSeconds()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
    }

    public static DateTimeOffset NotificationTime(ProgrammeReminder reminder, int minutesBefore,
        DateTimeOffset now)
    {
        var requested = reminder.Start.AddMinutes(-Math.Clamp(minutesBefore, 0, 60));
        return requested > now ? requested : now.AddSeconds(2);
    }

    public static bool CanCreate(TvProgramme programme, DateTimeOffset now) => programme.Start > now;
    public static bool IsDue(ProgrammeReminder reminder, int minutesBefore, DateTimeOffset now) =>
        !reminder.Notified && reminder.Start.AddMinutes(-Math.Clamp(minutesBefore, 0, 60)) <= now && reminder.End > now;
    public static bool IsExpired(ProgrammeReminder reminder, DateTimeOffset now) => reminder.End <= now;
}

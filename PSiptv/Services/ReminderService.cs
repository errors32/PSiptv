using System.Text.Json;
using PSiptv.Core;

namespace PSiptv.Services;

public static class ReminderService
{
    private static readonly SemaphoreSlim gate = new(1, 1);
#if !ANDROID
    private static Timer? timer;
#endif
    public static event Action? Changed;
    public static event Action<ProgrammeReminder>? Due;

    public static async Task<IReadOnlyList<ProgrammeReminder>> LoadAsync(string accountId, string? profileId = null)
    {
        var selectedProfile = profileId ?? UserProfileService.Active.Id;
        var reminders = await ReadAsync(accountId, selectedProfile);
        var current = reminders.Where(reminder => !ProgrammeReminderPolicy.IsExpired(reminder, DateTimeOffset.Now))
            .OrderBy(reminder => reminder.Start).ToArray();
        if (current.Length != reminders.Count)
            await WriteAsync(accountId, selectedProfile, current);
        return current;
    }

    public static async Task<bool> ToggleAsync(PlaylistAccount account, MediaItem channel, TvProgramme programme)
    {
        if (!ProgrammeReminderPolicy.CanCreate(programme, DateTimeOffset.Now))
            throw new InvalidOperationException("Só pode criar lembretes para programas que ainda não começaram.");
        var profileId = UserProfileService.Active.Id;
        var id = ProgrammeReminderPolicy.IdFor(account.Id, profileId, channel.Id, programme.Title, programme.Start);
        await gate.WaitAsync();
        try
        {
            var reminders = await ReadAsync(account.Id, profileId);
            var existing = reminders.FirstOrDefault(reminder => reminder.Id == id);
            if (existing is not null)
            {
                reminders.Remove(existing);
                await WriteAsync(account.Id, profileId, reminders);
                ReminderPlatformScheduler.Cancel(id);
                Changed?.Invoke();
                return false;
            }

            if (!await RequestNotificationPermissionAsync())
                throw new InvalidOperationException("Permita as notificações para receber lembretes de programas.");
            var reminder = new ProgrammeReminder(id, account.Id, profileId, channel.Id,
                channel.Name, programme.Title, programme.Start, programme.End);
            reminders.Add(reminder);
            await WriteAsync(account.Id, profileId, reminders);
            Schedule(reminder);
            Changed?.Invoke();
            return true;
        }
        finally { gate.Release(); }
    }

    public static async Task RemoveAsync(ProgrammeReminder reminder)
    {
        await gate.WaitAsync();
        try
        {
            var reminders = await ReadAsync(reminder.AccountId, reminder.ProfileId);
            reminders.RemoveAll(item => item.Id == reminder.Id);
            await WriteAsync(reminder.AccountId, reminder.ProfileId, reminders);
            ReminderPlatformScheduler.Cancel(reminder.Id);
        }
        finally { gate.Release(); }
        Changed?.Invoke();
    }

    public static async Task DeleteAccountAsync(string accountId)
    {
        foreach (var profile in UserProfileService.Profiles)
            await DeleteProfileAsync(accountId, profile.Id);
    }

    public static async Task DeleteProfileAsync(string accountId, string profileId)
    {
        var reminders = await ReadAsync(accountId, profileId);
        foreach (var reminder in reminders) ReminderPlatformScheduler.Cancel(reminder.Id);
        SecureStorage.Default.Remove(StorageKey(accountId, profileId));
    }

    public static async Task<IReadOnlyList<ProgrammeReminder>> ExportAsync(string accountId, string profileId) =>
        await LoadAsync(accountId, profileId);

    public static async Task ImportAsync(string accountId, string profileId, IEnumerable<ProgrammeReminder> imported)
    {
        var existing = await LoadAsync(accountId, profileId);
        var merged = existing.Concat(imported.Where(reminder => reminder.AccountId == accountId &&
                reminder.ProfileId == profileId && !ProgrammeReminderPolicy.IsExpired(reminder, DateTimeOffset.Now)))
            .GroupBy(reminder => reminder.Id).Select(group => group.Last() with { Notified = false })
            .OrderBy(reminder => reminder.Start).ToArray();
        await WriteAsync(accountId, profileId, merged);
        foreach (var reminder in merged) Schedule(reminder);
    }

    public static async Task<IReadOnlyList<ProgrammeReminder>> LoadActiveProfileAsync()
    {
        var accounts = await AppServices.Accounts.LoadAsync();
        var result = new List<ProgrammeReminder>();
        foreach (var account in accounts) result.AddRange(await LoadAsync(account.Id));
        return result.OrderBy(reminder => reminder.Start).ToArray();
    }

    public static async Task StartAsync()
    {
        await RescheduleAllAsync();
#if !ANDROID
        timer ??= new Timer(_ => _ = CheckDueAsync(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
#endif
    }

    public static async Task RescheduleAllAsync()
    {
        await UserProfileService.LoadAsync();
        var accounts = await AppServices.Accounts.LoadAsync();
        foreach (var profile in UserProfileService.Profiles)
        foreach (var account in accounts)
        foreach (var reminder in await LoadAsync(account.Id, profile.Id))
            if (!reminder.Notified) Schedule(reminder);
    }

    public static async Task MarkNotifiedAsync(string accountId, string profileId, string reminderId)
    {
        await gate.WaitAsync();
        try
        {
            var reminders = await ReadAsync(accountId, profileId);
            var index = reminders.FindIndex(reminder => reminder.Id == reminderId);
            if (index >= 0)
            {
                reminders[index] = reminders[index] with { Notified = true };
                await WriteAsync(accountId, profileId, reminders);
            }
        }
        finally { gate.Release(); }
    }

    private static async Task CheckDueAsync()
    {
        try
        {
            foreach (var reminder in await LoadActiveProfileAsync())
            {
                if (!ProgrammeReminderPolicy.IsDue(
                        reminder, AppOptions.ReminderMinutesBefore, DateTimeOffset.Now)) continue;
                await MarkNotifiedAsync(reminder.AccountId, reminder.ProfileId, reminder.Id);
                Due?.Invoke(reminder);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Reminder check failed: {ex.GetType().Name}"); }
    }

    private static void Schedule(ProgrammeReminder reminder) => ReminderPlatformScheduler.Schedule(reminder,
        ProgrammeReminderPolicy.NotificationTime(reminder, AppOptions.ReminderMinutesBefore, DateTimeOffset.Now));

    private static async Task<bool> RequestNotificationPermissionAsync()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            return await Permissions.RequestAsync<Permissions.PostNotifications>() == PermissionStatus.Granted;
#endif
        return true;
    }

    private static async Task<List<ProgrammeReminder>> ReadAsync(string accountId, string profileId)
    {
        var json = await SecureStorage.Default.GetAsync(StorageKey(accountId, profileId));
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<ProgrammeReminder>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static Task WriteAsync(string accountId, string profileId, IEnumerable<ProgrammeReminder> reminders) =>
        SecureStorage.Default.SetAsync(StorageKey(accountId, profileId), JsonSerializer.Serialize(reminders));

    private static string StorageKey(string accountId, string profileId) =>
        $"programme-reminders.{UserProfileService.Scope(accountId, profileId)}";
}

public static partial class ReminderPlatformScheduler
{
    public static void Schedule(ProgrammeReminder reminder, DateTimeOffset triggerAt) => ScheduleCore(reminder, triggerAt);
    public static void Cancel(string reminderId) => CancelCore(reminderId);
    static partial void ScheduleCore(ProgrammeReminder reminder, DateTimeOffset triggerAt);
    static partial void CancelCore(string reminderId);
}

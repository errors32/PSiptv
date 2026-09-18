using Android.App;
using Android.App.Job;
using Android.Content;
using PSiptv.Core;

namespace PSiptv.Services;

[Service(Name = "com.PS.PSiptv.CatalogUpdateJobService",
    Permission = "android.permission.BIND_JOB_SERVICE", Exported = false)]
public sealed class CatalogUpdateJobService : JobService
{
    private CancellationTokenSource? cancellation;
    private volatile bool stopped;

    public override bool OnStartJob(JobParameters? parameters)
    {
        stopped = false;
        cancellation = new CancellationTokenSource();
        _ = RunAsync(parameters, cancellation.Token);
        return true;
    }

    public override bool OnStopJob(JobParameters? parameters)
    {
        stopped = true;
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
        return true;
    }

    private async Task RunAsync(JobParameters? parameters, CancellationToken token)
    {
        try { await CatalogUpdateService.TryRunDueAsync(false, true, token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Background catalog update failed: {ex.GetType().Name}"); }
        finally
        {
            cancellation?.Dispose();
            cancellation = null;
            if (!stopped && parameters is not null) JobFinished(parameters, false);
        }
    }
}

public static partial class CatalogUpdatePlatformScheduler
{
    private const int JobId = 0x505349;

    static partial void ConfigureCore(bool replace)
    {
        var context = Android.App.Application.Context;
        var scheduler = (JobScheduler?)context.GetSystemService(Context.JobSchedulerService);
        if (scheduler is null) return;
        var schedule = AppOptions.CatalogUpdateSchedule;
        if (!AppOptions.CatalogUpdateInBackground || schedule is not
            (CatalogUpdateSchedule.Daily or CatalogUpdateSchedule.Weekly))
        {
            scheduler.Cancel(JobId);
            return;
        }
        if (!replace && scheduler.GetPendingJob(JobId) is not null) return;
        scheduler.Cancel(JobId);

        var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(CatalogUpdateJobService)));
        var interval = schedule == CatalogUpdateSchedule.Daily
            ? (long)TimeSpan.FromDays(1).TotalMilliseconds
            : (long)TimeSpan.FromDays(7).TotalMilliseconds;
        var network = AppOptions.CatalogUpdateWifiOnly
            ? NetworkType.Unmetered
            : NetworkType.Any;
        using var builder = new JobInfo.Builder(JobId, component);
        builder.SetRequiredNetworkType(network);
        builder.SetPersisted(true);
        builder.SetPeriodic(interval);
        var job = builder.Build();
        if (job is not null) scheduler.Schedule(job);
    }
}

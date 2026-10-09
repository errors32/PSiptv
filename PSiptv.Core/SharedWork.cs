namespace PSiptv.Core;

/// <summary>Shares an in-flight transfer; cancelling a waiter does not cancel other waiters.</summary>
public sealed class SharedWork<TKey, TValue> where TKey : notnull
{
    private readonly object sync = new();
    private readonly Dictionary<TKey, Task<TValue>> pending = [];

    public Task<TValue> RunAsync(TKey key, Func<Task<TValue>> factory, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        Task<TValue> task;
        lock (sync)
        {
            if (!pending.TryGetValue(key, out task!))
            {
                // Start outside the caller's context; the shared transfer owns its timeout.
                task = Task.Run(factory);
                pending[key] = task;
                _ = task.ContinueWith(completed =>
                {
                    _ = completed.Exception; // Observe failures even if all waiters cancelled.
                    lock (sync)
                        if (pending.TryGetValue(key, out var active) && ReferenceEquals(active, completed)) pending.Remove(key);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        return task.WaitAsync(token);
    }
}

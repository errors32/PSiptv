namespace PSiptv.Core;

/// <summary>A change permits at most two attempts; only a new change creates a new budget.</summary>
public static class BoundedSyncAttempt
{
    public const int MaximumAttempts = 2;

    public static async Task<bool> RunAsync(Func<int, CancellationToken, Task<bool>> synchronize,
        CancellationToken token, TimeSpan? retryDelay = null)
    {
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (await synchronize(attempt, token).ConfigureAwait(false)) return true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { System.Diagnostics.Debug.WriteLine(ex); }
            if (attempt < MaximumAttempts)
                await Task.Delay(retryDelay ?? TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
        }
        return false;
    }
}

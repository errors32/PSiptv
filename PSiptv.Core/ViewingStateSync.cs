namespace PSiptv.Core;

public sealed record ViewingStateMutation(ViewingState State, DateTimeOffset UpdatedAt, string ChangeId);

public static class ViewingStateSync
{
    public static Dictionary<string, ViewingStateMutation> Snapshot(PersonalLibrary library)
    {
        var result = library.StateChanges.ToDictionary();
        foreach (var (key, state) in library.States)
            result.TryAdd(key, new(state, DateTimeOffset.UnixEpoch, "legacy:" + (int)state));
        return result;
    }

    public static bool Track(PersonalLibrary library, IReadOnlyDictionary<string, ViewingState> previous)
    {
        var synchronize = false;
        foreach (var key in previous.Keys.Union(library.States.Keys))
        {
            var before = previous.GetValueOrDefault(key, ViewingState.Unwatched);
            var after = library.States.GetValueOrDefault(key, ViewingState.Unwatched);
            if (before == after) continue;
            var date = DateTimeOffset.UtcNow;
            if (library.StateChanges.TryGetValue(key, out var old) && date <= old.UpdatedAt)
                date = old.UpdatedAt.AddTicks(1);
            library.StateChanges[key] = new(after, date, Guid.NewGuid().ToString("N"));
            synchronize |= after == ViewingState.Completed || before == ViewingState.Completed && after == ViewingState.Unwatched;
        }
        return synchronize;
    }

    public static bool Merge(PersonalLibrary library, IReadOnlyDictionary<string, ViewingStateMutation> remote)
    {
        var local = Snapshot(library);
        var changed = false;
        foreach (var (key, value) in remote)
        {
            if (local.TryGetValue(key, out var old) && (value.UpdatedAt < old.UpdatedAt ||
                value.UpdatedAt == old.UpdatedAt && string.CompareOrdinal(value.ChangeId, old.ChangeId) <= 0)) continue;
            library.States[key] = value.State;
            library.StateChanges[key] = value;
            changed = true;
        }
        return changed;
    }
}

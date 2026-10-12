using System.Collections.ObjectModel;

namespace PSiptv.Core;

public static class RemoteChannelList
{
    /// <summary>Reconciles a snapshot without resetting the collection or replacing unchanged items.</summary>
    public static void Update(ObservableCollection<RemoteChannel> current, IReadOnlyList<RemoteChannel> snapshot)
    {
        for (var index = 0; index < snapshot.Count; index++)
        {
            var channel = snapshot[index];
            if (index < current.Count && current[index].Id == channel.Id)
            {
                if (current[index] != channel) current[index] = channel;
                continue;
            }

            var existing = -1;
            for (var candidate = index + 1; candidate < current.Count; candidate++)
                if (current[candidate].Id == channel.Id) { existing = candidate; break; }
            if (existing >= 0)
            {
                current.Move(existing, index);
                if (current[index] != channel) current[index] = channel;
            }
            else current.Insert(index, channel);
        }
        while (current.Count > snapshot.Count) current.RemoveAt(current.Count - 1);
    }
}

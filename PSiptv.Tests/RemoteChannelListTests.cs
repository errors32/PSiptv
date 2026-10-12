using System.Collections.ObjectModel;
using System.Collections.Specialized;
using PSiptv.Core;

internal static class RemoteChannelListTests
{
    internal static void Run(Action<bool, string> check)
    {
        var first = new RemoteChannel("1", "RTP 1");
        var second = new RemoteChannel("2", "RTP 2");
        var third = new RemoteChannel("3", "SIC");
        var channels = new ObservableCollection<RemoteChannel> { first, second, third };
        var changes = new List<NotifyCollectionChangedAction>();
        channels.CollectionChanged += (_, e) => changes.Add(e.Action);
        for (var poll = 0; poll < 20; poll++)
            RemoteChannelList.Update(channels, [new("1", "RTP 1"), new("2", "RTP 2"), new("3", "SIC")]);
        check(changes.Count == 0 && ReferenceEquals(channels[1], second),
            "Consultas repetidas do comando remoto não notificam nem recriam os canais");

        RemoteChannelList.Update(channels, [first, second with { Name = "RTP 2 HD" }, third]);
        check(changes.SequenceEqual([NotifyCollectionChangedAction.Replace]) && ReferenceEquals(channels[2], third),
            "Renomear um canal remoto atualiza apenas esse canal");

        changes.Clear();
        RemoteChannelList.Update(channels, [third, first, second with { Name = "RTP 2 HD" }]);
        check(changes.SequenceEqual([NotifyCollectionChangedAction.Move]) && ReferenceEquals(channels[0], third),
            "Reordenar canais remotos move os itens existentes sem repor a coleção");

        changes.Clear();
        RemoteChannelList.Update(channels, [third, new("4", "TVI"), first]);
        check(channels.Select(c => c.Id).SequenceEqual(["3", "4", "1"]) &&
            !changes.Contains(NotifyCollectionChangedAction.Reset) && ReferenceEquals(channels[2], first),
            "Adicionar e remover canais remotos preserva os itens restantes sem reset");

        changes.Clear();
        RemoteChannelList.Update(channels, []);
        check(channels.Count == 0 && changes.All(action => action == NotifyCollectionChangedAction.Remove),
            "Um snapshot remoto vazio remove os canais individualmente");
    }
}

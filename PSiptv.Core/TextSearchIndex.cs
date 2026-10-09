using System.Globalization;

namespace PSiptv.Core;

public sealed record SearchDocument<T>(string Title, IReadOnlyList<string> Terms, T Value);

/// <summary>Immutable search data and bounded ranking, reusable across queries.</summary>
public sealed class TextSearchIndex<T>
{
    private readonly SearchDocument<T>[] documents;
    private readonly CompareInfo comparison;
    private readonly IComparer<Priority> order;
    private readonly record struct Priority(int Rank, string Title, int Index);

    public TextSearchIndex(IEnumerable<SearchDocument<T>> documents, CultureInfo? culture = null)
    {
        this.documents = documents.Select(d => d with { Terms = d.Terms.ToArray() }).ToArray();
        comparison = (culture ?? CultureInfo.CurrentCulture).CompareInfo;
        order = Comparer<Priority>.Create((left, right) =>
        {
            var rank = left.Rank.CompareTo(right.Rank);
            if (rank != 0) return rank;
            var title = comparison.Compare(left.Title, right.Title, CompareOptions.IgnoreCase);
            return title != 0 ? title : left.Index.CompareTo(right.Index);
        });
    }

    public IReadOnlyList<T> Search(string query, int limit = 300, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (limit <= 0 || string.IsNullOrWhiteSpace(query)) return [];
        var best = new PriorityQueue<T, Priority>(Comparer<Priority>.Create((a, b) => order.Compare(b, a)));
        for (var i = 0; i < documents.Length; i++)
        {
            if ((i & 63) == 0) token.ThrowIfCancellationRequested();
            var document = documents[i];
            if (!document.Terms.Any(term => comparison.IndexOf(term, query, CompareOptions.IgnoreCase) >= 0)) continue;
            var rank = comparison.Compare(document.Title, query, CompareOptions.IgnoreCase) == 0 ? 0
                : comparison.IsPrefix(document.Title, query, CompareOptions.IgnoreCase) ? 1 : 2;
            var priority = new Priority(rank, document.Title, i);
            if (best.Count < limit) best.Enqueue(document.Value, priority);
            else if (best.TryPeek(out _, out var worst) && order.Compare(priority, worst) < 0)
            { best.Dequeue(); best.Enqueue(document.Value, priority); }
        }
        token.ThrowIfCancellationRequested();
        return best.UnorderedItems.OrderBy(item => item.Priority, order).Select(item => item.Element).ToArray();
    }
}

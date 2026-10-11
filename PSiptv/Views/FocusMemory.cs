using System.Runtime.CompilerServices;

namespace PSiptv.Views;

internal static class FocusMemory
{
    private sealed record Saved(WeakReference<VisualElement> Element, object? Binding, int Session, string? Key);
    private static readonly ConditionalWeakTable<Page, Saved> saved = new();
    public static void Remember(VisualElement element)
    {
        Element? parent = element;
        while (parent is not null && parent is not Page) parent = parent.Parent;
        if (parent is not Page page) return;
        saved.Remove(page); saved.Add(page, new(new(element), element.BindingContext, Services.AppServices.SessionVersion, ItemKey(element)));
    }
    public static void Restore(Page page)
    {
        if (!saved.TryGetValue(page, out var memory) || memory.Session != Services.AppServices.SessionVersion) return;
        if (memory.Element.TryGetTarget(out var element) && ReferenceEquals(memory.Binding, element.BindingContext) && Visible(element) && element.IsLoaded) { element.Focus(); return; }
        if (memory.Key is null) return;
        foreach (var candidate in Descendants(page))
            if (Visible(candidate) && candidate.IsLoaded && ItemKey(candidate) == memory.Key) { candidate.Focus(); break; }
    }
    private static bool Visible(VisualElement element)
    {
        for (Element? current = element; current is not null; current = current.Parent)
            if (current is VisualElement view && !view.IsVisible) return false;
        return true;
    }
    private static string? ItemKey(VisualElement element) => element is TvFocusableBorder border && (border.FavoriteItem ?? border.BindingContext as Core.MediaItem) is { } item ? Core.CatalogPreferences.ItemKey(item) : null;
    private static IEnumerable<VisualElement> Descendants(IVisualTreeElement root)
    {
        foreach (var child in root.GetVisualChildren())
        { if (child is VisualElement element) yield return element; foreach (var nested in Descendants(child)) yield return nested; }
    }
}

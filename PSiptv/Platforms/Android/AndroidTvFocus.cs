using System.Runtime.CompilerServices;
using Android.Views;
using PSiptv.Views;
using NativeView = Android.Views.View;

namespace PSiptv;

/// <summary>
/// Bridges Android's native D-pad focus to MAUI cards. Border is not a native
/// control, so relying only on VisualElement.Focused leaves RecyclerView cards
/// without a visible focus state on several Android TV implementations.
/// </summary>
internal static class AndroidTvFocus
{
    private static readonly ConditionalWeakTable<NativeView, FocusBridge> Bridges = new();
    private static readonly ConditionalWeakTable<NativeView, CollectionFocusBridge> Collections = new();
    private static WeakReference<TvFocusableBorder>? highlightedCard;

    internal static void Attach(NativeView platformView, TvFocusableBorder card) =>
        Bridges.GetValue(platformView, view => new FocusBridge(view)).Connect(card);

    internal static void AttachCollection(NativeView platformView) =>
        Collections.GetValue(platformView, view => new CollectionFocusBridge(view));

    private static void HighlightCardFor(NativeView? focusedView)
    {
        var target = FindCard(focusedView);
        if (highlightedCard?.TryGetTarget(out var previous) == true && !ReferenceEquals(previous, target))
            previous.SetFocusHighlight(false);
        if (target is null)
        {
            highlightedCard = null;
            return;
        }
        target.SetFocusHighlight(true);
        highlightedCard = new WeakReference<TvFocusableBorder>(target);
    }

    private static TvFocusableBorder? FindCard(NativeView? view)
    {
        for (var current = view; current is not null; current = current.Parent as NativeView)
            if (Bridges.TryGetValue(current, out var bridge) && bridge.TryGetCard(out var card)) return card;

        return view is ViewGroup group ? FindCardBelow(group) : null;
    }

    private static TvFocusableBorder? FindCardBelow(ViewGroup group)
    {
        for (var index = 0; index < group.ChildCount; index++)
        {
            var child = group.GetChildAt(index);
            if (child is null) continue;
            if (Bridges.TryGetValue(child, out var bridge) && bridge.TryGetCard(out var card)) return card;
            if (child is ViewGroup nested && FindCardBelow(nested) is { } descendant) return descendant;
        }
        return null;
    }

    private sealed class FocusBridge
    {
        private WeakReference<TvFocusableBorder>? card;
        private bool longPressed;

        internal FocusBridge(NativeView view)
        {
            view.FocusChange += OnFocusChange;
            view.KeyPress += OnKeyPress;
            view.LongClickable = true;
            view.LongClick += OnLongClick;
        }

        internal void Connect(TvFocusableBorder value)
        {
            card = new WeakReference<TvFocusableBorder>(value);
            value.SetFocusHighlight(value.IsFocused);
        }

        internal bool TryGetCard(out TvFocusableBorder? value)
        {
            value = null;
            return card?.TryGetTarget(out value) == true;
        }

        private void OnFocusChange(object? sender, NativeView.FocusChangeEventArgs e)
        {
            if (e.HasFocus && sender is NativeView view) HighlightCardFor(view);
            else if (card?.TryGetTarget(out var target) == true) target.SetFocusHighlight(false);
        }

        private void OnKeyPress(object? sender, NativeView.KeyEventArgs e)
        {
            if (e.Event?.Action != KeyEventActions.Up || e.KeyCode is not
                (Keycode.DpadCenter or Keycode.Enter or Keycode.NumpadEnter or Keycode.Space)) return;
            if (longPressed)
            {
                longPressed = false;
                e.Handled = true;
                return;
            }
            if (card?.TryGetTarget(out var target) == true) target.ActivateFromRemote();
            e.Handled = true;
        }

        private void OnLongClick(object? sender, NativeView.LongClickEventArgs e)
        {
            longPressed = true;
            if (card?.TryGetTarget(out var target) == true) _ = target.ToggleFavoriteAsync();
            e.Handled = true;
        }
    }

    private sealed class CollectionFocusBridge
    {
        internal CollectionFocusBridge(NativeView view)
        {
            view.ViewTreeObserver!.GlobalFocusChange += OnGlobalFocusChange;
        }

        private static void OnGlobalFocusChange(object? sender, ViewTreeObserver.GlobalFocusChangeEventArgs e) =>
            HighlightCardFor(e.NewFocus);
    }
}

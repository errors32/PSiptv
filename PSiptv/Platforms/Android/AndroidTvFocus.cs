using System.Runtime.CompilerServices;
using Android.Content;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using AndroidX.Core.View;
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
    private static readonly ConditionalWeakTable<NativeView, TextInputBridge> TextInputs = new();
    private static WeakReference<TvFocusableBorder>? highlightedCard;

    internal static void Attach(NativeView platformView, TvFocusableBorder card) =>
        Bridges.GetValue(platformView, view => new FocusBridge(view)).Connect(card);

    internal static void AttachCollection(NativeView platformView) =>
        Collections.GetValue(platformView, view => new CollectionFocusBridge(view));

    internal static void AttachTextInput(NativeView platformView) =>
        TextInputs.GetValue(platformView, view => new TextInputBridge(view));

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

    /// <summary>
    /// Android normally opens the IME after a touch on an EditText. A TV remote
    /// changes native focus and activates it with a key instead, which several
    /// Android TV implementations do not translate into that touch behaviour.
    /// Explicitly request the IME when the field receives focus or is activated.
    /// </summary>
    private sealed class TextInputBridge
    {
        private readonly NativeView host;
        private readonly EditText? editor;
        private bool keyboardWasVisible;
        private bool monitoringKeyboard;

        internal TextInputBridge(NativeView view)
        {
            host = view;
            editor = FindEditor(view);
            if (editor is null) return;

            editor.ShowSoftInputOnFocus = true;
            host.FocusChange += OnFocusChange;
            host.KeyPress += OnKeyPress;
            host.Click += OnClick;
            if (!ReferenceEquals(host, editor))
            {
                editor.FocusChange += OnFocusChange;
                editor.KeyPress += OnKeyPress;
                editor.Click += OnClick;
            }
        }

        private static EditText? FindEditor(NativeView view)
        {
            if (view is EditText editText) return editText;
            if (view is not ViewGroup group) return null;
            for (var index = 0; index < group.ChildCount; index++)
                if (group.GetChildAt(index) is { } child && FindEditor(child) is { } found) return found;
            return null;
        }

        private void OnFocusChange(object? sender, NativeView.FocusChangeEventArgs e)
        {
            if (e.HasFocus) ShowKeyboard();
        }

        private void OnClick(object? sender, EventArgs e) => ShowKeyboard();

        private void OnKeyPress(object? sender, NativeView.KeyEventArgs e)
        {
            if (e.Event?.Action != KeyEventActions.Up || e.KeyCode is not
                (Keycode.DpadCenter or Keycode.Enter or Keycode.NumpadEnter)) return;
            ShowKeyboard();
            e.Handled = true;
        }

        private void ShowKeyboard()
        {
            if (editor is null) return;
            if (!editor.HasFocus) editor.RequestFocus();
            editor.Post(() =>
            {
                if (!editor.HasFocus || !editor.IsShown) return;
                var input = editor.Context?.GetSystemService(Context.InputMethodService) as InputMethodManager;
                input?.ShowSoftInput(editor, ShowFlags.Implicit);
                MonitorKeyboardVisibility();
            });
        }

        private void MonitorKeyboardVisibility()
        {
            if (editor is null || monitoringKeyboard) return;
            monitoringKeyboard = true;
            editor.Post(CheckKeyboardVisibility);
        }

        private void CheckKeyboardVisibility()
        {
            if (editor is null || !editor.IsAttachedToWindow || !editor.HasFocus)
            {
                keyboardWasVisible = false;
                monitoringKeyboard = false;
                return;
            }

            var keyboardVisible = ViewCompat.GetRootWindowInsets(editor)?
                .IsVisible(WindowInsetsCompat.Type.Ime()) == true;
            if (keyboardVisible)
            {
                keyboardWasVisible = true;
            }
            else if (keyboardWasVisible)
            {
                // Android deliberately keeps an EditText focused when Back closes
                // the IME. On a television that traps subsequent D-pad input in
                // the editor, so explicitly return focus to the form.
                keyboardWasVisible = false;
                monitoringKeyboard = false;
                var next = editor.FocusSearch(FocusSearchDirection.Down)
                    ?? editor.FocusSearch(FocusSearchDirection.Up);
                editor.ClearFocus();
                if (!ReferenceEquals(host, editor)) host.ClearFocus();
                next?.RequestFocus();
                return;
            }

            editor.PostDelayed(CheckKeyboardVisibility, 100);
        }
    }
}

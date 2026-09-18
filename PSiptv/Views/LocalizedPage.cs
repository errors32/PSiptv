using PSiptv.Services;
namespace PSiptv.Views;

public class LocalizedPage : ContentPage
{
    public new async Task<string> DisplayActionSheetAsync(string title, string? cancel, string? destruction, params string[] buttons)
    {
        var translated = buttons.Select(LanguageService.Text).ToArray();
        var answer = await base.DisplayActionSheetAsync(LanguageService.Text(title), cancel is null ? null : LanguageService.Text(cancel), destruction is null ? null : LanguageService.Text(destruction), translated);
        var index = Array.IndexOf(translated, answer);
        return index >= 0 ? buttons[index] : answer;
    }
    public new Task DisplayAlertAsync(string title, string message, string cancel) => base.DisplayAlertAsync(LanguageService.Text(title), LanguageService.Text(message), LanguageService.Text(cancel));
    public new Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel) => base.DisplayAlertAsync(LanguageService.Text(title), LanguageService.Text(message), LanguageService.Text(accept), LanguageService.Text(cancel));
    public new Task<string> DisplayPromptAsync(string title, string message, string accept = "OK", string cancel = "Cancel", string? placeholder = null, int maxLength = -1, Keyboard? keyboard = null, string initialValue = "")
        => base.DisplayPromptAsync(LanguageService.Text(title), LanguageService.Text(message), LanguageService.Text(accept), LanguageService.Text(cancel), placeholder is null ? null : LanguageService.Text(placeholder), maxLength, keyboard, initialValue);
}

internal sealed class LocalizedLabel : Label
{
    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName != nameof(Text) || Text is null) return;
        var translated = LanguageService.Text(Text);
        if (translated != Text) Text = translated;
    }
}

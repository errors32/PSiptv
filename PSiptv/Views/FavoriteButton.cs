using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class FavoriteButton : Button
{
    public FavoriteButton()
    {
        FontSize = 24; WidthRequest = 48; HeightRequest = 48;
        Padding = 0; CornerRadius = 12; BackgroundColor = Colors.Transparent;
        SetDynamicResource(TextColorProperty, "Accent");
        BindingContextChanged += (_, _) => Refresh();
        Loaded += (_, _) => { FavoritesService.Changed -= Refresh; FavoritesService.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => FavoritesService.Changed -= Refresh;
        Clicked += async (_, _) =>
        {
            if (BindingContext is not MediaItem item) return;
            IsEnabled = false;
            try { await FavoritesService.ToggleAsync(item); }
            catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
            finally { IsEnabled = true; Refresh(); }
        };
        Refresh();
    }

    private void Refresh()
    {
        var selected = BindingContext is MediaItem item && FavoritesService.Contains(item);
        Text = selected ? "★" : "☆";
        var label = LanguageService.Text(selected ? "Remover dos favoritos" : "Guardar nos favoritos");
        SemanticProperties.SetDescription(this, label);
        ToolTipProperties.SetText(this, label);
    }
}

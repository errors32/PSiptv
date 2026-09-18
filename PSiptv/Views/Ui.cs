using CommunityToolkit.Maui.Behaviors;
using PSiptv.Core;
using PSiptv.Services;
using Microsoft.Maui.Controls.Shapes;

namespace PSiptv.Views;

public static class Ui
{
#if ANDROID
    public static bool IsTelevision => !DeviceProfile.IsAutomotive &&
        (DeviceInfo.Idiom == DeviceIdiom.TV ||
         Android.App.Application.Context.PackageManager?.HasSystemFeature("android.software.leanback") == true);
#else
    public static bool IsTelevision => false;
#endif
    public static bool UsesLargeControls => IsTelevision || DeviceProfile.IsAutomotive;

    public static Label Text(string text, double size = 14, bool muted = false)
    {
        var fontSize = DeviceProfile.IsAutomotive ? Math.Max(24, size * 1.15)
            : IsTelevision ? Math.Clamp(size, 14, 22) : size;
        var label = new LocalizedLabel { Text = LanguageService.Text(text), FontSize = fontSize };
        label.SetDynamicResource(Label.TextColorProperty, muted ? "Muted" : "Ink");
        return label;
    }

    public static Label FontIcon(string glyph, double size = 22)
    {
        var icon = new Label
        {
            Text = glyph,
            FontFamily = "FontAwesomeFreeSolid",
            FontSize = size,
            WidthRequest = 32,
            HeightRequest = 32,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        icon.SetDynamicResource(Label.TextColorProperty, "Ink");
        return icon;
    }

    public static FontImageSource FontIconSource(string glyph, double size = 30, string fontFamily = "FontAwesomeFreeSolid")
    {
        var icon = new FontImageSource
        {
            Glyph = glyph,
            FontFamily = fontFamily,
            Size = size
        };
        icon.SetDynamicResource(FontImageSource.ColorProperty, "Muted");
        return icon;
    }

    public static Button Button(string text, Func<Task> action, bool primary = false)
    {
        var button = new Button
        {
            Text = LanguageService.Text(text), CornerRadius = 12,
            Padding = DeviceProfile.IsAutomotive ? new Thickness(24, 16)
                : IsTelevision ? new Thickness(14, 8) : new Thickness(16, 12),
            FontSize = DeviceProfile.IsAutomotive ? 24 : IsTelevision ? 16 : 14,
            MinimumHeightRequest = DeviceProfile.IsAutomotive ? 64 : IsTelevision ? 48 : 46
        };
        button.SetDynamicResource(VisualElement.BackgroundColorProperty, primary ? "Accent" : "Surface");
        button.SetDynamicResource(Microsoft.Maui.Controls.Button.BorderColorProperty, "Accent");
        button.Focused += (_, _) => { button.BorderWidth = 3; button.Scale = IsTelevision ? 1.04 : 1; };
        button.Unfocused += (_, _) => { button.BorderWidth = 0; button.Scale = 1; };
        if (primary) button.TextColor = Color.FromArgb("#071520");
        else button.SetDynamicResource(Microsoft.Maui.Controls.Button.TextColorProperty, "Ink");
        button.Clicked += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); }
            catch (OperationCanceledException ex)
            {
                if (!ex.CancellationToken.IsCancellationRequested) await ErrorAsync(button, ex);
            }
            catch (Exception ex) { await ErrorAsync(button, ex); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    public static Entry Entry(string placeholder, bool password = false)
    {
        var entry = new Entry { Placeholder = LanguageService.Text(placeholder), IsPassword = password, MinimumHeightRequest = 48 };
        entry.SetDynamicResource(Microsoft.Maui.Controls.Entry.TextColorProperty, "Ink");
        entry.SetDynamicResource(Microsoft.Maui.Controls.Entry.PlaceholderColorProperty, "Muted");
        entry.SetDynamicResource(VisualElement.BackgroundColorProperty, "Surface");
        return entry;
    }

    public static VerticalStackLayout Stack(params View[] views)
    {
        var stack = new VerticalStackLayout { Spacing = IsTelevision ? 8 : 12 };
        foreach (var view in views) stack.Add(view);
        return stack;
    }

    public static Border Card(View content)
    {
        var border = new Border { Content = content, Padding = IsTelevision ? 12 : 18, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 16 } };
        border.SetDynamicResource(VisualElement.BackgroundColorProperty, "Surface");
        return border;
    }

    public static TvFocusableBorder FocusableCard(View content, Action<object?> activate)
    {
        var border = new TvFocusableBorder
        {
            Content = content, Padding = IsTelevision ? 10 : 18, StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Activate = activate
        };
        border.SetDynamicResource(VisualElement.BackgroundColorProperty, "Surface");
        border.SetDynamicResource(Border.StrokeProperty, "Accent");
        border.Focused += (_, _) => border.SetFocusHighlight(true);
        border.Unfocused += (_, _) => border.SetFocusHighlight(false);
        if (!IsTelevision)
        {
            border.Behaviors.Add(new TouchBehavior
            {
                LongPressCommand = new Command(async () => await border.ToggleFavoriteAsync())
            });
        }
        if (UsesLargeControls)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => border.ActivateFromRemote();
            border.GestureRecognizers.Add(tap);
        }
        return border;
    }

    public static void Page(ContentPage page, string title)
    {
        page.Title = LanguageService.Text(title);
        page.Loaded += (_, _) =>
        {
            Localize(page);
            if (!IsTelevision) return;
            ApplyTelevisionLayout(page);
            page.Dispatcher.Dispatch(() => FocusFirstTelevisionControl(page));
        };
        page.SetDynamicResource(VisualElement.BackgroundColorProperty, "Canvas");
    }

    /// <summary>
    /// Applies the common ten-foot layout rules to every secondary page. This
    /// keeps pages that do not need a bespoke TV composition dense, wide and
    /// usable with a D-pad instead of leaving them with phone-sized gutters.
    /// </summary>
    private static void ApplyTelevisionLayout(ContentPage page)
    {
        foreach (var element in VisualElements(page))
        {
            if (element is Layout layout)
                layout.Padding = Compact(layout.Padding, 14);

            if (element is VerticalStackLayout vertical)
                vertical.Spacing = Math.Min(vertical.Spacing, 8);
            else if (element is HorizontalStackLayout horizontal)
                horizontal.Spacing = Math.Min(horizontal.Spacing, 8);
            else if (element is Grid grid)
            {
                grid.RowSpacing = Math.Min(grid.RowSpacing, 8);
                grid.ColumnSpacing = Math.Min(grid.ColumnSpacing, 8);
            }

            // Form pages used an 680/800 dp phone/tablet column. A wider cap
            // avoids needless vertical scrolling on a 16:9 television.
            if (element is View view && element.Parent is ScrollView && element.MaximumWidthRequest is > 0 and < 1100)
            {
                element.MaximumWidthRequest = 1100;
                view.HorizontalOptions = LayoutOptions.Fill;
            }
        }
    }

    private static Thickness Compact(Thickness value, double maximum) => new(
        Math.Min(value.Left, maximum), Math.Min(value.Top, maximum),
        Math.Min(value.Right, maximum), Math.Min(value.Bottom, maximum));

    private static void FocusFirstTelevisionControl(ContentPage page)
    {
        var elements = VisualElements(page).Where(element => element.IsVisible && element.IsEnabled).ToArray();
        if (elements.Any(element => element.IsFocused)) return;

        // Prefer navigable collections and selectors. Text fields come last so
        // merely opening a page never raises the on-screen keyboard.
        var target = elements
            .Where(element => element is TvFocusableBorder or CollectionView or Picker or Microsoft.Maui.Controls.Button or Switch or Microsoft.Maui.Controls.Entry)
            .OrderBy(element => element switch
            {
                TvFocusableBorder => 0,
                CollectionView => 1,
                Picker => 2,
                Microsoft.Maui.Controls.Button => 3,
                Switch => 4,
                Microsoft.Maui.Controls.Entry => 5,
                _ => 6
            })
            .FirstOrDefault();
        target?.Focus();
    }

    private static IEnumerable<VisualElement> VisualElements(IVisualTreeElement root)
    {
        foreach (var child in root.GetVisualChildren())
        {
            if (child is VisualElement element) yield return element;
            foreach (var descendant in VisualElements(child)) yield return descendant;
        }
    }

    private static void Localize(IVisualTreeElement node)
    {
        if (node is SearchBar search && search.Placeholder is { } hint) search.Placeholder = LanguageService.Text(hint);
        if (node is Picker picker)
        {
            if (picker.Title is { } title) picker.Title = LanguageService.Text(title);
            if (picker.ItemsSource is string[] values && !values.SequenceEqual(values.Select(LanguageService.Text))) { var index = picker.SelectedIndex; picker.ItemsSource = values.Select(LanguageService.Text).ToArray(); picker.SelectedIndex = index; }
        }
        foreach (var child in node.GetVisualChildren()) Localize(child);
    }

    public static Task ErrorAsync(Element sender, Exception ex)
    {
        var page = sender as Page;
        for (var parent = sender.Parent; page is null && parent is not null; parent = parent.Parent) page = parent as Page;
        // Network exception messages often contain provider credentials in URLs.
        var message = ex switch
        {
            HttpRequestException => "Não foi possível contactar o fornecedor. Verifique o endereço e a ligação à Internet.",
            TaskCanceledException => "O fornecedor demorou demasiado tempo a responder. Tente novamente.",
            UnauthorizedAccessException => ex.Message,
            System.Text.Json.JsonException => "O fornecedor devolveu uma resposta inválida.",
            System.Xml.XmlException => "O guia TV devolvido não é XML válido.",
            InvalidOperationException => ex.Message,
            _ => "Não foi possível concluir a operação. Verifique os dados e tente novamente."
        };
        return page is null ? Task.CompletedTask : LanguageService.AlertAsync(page, "PSiptv", message);
    }

    public static CollectionView MediaList()
    {
        CollectionView collection = null!;
        collection = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemsLayout = new GridItemsLayout(1, ItemsLayoutOrientation.Vertical)
            {
                HorizontalItemSpacing = 10, VerticalItemSpacing = 10
            },
            EmptyView = Text("Nenhum conteúdo nesta categoria.", 16, true),
            ItemTemplate = new DataTemplate(() =>
            {
                var image = new LogoImage { WidthRequest = 56, HeightRequest = 56, Aspect = Aspect.AspectFit };

                var title = Text("", 16); title.FontAttributes = FontAttributes.Bold;
                title.SetBinding(Label.TextProperty, "Name");
                var category = Text("", 12, true); category.SetBinding(Label.TextProperty, "Category");
                var grid = new Grid
                {
                    ColumnDefinitions = [new ColumnDefinition(new GridLength(64)), new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(new GridLength(IsTelevision ? 0 : 48)), new ColumnDefinition(new GridLength(24))],
                    ColumnSpacing = 8
                };
                var favorite = new FavoriteButton { IsVisible = !IsTelevision, IsEnabled = !IsTelevision };
                grid.Add(image); grid.Add(Stack(title, category), 1); grid.Add(favorite, 2); grid.Add(Text("›", 26, true), 3);
                var card = FocusableCard(grid, value => collection.SelectedItem = value); card.Margin = new Thickness(0, 0, 0, 8);
                return card;
            })
        };
        return collection;
    }

    public static (double Width, double Height) Viewport(VisualElement view)
    {
        if (view.Width > 0 && view.Height > 0) return (view.Width, view.Height);
        var display = DeviceDisplay.Current.MainDisplayInfo;
        var density = Math.Max(1, display.Density);
        return (display.Width / density, display.Height / density);
    }

    public static bool IsLandscape(VisualElement view)
    {
        var size = Viewport(view);
        return size.Width > size.Height;
    }

    public static void UpdateColumns(CollectionView collection, double width, double minimumItemWidth = 320, int maximum = 4)
    {
        if (collection.ItemsLayout is not GridItemsLayout layout || width <= 0) return;
        layout.Span = Math.Clamp((int)(width / minimumItemWidth), 1, maximum);
    }
}

public sealed class TvFocusableBorder : Border
{
    internal Action<object?>? Activate { get; init; }
    internal MediaItem? FavoriteItem { get; set; }

    internal void ActivateFromRemote() => Activate?.Invoke(BindingContext);

    internal async Task ToggleFavoriteAsync()
    {
        var item = FavoriteItem ?? BindingContext as MediaItem;
        if (item is null) return;
        try { await FavoritesService.ToggleAsync(item); }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
    }

    internal void SetFocusHighlight(bool focused)
    {
        StrokeThickness = focused ? (Ui.IsTelevision ? 5 : 4) : 0;
        Scale = focused ? (Ui.IsTelevision ? 1.06 : 1.03) : 1;
        SetDynamicResource(VisualElement.BackgroundColorProperty, focused ? "ProfileTile" : "Surface");
        ZIndex = focused ? 10 : 0;
    }
}

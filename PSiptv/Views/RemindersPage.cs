using PSiptv.Core;
using PSiptv.Services;

namespace PSiptv.Views;

public sealed class RemindersPage : LocalizedPage
{
    private readonly CollectionView list = new();
    private readonly Label status = Ui.Text("A carregar lembretes…", 14, true);

    public RemindersPage()
    {
        Ui.Page(this, "Lembretes de programas");
        list.EmptyView = Ui.Text("Não existem lembretes futuros neste perfil.", 16, true);
        list.ItemsLayout = Ui.IsTelevision
            ? new GridItemsLayout(2, ItemsLayoutOrientation.Vertical) { HorizontalItemSpacing = 8, VerticalItemSpacing = 8 }
            : new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { ItemSpacing = 10 };
        list.ItemTemplate = new DataTemplate(() =>
        {
            var title = Ui.Text("", 18);
            title.FontAttributes = FontAttributes.Bold;
            title.SetBinding(Label.TextProperty, nameof(ReminderRow.Title));
            var details = Ui.Text("", 13, true);
            details.SetBinding(Label.TextProperty, nameof(ReminderRow.Details));
            Button remove = null!;
            remove = Ui.Button("Remover lembrete", async () =>
            {
                if (remove.BindingContext is not ReminderRow row) return;
                await ReminderService.RemoveAsync(row.Reminder);
                await LoadAsync();
            });
            return Ui.Card(Ui.Stack(title, details, remove));
        });
        var root = new Grid
        {
            Padding = 20, RowSpacing = 12,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)]
        };
        root.Add(status);
        root.Add(list, 0, 1);
        Content = root;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var reminders = await ReminderService.LoadActiveProfileAsync();
        list.ItemsSource = reminders.Select(reminder => new ReminderRow(reminder,
            reminder.ProgrammeTitle,
            $"{reminder.ChannelName} · {AppOptions.FormatTime(reminder.Start)}")).ToArray();
        status.Text = LanguageService.Format("{0} lembretes futuros · Perfil {1}",
            reminders.Count, UserProfileService.Active.Name);
    }

    private sealed record ReminderRow(ProgrammeReminder Reminder, string Title, string Details);
}

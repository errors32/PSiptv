using PSiptv.Services;
using Microsoft.Maui.Layouts;

namespace PSiptv.Views;

public sealed class UserProfilesPage : LocalizedPage
{
    private readonly Grid profiles = new() { ColumnSpacing = 8, RowSpacing = 8 };
    private readonly Func<UserProfile, Task> changeProfile;
    private bool busy;

    public UserProfilesPage(Func<UserProfile, Task> changeProfile)
    {
        this.changeProfile = changeProfile;
        Ui.Page(this, "Perfis de utilizador");
        var profileColumns = Ui.IsTelevision ? 2 : 1;
        for (var i = 0; i < profileColumns; i++)
            profiles.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        var add = Ui.Button("+ Adicionar perfil", AddAsync, true);
        var content = Ui.Stack(
            Ui.Text("Quem está a ver?", 28),
            Ui.Text("Cada perfil tem favoritos e histórico próprios. As listas e os catálogos são partilhados.", 13, true),
            add,
            profiles);
        content.Padding = 24;
        content.MaximumWidthRequest = 800;
        Content = new ScrollView { Content = content };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await UserProfileService.LoadAsync();
            Render();
        }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
    }

    private void Render()
    {
        profiles.Clear();
        profiles.RowDefinitions.Clear();
        var columns = Ui.IsTelevision ? 2 : 1;
        var index = 0;
        foreach (var profile in UserProfileService.Profiles)
        {
            var active = profile.Id == UserProfileService.Active.Id;
            var avatar = Ui.Text(profile.Avatar, 34);
            avatar.VerticalOptions = LayoutOptions.Center;
            var name = Ui.Text(profile.Name, 18);
            name.FontAttributes = FontAttributes.Bold;
            var state = Ui.Text(active ? "Perfil ativo" : "", 12, true);
            var identity = new Grid
            {
                ColumnDefinitions = [new(new GridLength(52)), new(GridLength.Star)],
                ColumnSpacing = 10
            };
            identity.Add(avatar);
            identity.Add(Ui.Stack(name, state), 1);
            var actions = new FlexLayout { Direction = FlexDirection.Row, Wrap = FlexWrap.Wrap,
                JustifyContent = FlexJustify.Start, AlignItems = FlexAlignItems.Center };
            var edit = Ui.Button("Editar perfil", () => EditAsync(profile));
            edit.Margin = new Thickness(0, 0, 8, 8);
            actions.Add(edit);
            if (!active)
            {
                var use = Ui.Button("Usar este perfil", () => SelectAsync(profile), true);
                use.Margin = new Thickness(0, 0, 8, 8);
                actions.Add(use);
                if (profile.Id != UserProfileService.DefaultProfile.Id)
                {
                    var delete = Ui.Button("Eliminar", () => DeleteAsync(profile));
                    delete.Margin = new Thickness(0, 0, 8, 8);
                    actions.Add(delete);
                }
            }
            if (index % columns == 0)
                profiles.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            profiles.Add(Ui.Card(Ui.Stack(identity, actions)), index % columns, index / columns);
            index++;
        }
    }

    private async Task EditAsync(UserProfile profile)
    {
        if (busy) return;
        var name = await DisplayPromptAsync(LanguageService.Text("Editar perfil"),
            LanguageService.Text("Nome do perfil"), LanguageService.Text("Continuar"),
            LanguageService.Text("Cancelar"), initialValue: profile.Name, maxLength: 30);
        if (name is null) return;
        var avatars = UserProfileService.AvatarChoices.ToArray();
        var avatar = await LanguageService.ActionSheetAsync(this, "Escolher ícone", "Cancelar", null, avatars);
        if (!avatars.Contains(avatar)) return;
        busy = true;
        try
        {
            await UserProfileService.UpdateAsync(profile.Id, name, avatar);
            Render();
        }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
        finally { busy = false; }
    }

    private async Task AddAsync()
    {
        if (busy) return;
        var name = await DisplayPromptAsync(LanguageService.Text("Adicionar perfil"),
            LanguageService.Text("Nome do perfil"), LanguageService.Text("Guardar"),
            LanguageService.Text("Cancelar"), maxLength: 30);
        if (name is null) return;
        busy = true;
        try
        {
            await UserProfileService.CreateAsync(name);
            Render();
        }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
        finally { busy = false; }
    }

    private async Task SelectAsync(UserProfile profile)
    {
        if (busy) return;
        busy = true;
        try
        {
            await changeProfile(profile);
            await Navigation.PopToRootAsync(false);
        }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
        finally { busy = false; }
    }

    private async Task DeleteAsync(UserProfile profile)
    {
        if (busy || !await DisplayAlertAsync(LanguageService.Text("Eliminar perfil"),
                LanguageService.Format("Eliminar o perfil «{0}» e todos os seus dados e gravações?", profile.Name),
                LanguageService.Text("Eliminar"), LanguageService.Text("Cancelar"))) return;
        busy = true;
        try
        {
            var accounts = await AppServices.Accounts.LoadAsync();
            foreach (var account in accounts)
            {
                await FavoritesService.DeleteProfileAsync(account.Id, profile.Id);
                await HistoryService.DeleteProfileAsync(account.Id, profile.Id);
                await ReminderService.DeleteProfileAsync(account.Id, profile.Id);
                await DvrService.DeleteProfileAsync(account.Id, profile.Id);
                await OfflineDownloadService.DeleteProfileAsync(account.Id, profile.Id);
            }
            await UserProfileService.DeleteAsync(profile.Id);
            Render();
        }
        catch (Exception ex) { await Ui.ErrorAsync(this, ex); }
        finally { busy = false; }
    }
}

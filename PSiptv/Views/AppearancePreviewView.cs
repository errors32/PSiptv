using PSiptv.Services;

namespace PSiptv.Views;

public sealed class AppearancePreviewView : ContentView
{
    public AppearancePreviewView()
    {
        Loaded += (_, _) => { DesignService.Changed += Rebuild; Rebuild(); };
        Unloaded += (_, _) => DesignService.Changed -= Rebuild;
        Rebuild();
    }
    private void Rebuild()
    {
        var title = Ui.Text("PSiptv · " + DesignService.Name(DesignService.Mode), 22);
        View Example(string heading = "Exemplo de conteúdo", int height = 90) => Ui.Card(Ui.Stack(
            new Image { Source = "psiptv_mark.svg", HeightRequest = height, Aspect = Aspect.AspectFit }, Ui.Text(heading, 20),
            Ui.Text("Conteúdo ilustrativo", 12, true), Ui.Button("Retomar", () => Task.CompletedTask, true)));
        var example = Example();
        var nav = Ui.Stack(Ui.Button("Início", () => Task.CompletedTask), Ui.Button("TV ao Vivo", () => Task.CompletedTask));
        var layout = new Grid { ColumnSpacing = 12, RowSpacing = 12, RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)] };
        layout.Add(title);
        if (DesignService.Mode == InterfaceDesign.Library)
        {
            layout.ColumnDefinitions = [new(new GridLength(120)), new(GridLength.Star)];
            Grid.SetColumnSpan(title, 2); layout.Add(nav, 0, 1); layout.Add(example, 1, 1);
        }
        else if (DesignService.Mode == InterfaceDesign.Magazine)
        {
            layout.Add(Ui.Stack(Ui.Text("As tuas histórias", 28), Ui.Text("01 · Continuar a ver", 18), example, Ui.Text("02 · Agora e a seguir", 18)), 0, 1);
        }
        else if (DesignService.Mode == InterfaceDesign.TvGuide)
        {
            var guide = Ui.Stack(Ui.Text("18:00                       19:00", 12, true), Ui.Card(Ui.Stack(Ui.Text("RTP 1 · Programa atual", 16), Ui.Text("A seguir · Notícias", 14))), Ui.Card(Ui.Text("RTP 2 · Documentário", 16)));
            layout.Add(guide, 0, 1);
        }
        else if (DesignService.Mode == InterfaceDesign.Modular)
        {
            var modules = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)], ColumnSpacing = 8, RowSpacing = 8 };
            modules.Add(example); Grid.SetColumnSpan(example, 2);
            modules.Add(Ui.Card(Ui.Text("Favoritos", 16)), 0, 1); modules.Add(Ui.Card(Ui.Text("Rádio", 16)), 1, 1);
            layout.Add(modules, 0, 1);
        }
        else if (DesignService.Mode == InterfaceDesign.Console)
        {
            layout.Add(Ui.Stack(Ui.Text("Início   ·   TV   ·   Filmes   ·   Rádio", 18), Example("Pronto para continuar?", 130)), 0, 1);
        }
        else if (DesignService.Mode == InterfaceDesign.Cinema) layout.Add(Ui.Stack(Ui.Text("Descobrir · TV · Biblioteca", 14), Example("Continuar a ver", 180)), 0, 1);
        else { layout.Add(Ui.Stack(nav, example), 0, 1); }
        Content = Ui.Card(layout);
    }
}

public sealed partial class SettingsPage
{
    private View BuildExperienceAppearance() => Ui.Stack(Ui.Text("Pré-visualização", 20), new AppearancePreviewView(),
        ExperienceChoice("Tamanho do texto", "ui.textScale", [("Pequeno", "0.9"), ("Normal", "1"), ("Grande", "1.2"), ("Muito grande", "1.4")], "1"),
        ExperienceChoice("Densidade dos cartões", "ui.density", [("Normal", "normal"), ("Compacta", "compact")], "normal"),
        ExperienceChoice("Reduzir animações", "ui.reduceMotion", [("Não", "false"), ("Sim", "true")], "false"),
        Ui.Button("Personalizar página inicial", () => Navigation.PushAsync(new HomeCustomizationPage())));
    private View ExperienceChoice(string title, string key, (string Name, string Value)[] choices, string fallback)
    {
        var selected = Preferences.Default.Get(key, fallback);
        var picker = new Picker { Title = title, ItemsSource = choices.Select(c => c.Name).ToArray(), SelectedIndex = Math.Max(0, Array.FindIndex(choices, c => c.Value == selected)) };
        picker.SetDynamicResource(Picker.TextColorProperty, "Ink");
        picker.SelectedIndexChanged += (_, _) => { if (picker.SelectedIndex < 0) return; Preferences.Default.Set(key, choices[picker.SelectedIndex].Value); DesignService.Refresh(); if (key == "tv.watchNext") _ = TvDiscoveryService.RefreshAsync(); };
        return Ui.Stack(Ui.Text(title), picker);
    }
    private View BuildExperienceSettings() => Ui.Stack(
        ExperienceChoice("Alternativas automáticas de canais", "channels.fallback", [("Desativadas", "false"), ("Ativadas", "true")], "false"),
        ExperienceChoice("Qualidade preferida", "channels.quality", [("Original", "auto"), ("SD", "SD"), ("HD", "HD"), ("FHD", "FHD"), ("4K", "4K")], "auto"),
        Ui.Button("Agrupar canais e escolher versões", () => Navigation.PushAsync(new ChannelAlternativesPage())),
        ExperienceChoice("Margem antes das gravações", "dvr.before", [("Sem margem", "0"), ("2 minutos", "2"), ("5 minutos", "5"), ("10 minutos", "10")], "0"),
        ExperienceChoice("Margem depois das gravações", "dvr.after", [("Sem margem", "0"), ("5 minutos", "5"), ("10 minutos", "10"), ("30 minutos", "30")], "0"),
        ExperienceChoice("Limite de gravações simultâneas por lista", "dvr.limit", [("1", "1"), ("2", "2"), ("3", "3"), ("4", "4")], "2"),
        ExperienceChoice("Limpeza de gravações concluídas", "dvr.retention", [("Manual", "0"), ("Após 7 dias", "7"), ("Após 30 dias", "30"), ("Após 90 dias", "90")], "0"),
        ExperienceChoice("Continuar a ver no Android TV", "tv.watchNext", [("Desativado", "false"), ("Ativado", "true")], "false"),
        ExperienceChoice("Atalho de pesquisa", "shortcut.search", [("S", "S"), ("F3", "F3")], "S"),
        ExperienceChoice("Atalho do início", "shortcut.home", [("H", "H"), ("F2", "F2")], "H"),
        ExperienceChoice("Atalho de ecrã inteiro", "shortcut.fullscreen", [("F", "F"), ("F11", "F11")], "F"),
        Ui.Text("As alternativas são tentadas depois de esgotar as reconexões. A limpeza automática só elimina gravações concluídas. Os atalhos não atuam enquanto escreve num campo de texto.", 12, true));
}

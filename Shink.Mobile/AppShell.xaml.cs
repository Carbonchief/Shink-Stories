using Shink.Mobile.Pages;

namespace Shink.Mobile;

public partial class AppShell : Shell
{
    public AppShell(IServiceProvider services)
    {
        InitializeComponent();

        Items.Clear();
        Routing.RegisterRoute(nameof(StoryDetailPage), typeof(StoryDetailPage));

        var tabs = new TabBar();
        tabs.Items.Add(CreateTab("Luister", () => services.GetRequiredService<LuisterPage>()));
        tabs.Items.Add(CreateTab("Gratis", () => services.GetRequiredService<GratisPage>()));
        tabs.Items.Add(CreateTab("Tuis", () => services.GetRequiredService<HomePage>()));
        tabs.Items.Add(CreateTab("Meer", () => services.GetRequiredService<AboutPage>()));
        tabs.Items.Add(CreateTab("Rekening", () => services.GetRequiredService<AccountPage>()));
        Items.Add(tabs);
    }

    private static Tab CreateTab(string title, Func<Page> pageFactory)
    {
        var tab = new Tab { Title = title };
        tab.Items.Add(new ShellContent
        {
            Title = title,
            ContentTemplate = new DataTemplate(pageFactory)
        });
        return tab;
    }
}

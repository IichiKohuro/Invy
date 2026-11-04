using Invy.Client.Core.Pages;
using Invy.Client.Core.Shell;

namespace Invy.Client.Tests.Shell;

public class ShellViewModelTests
{
    [Fact]
    public void Starts_on_home_page()
    {
        var shell = new ShellViewModel(new FakeThemeService());

        shell.SelectedItem!.Key.ShouldBe("home");
        shell.CurrentPage.ShouldBeOfType<HomePageViewModel>();
    }

    [Fact]
    public void Navigation_reuses_page_instances()
    {
        var shell = new ShellViewModel(new FakeThemeService());
        var home = shell.CurrentPage;

        shell.Navigate("places");
        shell.CurrentPage.ShouldBeOfType<PlaceholderPageViewModel>().Description.ShouldBe("Нет подключения к серверу Invy.");

        shell.Navigate("home");
        shell.CurrentPage.ShouldBeSameAs(home);
    }

    [Fact]
    public void Unknown_key_keeps_current_page()
    {
        var shell = new ShellViewModel(new FakeThemeService());

        shell.Navigate("no-such-page");

        shell.SelectedItem!.Key.ShouldBe("home");
    }

    [Fact]
    public void Footer_settings_page_applies_theme()
    {
        var themes = new FakeThemeService();
        var shell = new ShellViewModel(themes);

        shell.Navigate("settings");
        var settings = shell.CurrentPage.ShouldBeOfType<SettingsPageViewModel>();
        settings.ThemeIndex = (int)ThemeMode.Dark;

        themes.Mode.ShouldBe(ThemeMode.Dark);
    }

    private sealed class FakeThemeService : IThemeService
    {
        public ThemeMode Mode { get; private set; }

        public void Apply(ThemeMode mode) => Mode = mode;
    }
}

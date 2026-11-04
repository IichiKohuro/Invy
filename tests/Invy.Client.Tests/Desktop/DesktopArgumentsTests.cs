using Invy.Desktop.Hosting;

namespace Invy.Client.Tests.Desktop;

public class DesktopArgumentsTests
{
    [Fact]
    public void Empty_arguments_mean_normal_start() =>
        DesktopArguments.Parse([]).ShouldBe(new DesktopArguments(Background: false, QuickSearch: false, Search: null));

    [Fact]
    public void Parses_flags_case_insensitively() =>
        DesktopArguments.Parse(["--BACKGROUND", "--quick-search"]).ShouldBe(new DesktopArguments(true, true, null));

    [Fact]
    public void Parses_search_query()
    {
        DesktopArguments.Parse(["--search", "  кабель usb-c "]).Search.ShouldBe("кабель usb-c");
        DesktopArguments.Parse(["--search"]).Search.ShouldBeNull();
    }
}

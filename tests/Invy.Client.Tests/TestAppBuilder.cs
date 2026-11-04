using Avalonia;
using Avalonia.Headless;
using Invy.Client.Tests;
using Invy.Client.UI;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Invy.Client.Tests;

/// <summary>Приложение для headless-тестов UI (без окна и видеокарты).</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

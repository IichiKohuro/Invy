using Avalonia;
using Avalonia.Headless;
using Invy.Client.Tests;
using Invy.Client.UI;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Invy.Client.Tests;

/// <summary>
/// Приложение для headless-тестов UI: без окна и видеокарты, но с настоящей отрисовкой Skia,
/// чтобы можно было снимать скриншоты и проверять пиксели.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

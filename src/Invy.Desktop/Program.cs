using Avalonia;
using Invy.Client.UI;
using Invy.Desktop.Hosting;

namespace Invy.Desktop;

internal static class Program
{
    private const string AppId = "Invy.Desktop";

    [STAThread]
    public static int Main(string[] args)
    {
        using var instance = SingleInstance.TryAcquire(AppId);
        if (instance is null)
        {
            // Уже запущено: передаём аргументы работающему экземпляру (по умолчанию он покажет окно).
            SingleInstance.SendAsync(AppId, args, TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            return 0;
        }

        using var host = new DesktopHost(instance, DesktopArguments.Parse(args));
        return Configure(AppBuilder.Configure(() => new App(host))).StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Используется дизайнером Avalonia.</summary>
    public static AppBuilder BuildAvaloniaApp() => Configure(AppBuilder.Configure<App>());

    private static AppBuilder Configure(AppBuilder builder) =>
        builder
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

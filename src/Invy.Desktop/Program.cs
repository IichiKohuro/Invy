using Avalonia;
using Invy.Client.UI;
using Invy.Desktop.Hosting;
using Velopack;

namespace Invy.Desktop;

internal static class Program
{
    private const string AppId = "Invy.Desktop";

    [STAThread]
    public static int Main(string[] args)
    {
        // Первым делом: Velopack обрабатывает установку/удаление/обновление и в этих случаях сам завершает процесс.
        VelopackApp.Build().Run();

        using var instance = SingleInstance.TryAcquire(AppId);
        if (instance is null)
        {
            // Уже запущено: передаём аргументы работающему экземпляру (по умолчанию он покажет окно).
            SingleInstance.SendAsync(AppId, args, TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            return 0;
        }

        using var host = new DesktopHost(instance, DesktopArguments.Parse(args), ServerUrl());
        return Configure(AppBuilder.Configure(() => new App(host))).StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Используется дизайнером Avalonia.</summary>
    public static AppBuilder BuildAvaloniaApp() => Configure(AppBuilder.Configure<App>());

    private static AppBuilder Configure(AppBuilder builder) =>
        builder
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    // Фаза 0: адрес сервера — из переменной окружения. С фазы 2 — из настроек подключения и автообнаружения.
    private static Uri ServerUrl() =>
        Uri.TryCreate(Environment.GetEnvironmentVariable("INVY_SERVER"), UriKind.Absolute, out var url)
            ? url
            : new Uri($"http://localhost:{Invy.Contracts.ApiRoutes.DefaultHttpPort}/");
}

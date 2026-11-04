using Android.Content;
using Android.OS;
using Avalonia.Controls.ApplicationLifetimes;
using Invy.Android.Updates;
using Invy.Client.Core.Connection;
using Invy.Client.Core.Shell;
using Invy.Client.Core.Updates;
using Invy.Client.UI;
using Invy.Client.UI.Views;
using Invy.Contracts.Devices;

namespace Invy.Android;

/// <summary>Android-хост: один главный вид на весь экран и самообновление через APK.</summary>
internal sealed class AndroidHost(Context context) : IAppHost
{
    private readonly AndroidUpdateService _updates = new(context);

    public IAppUpdater Updater => _updates;

    // Адрес сервера вводится на экране подключения; автообнаружение в сети — фаза 5.
    public string? DefaultServerAddress => null;

    public ISettingsStore Settings { get; } = new JsonFileSettingsStore(Path.Combine(context.FilesDir!.AbsolutePath, "settings.json"));

    // Шифрование токена в Android Keystore — фаза 4; до тех пор файл настроек лежит в закрытом каталоге приложения.
    public ITokenProtector TokenProtector { get; } = new PlainTokenProtector();

    public DevicePlatform Platform => DevicePlatform.Android;

    public string DeviceName => string.IsNullOrWhiteSpace(Build.Model) ? "Android" : $"{Build.Manufacturer} {Build.Model}".Trim();

    public string AppVersion => _updates.CurrentVersion;

    public string? PhotoCacheDirectory { get; } = Path.Combine(context.CacheDir!.AbsolutePath, "photos");

    public void OnFrameworkInitialized(App app, ShellViewModel shell)
    {
        if (app.ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new MainView { DataContext = shell };
        }
    }
}

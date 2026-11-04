using Android.Content;
using Avalonia.Controls.ApplicationLifetimes;
using Invy.Client.Core.Shell;
using Invy.Client.Core.Updates;
using Invy.Client.UI;
using Invy.Client.UI.Views;
using Invy.Android.Updates;

namespace Invy.Android;

/// <summary>Android-хост: один главный вид на весь экран и самообновление через APK.</summary>
internal sealed class AndroidHost(Context context) : IAppHost
{
    public IAppUpdater Updater { get; } = new AndroidUpdateService(context);

    // Адрес сервера в фазе 0 вводится в настройках; автообнаружение — фаза 5.
    public string? DefaultServerAddress => null;

    public void OnFrameworkInitialized(App app, ShellViewModel shell)
    {
        if (app.ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new MainView { DataContext = shell };
        }
    }
}

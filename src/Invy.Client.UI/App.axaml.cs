using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Invy.Client.Core.Shell;
using Invy.Client.UI.Services;
using Invy.Client.UI.Views;

namespace Invy.Client.UI;

public partial class App : Application
{
    private readonly IAppHost? _host;

    /// <summary>Для дизайнера Avalonia и тестов.</summary>
    public App()
    {
    }

    public App(IAppHost host) => _host = host;

    /// <summary>Оболочка приложения; доступна хостам (трей, горячие клавиши) для навигации.</summary>
    public ShellViewModel? Shell { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Shell = new ShellViewModel(new ThemeService(this), _host?.Updater, _host?.DefaultServerAddress);

        if (_host is not null)
        {
            _host.OnFrameworkInitialized(this, Shell);
        }
        else
        {
            switch (ApplicationLifetime)
            {
                case IClassicDesktopStyleApplicationLifetime desktop:
                    desktop.MainWindow = new MainWindow { DataContext = Shell };
                    break;
                case ISingleViewApplicationLifetime singleView:
                    singleView.MainView = new MainView { DataContext = Shell };
                    break;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}

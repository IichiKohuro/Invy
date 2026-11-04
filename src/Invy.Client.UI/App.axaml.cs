using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Invy.Client.Core.Shell;
using Invy.Client.UI.Services;
using Invy.Client.UI.Views;

namespace Invy.Client.UI;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Shell = new ShellViewModel(new ThemeService(this));

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow { DataContext = Shell };
                break;
            case ISingleViewApplicationLifetime singleView:
                singleView.MainView = new MainView { DataContext = Shell };
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Оболочка приложения; доступна хостам (трей, горячие клавиши) для навигации.</summary>
    public ShellViewModel? Shell { get; private set; }
}

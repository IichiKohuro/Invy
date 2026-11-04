using Avalonia;
using Avalonia.Styling;
using Invy.Client.Core.Shell;

namespace Invy.Client.UI.Services;

/// <summary>Переключает тему Avalonia без перезапуска. «Как в системе» — FluentAvalonia следит за темой ОС.</summary>
public sealed class ThemeService(Application application) : IThemeService
{
    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    public void Apply(ThemeMode mode)
    {
        Mode = mode;
        application.RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}

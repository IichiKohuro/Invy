using Avalonia;
using Avalonia.Styling;
using Invy.Client.Core.Connection;
using Invy.Client.Core.Shell;

namespace Invy.Client.UI.Services;

/// <summary>
/// Переключает тему Avalonia без перезапуска. «Как в системе» — FluentAvalonia следит за темой ОС.
/// С хранилищем настроек выбранная тема сохраняется и применяется при следующем запуске.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private readonly Application _application;
    private readonly ISettingsStore? _settings;

    public ThemeService(Application application, ISettingsStore? settings = null)
    {
        _application = application;
        _settings = settings;
        if (settings is not null)
        {
            SetVariant(settings.Current.Theme);
        }
    }

    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    public void Apply(ThemeMode mode)
    {
        SetVariant(mode);
        if (_settings is not null && _settings.Current.Theme != mode)
        {
            _settings.Save(_settings.Current with { Theme = mode });
        }
    }

    private void SetVariant(ThemeMode mode)
    {
        Mode = mode;
        _application.RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}

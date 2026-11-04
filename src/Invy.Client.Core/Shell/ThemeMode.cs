namespace Invy.Client.Core.Shell;

/// <summary>Тема оформления. Значения совпадают с порядком вариантов в настройках.</summary>
public enum ThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>Переключение темы реализует UI-слой (Avalonia).</summary>
public interface IThemeService
{
    ThemeMode Mode { get; }

    void Apply(ThemeMode mode);
}

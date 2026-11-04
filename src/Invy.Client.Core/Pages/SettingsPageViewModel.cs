using CommunityToolkit.Mvvm.ComponentModel;
using Invy.Client.Core.Shell;

namespace Invy.Client.Core.Pages;

public sealed partial class SettingsPageViewModel : PageViewModel
{
    private readonly IThemeService _themeService;

    public SettingsPageViewModel(IThemeService themeService)
    {
        _themeService = themeService;
        ThemeIndex = (int)themeService.Mode;
    }

    public override string Title => "Настройки";

    /// <summary>Порядок совпадает с <see cref="ThemeMode"/>.</summary>
    public IReadOnlyList<string> ThemeOptions { get; } = ["Как в системе", "Светлая", "Тёмная"];

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    partial void OnThemeIndexChanged(int value)
    {
        if (Enum.IsDefined((ThemeMode)value))
        {
            _themeService.Apply((ThemeMode)value);
        }
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using Invy.Client.Core.Pages;

namespace Invy.Client.Core.Shell;

/// <summary>Оболочка приложения: боковая навигация и текущая страница.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly Dictionary<string, PageViewModel> _pages = [];

    public ShellViewModel(IThemeService themeService)
    {
        ArgumentNullException.ThrowIfNull(themeService);

        _pages["settings"] = new SettingsPageViewModel(themeService);
        SelectedItem = MenuItems[0];
    }

    public IReadOnlyList<NavigationItem> MenuItems { get; } =
    [
        new("home", "Главная", "Home"),
        new("places", "Места", "MapPin"),
        new("search", "Поиск", "Find"),
        new("unsorted", "Неразобранное", "Import"),
        new("tags", "Теги", "Tag"),
        new("types", "Типы", "Library"),
        new("saved", "Сохранённые", "Bookmarks"),
        new("trash", "Корзина", "Delete"),
        new("journal", "Журнал", "Clock"),
    ];

    public IReadOnlyList<NavigationItem> FooterItems { get; } =
    [
        new("settings", "Настройки", "Settings"),
    ];

    [ObservableProperty]
    public partial NavigationItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial PageViewModel? CurrentPage { get; private set; }

    /// <summary>Переход по ключу пункта навигации (горячие клавиши, трей, ссылки).</summary>
    public void Navigate(string key) =>
        SelectedItem = MenuItems.Concat(FooterItems).FirstOrDefault(i => i.Key == key) ?? SelectedItem;

    partial void OnSelectedItemChanged(NavigationItem? value) =>
        CurrentPage = value is null ? null : GetOrCreatePage(value);

    private PageViewModel GetOrCreatePage(NavigationItem item)
    {
        if (!_pages.TryGetValue(item.Key, out var page))
        {
            page = item.Key switch
            {
                "home" => new HomePageViewModel(),
                "places" => new PlacesPageViewModel(),
                _ => new PlaceholderPageViewModel(item.Title, "Раздел появится в следующих фазах — см. PLAN.md."),
            };
            _pages[item.Key] = page;
        }

        return page;
    }
}

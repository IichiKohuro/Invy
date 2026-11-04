using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Invy.Client.Core.Catalog;
using Invy.Client.Core.Connection;
using Invy.Client.Core.Explorer;
using Invy.Client.Core.Pages;
using Invy.Client.Core.Session;
using Invy.Client.Core.Updates;

namespace Invy.Client.Core.Shell;

/// <summary>Уведомление внизу окна: «Перемещено», «Нет связи с сервером».</summary>
public sealed record Notice(string Message, NotifyKind Kind);

/// <summary>Оболочка приложения: боковая навигация, текущая страница и уведомления.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly Dictionary<string, PageViewModel> _pages = [];
    private readonly IThemeService _themeService;
    private readonly IAppUpdater? _updater;
    private readonly string? _serverAddress;

    public ShellViewModel(IThemeService themeService, IAppUpdater? updater = null, string? serverAddress = null, ClientSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(themeService);
        _themeService = themeService;
        _updater = updater;
        _serverAddress = serverAddress;
        Session = session;
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

    /// <summary>Сеанс работы с сервером; <c>null</c> — ещё не подключились.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected), nameof(ShowConnect))]
    public partial ClientSession? Session { get; private set; }

    /// <summary>Экран подключения (адрес, код сопряжения); задаёт хост приложения.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConnect))]
    public partial ConnectViewModel? Connect { get; set; }

    public bool IsConnected => Session is not null;

    /// <summary>Показывать экран подключения вместо разделов.</summary>
    public bool ShowConnect => Session is null && Connect is not null;

    /// <summary>Отключиться и выбрать другой сервер; хост закрывает сеанс.</summary>
    public event Action? DisconnectRequested;

    [ObservableProperty]
    public partial NavigationItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial PageViewModel? CurrentPage { get; private set; }

    [ObservableProperty]
    public partial Notice? Notice { get; set; }

    /// <summary>Переход по ключу пункта навигации (горячие клавиши, трей, ссылки).</summary>
    public void Navigate(string key) =>
        SelectedItem = MenuItems.Concat(FooterItems).FirstOrDefault(i => i.Key == key) ?? SelectedItem;

    /// <summary>Подключились (или переподключились к другому серверу): страницы с данными создаются заново.</summary>
    public void AttachSession(ClientSession? session)
    {
        foreach (var page in _pages.Values.OfType<IDisposable>())
        {
            page.Dispose();
        }

        _pages.Clear();
        Session = session;
        CurrentPage = SelectedItem is null ? null : GetOrCreatePage(SelectedItem);
    }

    public void ShowNotice(string message, NotifyKind kind = NotifyKind.Info) => Notice = new Notice(message, kind);

    [RelayCommand]
    private void Disconnect() => DisconnectRequested?.Invoke();

    partial void OnSelectedItemChanged(NavigationItem? value) =>
        CurrentPage = value is null ? null : GetOrCreatePage(value);

    partial void OnCurrentPageChanged(PageViewModel? value)
    {
        if (value is not null)
        {
            _ = ActivateAsync(value);
        }
    }

    private async Task ActivateAsync(PageViewModel page)
    {
        try
        {
            await page.ActivateAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or Api.ApiException)
        {
            ShowNotice("Нет связи с сервером Invy", NotifyKind.Error);
        }
    }

    private PageViewModel GetOrCreatePage(NavigationItem item)
    {
        if (!_pages.TryGetValue(item.Key, out var page))
        {
            page = item.Key switch
            {
                "home" => new HomePageViewModel(),
                "settings" => new SettingsPageViewModel(_themeService, _updater, Session?.Connection.ServerUrl?.ToString() ?? _serverAddress, this),
                "places" when Session is not null => new ExplorerViewModel(Session, ExplorerMode.Places),
                "unsorted" when Session is not null => new ExplorerViewModel(Session, ExplorerMode.Unsorted),
                "types" when Session is not null => new TypesViewModel(Session),
                "tags" when Session is not null => new TagsViewModel(Session),
                "places" or "unsorted" or "types" or "tags" => new PlaceholderPageViewModel(item.Title, "Нет подключения к серверу Invy."),
                _ => new PlaceholderPageViewModel(item.Title, "Раздел появится в следующих фазах — см. PLAN.md."),
            };
            _pages[item.Key] = page;
        }

        return page;
    }
}

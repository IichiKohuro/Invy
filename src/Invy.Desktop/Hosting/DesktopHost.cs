using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Invy.Client.Core.Connection;
using Invy.Client.Core.Editor;
using Invy.Client.Core.Session;
using Invy.Client.Core.Shell;
using Invy.Client.Core.Updates;
using Invy.Client.UI;
using Invy.Client.UI.Views;
using Invy.Contracts.Devices;
using Invy.Desktop.Hotkeys;
using Invy.Desktop.Updates;
using Invy.Desktop.Views;

namespace Invy.Desktop.Hosting;

/// <summary>
/// Desktop-хост: главное окно сворачивается в трей, быстрый поиск по глобальной горячей клавише
/// и по клику на иконку, повторный запуск приложения активирует уже работающий экземпляр.
/// </summary>
internal sealed class DesktopHost(SingleInstance instance, DesktopArguments startup, Uri serverUrl) : IAppHost, IDisposable
{
    private readonly DesktopUpdateService _updates = new(serverUrl);
    private NativeMenuItem? _updateItem;

    public IAppUpdater Updater => _updates;

    public string DefaultServerAddress => serverUrl.ToString();

    public ISettingsStore Settings { get; } = new JsonFileSettingsStore(Path.Combine(AppDataDirectory(Environment.SpecialFolder.ApplicationData), "settings.json"));

    public ITokenProtector TokenProtector { get; } = DpapiTokenProtector.ForCurrentPlatform();

    public DevicePlatform Platform => OperatingSystem.IsWindows() ? DevicePlatform.Windows : DevicePlatform.Other;

    public string DeviceName => Environment.MachineName;

    public string AppVersion => _updates.CurrentVersion;

    public string PhotoCacheDirectory { get; } = Path.Combine(AppDataDirectory(Environment.SpecialFolder.LocalApplicationData), "cache", "photos");

    public const string QuickSearchHotKey = "Ctrl+Alt+I";
    public const string QuickAddHotKey = "Ctrl+Alt+N";

    private const int QuickSearchHotKeyId = 1;
    private const int QuickAddHotKeyId = 2;

    private IClassicDesktopStyleApplicationLifetime? _lifetime;
    private App? _app;
    private ShellViewModel? _shell;
    private NativeMenuItem? _serverItem;
    private MainWindow? _mainWindow;
    private QuickSearchWindow? _quickSearch;
    private TrayIcon? _trayIcon;
    private GlobalHotKeyService? _hotKeys;
    private bool _exiting;
    private bool _disposed;

    private static WindowIcon AppIcon => new(AssetLoader.Open(new Uri("avares://Invy.Client.UI/Assets/invy-256.png")));

    public void OnFrameworkInitialized(App app, ShellViewModel shell)
    {
        if (app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime)
        {
            return;
        }

        _lifetime = lifetime;
        _app = app;
        _shell = shell;
        if (app.Connector is { } connector)
        {
            connector.SessionChanged += session => Dispatcher.UIThread.Post(() => UpdateServerItem(session));
        }

        // Закрытие окна не завершает приложение: оно остаётся в трее до «Выход».
        lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        lifetime.Exit += (_, _) =>
        {
            _updates.ApplyOnExit();   // скачанное, но не установленное обновление ставится после выхода
            Dispose();
        };

        _trayIcon = CreateTrayIcon();
        app.SetValue(TrayIcon.IconsProperty, new TrayIcons { _trayIcon });
        RegisterHotKeys();

        instance.ArgumentsReceived += args => Dispatcher.UIThread.Post(() => Handle(DesktopArguments.Parse(args), fromSecondInstance: true));
        instance.StartListening();

        Handle(startup, fromSecondInstance: false);

        if (_updates.IsSupported)
        {
            DispatcherTimer.RunOnce(() => _ = CheckForUpdatesAsync(), TimeSpan.FromSeconds(30));
        }
    }

    public void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow { DataContext = _shell };
            ((Window)_mainWindow).Icon = AppIcon;
            _mainWindow.Closing += OnMainWindowClosing;
            _lifetime!.MainWindow = _mainWindow;
        }

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    public void ShowQuickSearch()
    {
        if (_quickSearch is null)
        {
            _quickSearch = new QuickSearchWindow { Icon = AppIcon };
            _quickSearch.Submitted += (_, _) => OpenSearch();
        }

        _quickSearch.ShowAndFocus();
    }

    /// <summary>Быстро добавить вещь (Ctrl+Alt+N, трей): редактор в последнее место, даже если главное окно скрыто.</summary>
    public async Task QuickAddAsync()
    {
        if (_app?.Connector?.Session is not { CanEdit: true } session)
        {
            ShowMainWindow();
            return;
        }

        MoveTarget? place = null;
        if (session.Settings.Current.RecentPlaceIds is [var recent, ..])
        {
            await session.TryAsync(async () =>
            {
                var item = await session.Api.GetItemAsync(recent);
                if (!item.IsDeleted)
                {
                    place = new MoveTarget(item.Id, string.Join(" › ", item.Path.Select(p => p.Name).Append(item.Name)));
                }
            });
        }

        await session.Dialogs.EditItemAsync(ItemEditorViewModel.ForNew(session, place));
    }

    public void Exit()
    {
        _exiting = true;
        _lifetime?.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            _hotKeys?.Dispose();
        }

        _trayIcon?.Dispose();
    }

    private void Handle(DesktopArguments args, bool fromSecondInstance)
    {
        if (args.QuickSearch)
        {
            ShowQuickSearch();
        }
        else if (args.Search is not null)
        {
            OpenSearch();
        }
        else if (!args.Background || fromSecondInstance)
        {
            ShowMainWindow();
        }
    }

    // Передача текста запроса появится вместе со страницей поиска (фаза 3).
    private void OpenSearch()
    {
        ShowMainWindow();
        _shell?.Navigate("search");
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_exiting || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
        {
            return;
        }

        e.Cancel = true;
        _mainWindow?.Hide();
    }

    private TrayIcon CreateTrayIcon()
    {
        var menu = new NativeMenu();
        menu.Items.Add(new NativeMenuItem($"Найти…  ({QuickSearchHotKey})") { Command = new RelayCommand(ShowQuickSearch) });
        menu.Items.Add(new NativeMenuItem($"Добавить вещь…  ({QuickAddHotKey})") { Command = new AsyncRelayCommand(QuickAddAsync) });
        menu.Items.Add(new NativeMenuItem("Открыть Invy") { Command = new RelayCommand(ShowMainWindow) });
        menu.Items.Add(new NativeMenuItemSeparator());
        _serverItem = new NativeMenuItem("Сервер: нет подключения") { IsEnabled = false };
        menu.Items.Add(_serverItem);
        _updateItem = new NativeMenuItem("Проверить обновления") { Command = new AsyncRelayCommand(CheckForUpdatesAsync) };
        menu.Items.Add(_updateItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(new NativeMenuItem("Выход") { Command = new RelayCommand(Exit) });

        var tray = new TrayIcon { Icon = AppIcon, ToolTipText = $"Invy {_updates.CurrentVersion}", Menu = menu };
        tray.Clicked += (_, _) => ShowQuickSearch();
        return tray;
    }

    /// <summary>
    /// Первое нажатие — проверить и скачать; если обновление уже скачано — установить и перезапустить.
    /// Ошибки сети не мешают работе: пункт меню показывает статус и позволяет повторить.
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
        var item = _updateItem!;
        if (!_updates.IsSupported)
        {
            item.Header = "Обновления — только в установленной версии";
            item.IsEnabled = false;
            return;
        }

        if (_updates.PendingVersion is not null)
        {
            _updates.Install();
            return;
        }

        item.Header = "Проверка обновлений…";
        item.IsEnabled = false;
        try
        {
            var version = await _updates.CheckAndDownloadAsync(_app?.Connector?.Connection.ServerUrl ?? serverUrl);
            item.Header = version is null ? "Обновлений нет — проверить снова" : $"Установить {version} и перезапустить";
        }
#pragma warning disable CA1031 // любая ошибка проверки обновлений не должна ронять приложение
        catch (Exception)
#pragma warning restore CA1031
        {
            item.Header = "Сервер обновлений недоступен — повторить";
        }
        finally
        {
            item.IsEnabled = true;
        }
    }

    private void RegisterHotKeys()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            return;
        }

        var hotKeys = new Dictionary<int, HotKeyGesture>();
        if (HotKeyGesture.TryParse(QuickSearchHotKey, out var search))
        {
            hotKeys[QuickSearchHotKeyId] = search;
        }

        if (HotKeyGesture.TryParse(QuickAddHotKey, out var add))
        {
            hotKeys[QuickAddHotKeyId] = add;
        }

        var service = new GlobalHotKeyService(hotKeys);
        service.Pressed += (_, id) => Dispatcher.UIThread.Post(() =>
        {
            if (id == QuickSearchHotKeyId)
            {
                ShowQuickSearch();
            }
            else if (id == QuickAddHotKeyId)
            {
                _ = QuickAddAsync();
            }
        });
        _hotKeys = service;
    }

    private void UpdateServerItem(ClientSession? session)
    {
        if (_serverItem is not null)
        {
            _serverItem.Header = session?.Connection.Server is { } server ? $"Сервер: «{server.Name}» — подключено" : "Сервер: нет подключения";
        }
    }

    private static string AppDataDirectory(Environment.SpecialFolder folder) => Path.Combine(Environment.GetFolderPath(folder), "Invy");
}

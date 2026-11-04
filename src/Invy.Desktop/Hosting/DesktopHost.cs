using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Invy.Client.Core.Shell;
using Invy.Client.UI;
using Invy.Client.UI.Views;
using Invy.Desktop.Hotkeys;
using Invy.Desktop.Views;

namespace Invy.Desktop.Hosting;

/// <summary>
/// Desktop-хост: главное окно сворачивается в трей, быстрый поиск по глобальной горячей клавише
/// и по клику на иконку, повторный запуск приложения активирует уже работающий экземпляр.
/// </summary>
internal sealed class DesktopHost(SingleInstance instance, DesktopArguments startup) : IAppHost, IDisposable
{
    public const string QuickSearchHotKey = "Ctrl+Alt+I";
    public const string QuickAddHotKey = "Ctrl+Alt+N";

    private const int QuickSearchHotKeyId = 1;
    private const int QuickAddHotKeyId = 2;

    private IClassicDesktopStyleApplicationLifetime? _lifetime;
    private ShellViewModel? _shell;
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
        _shell = shell;

        // Закрытие окна не завершает приложение: оно остаётся в трее до «Выход».
        lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        lifetime.Exit += (_, _) => Dispose();

        _trayIcon = CreateTrayIcon();
        app.SetValue(TrayIcon.IconsProperty, new TrayIcons { _trayIcon });
        RegisterHotKeys();

        instance.ArgumentsReceived += args => Dispatcher.UIThread.Post(() => Handle(DesktopArguments.Parse(args), fromSecondInstance: true));
        instance.StartListening();

        Handle(startup, fromSecondInstance: false);
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
        menu.Items.Add(new NativeMenuItem($"Добавить вещь…  ({QuickAddHotKey})") { Command = new RelayCommand(ShowMainWindow) });
        menu.Items.Add(new NativeMenuItem("Открыть Invy") { Command = new RelayCommand(ShowMainWindow) });
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(new NativeMenuItem("Сервер: нет подключения") { IsEnabled = false });
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(new NativeMenuItem("Выход") { Command = new RelayCommand(Exit) });

        var tray = new TrayIcon { Icon = AppIcon, ToolTipText = "Invy", Menu = menu };
        tray.Clicked += (_, _) => ShowQuickSearch();
        return tray;
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
                ShowMainWindow();
            }
        });
        _hotKeys = service;
    }
}

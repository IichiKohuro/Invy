using Avalonia.Threading;
using Invy.Client.Core.Api;
using Invy.Client.Core.Connection;
using Invy.Client.Core.Session;
using Invy.Client.Core.Shell;
using Invy.Client.UI.Controls;

namespace Invy.Client.UI.Services;

/// <summary>
/// Подключение приложения к серверу: экран подключения → сеанс (справочники, изменения в реальном времени) → разделы.
/// «Сменить сервер» в настройках закрывает сеанс и возвращает экран подключения.
/// </summary>
public sealed class AppConnector : IAsyncDisposable
{
    private readonly IAppHost _host;
    private readonly ShellViewModel _shell;
    private readonly DialogService _dialogs;
    private RealtimeState _realtimeState = RealtimeState.Connected;

    public AppConnector(IAppHost host, ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(shell);
        _host = host;
        _shell = shell;
        _dialogs = new DialogService(shell);
        Connection = new ServerConnection(host.Settings, host.TokenProtector, null, host.DeviceName, host.Platform, host.AppVersion);
        shell.Connect = new ConnectViewModel(Connection, host.Settings.Current.ServerUrl ?? host.DefaultServerAddress, StartSessionAsync);
        shell.DisconnectRequested += () => _ = DisconnectAsync();
    }

    public ServerConnection Connection { get; }

    public ClientSession? Session { get; private set; }

    /// <summary>Сеанс открыт или закрыт (трей показывает состояние).</summary>
    public event Action<ClientSession?>? SessionChanged;

    /// <summary>При запуске: если уже подключались к серверу — подключиться сразу.</summary>
    public Task AutoConnectAsync() =>
        _host.Settings.Current.ServerUrl is null || _shell.Connect is not { } connect ? Task.CompletedTask : connect.ConnectCommand.ExecuteAsync(null);

    public async ValueTask DisposeAsync()
    {
        if (Session is { } session)
        {
            Session = null;
            await session.DisposeAsync();
        }
    }

    private async Task StartSessionAsync(ServerConnection connection)
    {
        if (Session is { } previous)
        {
            await previous.DisposeAsync();
        }

        var cache = _host.PhotoCacheDirectory is { } root && connection.Server is { } server
            ? Path.Combine(root, server.ServerId.ToString("N"))
            : null;
        var session = new ClientSession(connection, _host.Settings, _dialogs, AvaloniaUiDispatcher.Instance, photoCacheDirectory: cache);
        session.Realtime.PairingRequested += request => _ = PairingPrompt.AskAsync(session, request);
        session.Realtime.StateChanged += OnRealtimeStateChanged;
        await session.StartAsync();

        ItemIcon.PhotoLoader = id => session.Photos.GetAsync(id, PhotoSize.Thumb);
        Session = session;
        _shell.AttachSession(session);
        SessionChanged?.Invoke(session);
    }

    private async Task DisconnectAsync()
    {
        if (Session is not { } session)
        {
            return;
        }

        Session = null;
        _shell.AttachSession(null);
        SessionChanged?.Invoke(null);
        await session.DisposeAsync();

        // Выбрали «Сменить сервер» — в следующий запуск не подключаться автоматически.
        _host.Settings.Save(_host.Settings.Current with { ServerUrl = null });
    }

    private void OnRealtimeStateChanged(RealtimeState state)
    {
        var previous = _realtimeState;
        _realtimeState = state;
        Dispatcher.UIThread.Post(() =>
        {
            if (state == RealtimeState.Reconnecting && previous != RealtimeState.Reconnecting)
            {
                _dialogs.Notify("Нет связи с сервером Invy — переподключаемся…", NotifyKind.Warning);
            }
            else if (state == RealtimeState.Connected && previous == RealtimeState.Reconnecting)
            {
                _dialogs.Notify("Связь с сервером восстановлена", NotifyKind.Success);
            }
        });
    }
}

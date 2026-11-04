using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Invy.Client.Core.Api;

namespace Invy.Client.Core.Connection;

/// <summary>
/// Экран подключения: адрес сервера → «Подключиться». Если сервер просит подтверждения, показывается код —
/// его же администратор видит у себя; после одобрения клиент подключается сам.
/// </summary>
public sealed partial class ConnectViewModel : ObservableObject
{
    private readonly Func<ServerConnection, Task> _onConnected;
    private string? _localError;

    /// <param name="connection">Подключение к серверу.</param>
    /// <param name="address">Адрес по умолчанию (сохранённый или из настроек хоста).</param>
    /// <param name="onConnected">Создать сеанс и открыть приложение; ошибки сети показываются здесь же.</param>
    public ConnectViewModel(ServerConnection connection, string? address, Func<ServerConnection, Task> onConnected)
    {
        ArgumentNullException.ThrowIfNull(connection);
        Connection = connection;
        _onConnected = onConnected;
        Address = address ?? string.Empty;
        connection.PropertyChanged += OnConnectionChanged;
    }

    public ServerConnection Connection { get; }

    [ObservableProperty]
    public partial string Address { get; set; }

    /// <summary>Подключились к серверу и загружаем справочники.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(StatusText))]
    public partial bool IsStarting { get; private set; }

    public string? PairingCode => Connection.PairingCode;

    public bool IsWaitingForApproval => Connection.Status == ConnectionStatus.WaitingForApproval;

    public bool IsBusy => IsStarting || Connection.Status is ConnectionStatus.Connecting or ConnectionStatus.WaitingForApproval;

    public string? Error => _localError ?? (Connection.Status == ConnectionStatus.Failed ? Connection.Error : null);

    public string StatusText => IsStarting ? "Загрузка справочников…" : Connection.Status switch
    {
        ConnectionStatus.Connecting => "Подключение…",
        ConnectionStatus.WaitingForApproval => "Подтвердите подключение на компьютере с сервером Invy или в приложении администратора. Код должен совпасть:",
        ConnectionStatus.Connected => $"Подключено к «{Connection.Server?.Name}»",
        _ => "Укажите адрес компьютера, где запущен сервер Invy, — в домашней сети.",
    };

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        SetLocalError(null);
        if (ServerConnection.ParseServerAddress(Address) is not { } url)
        {
            SetLocalError("Укажите адрес сервера, например 192.168.1.10 или home-pc:5470");
            return;
        }

        if (!await Connection.ConnectAsync(url, cancellationToken))
        {
            return;
        }

        IsStarting = true;
        try
        {
            await _onConnected(Connection);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            SetLocalError(ex is ApiException api ? api.UserMessage : "Сервер перестал отвечать — попробуйте ещё раз");
        }
        finally
        {
            IsStarting = false;
        }
    }

    private void SetLocalError(string? error)
    {
        _localError = error;
        OnPropertyChanged(nameof(Error));
    }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(PairingCode));
        OnPropertyChanged(nameof(IsWaitingForApproval));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(StatusText));
    }
}

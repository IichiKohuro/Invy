using Invy.Client.Core.Api;
using Invy.Contracts;
using Invy.Contracts.Devices;
using Invy.Contracts.Sync;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Invy.Client.Core.Connection;

public enum RealtimeState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
}

/// <summary>
/// Изменения в реальном времени (SignalR <c>/hubs/inventory</c>). Номер последнего изменения (seq) запоминается:
/// при разрыве связи или пропуске события недостающее догружается через <c>/sync/changes</c>,
/// поэтому подписчики получают все изменения по порядку и без повторов.
/// </summary>
public sealed class RealtimeService(IUiDispatcher dispatcher) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HubConnection? _hub;
    private InvyApiClient? _api;

    public event Action<ChangeBatch>? ChangesReceived;

    public event Action<PendingPairingDto>? PairingRequested;

    public event Action<RealtimeState>? StateChanged;

    public long LastSeq { get; internal set; }

    public RealtimeState State { get; private set; } = RealtimeState.Disconnected;

    /// <summary>Только Long Polling — для тестов на TestServer.</summary>
    public bool LongPollingOnly { get; init; }

    public async Task StartAsync(ServerConnection connection, CancellationToken cancellationToken = default)
    {
        await StopAsync();
        _api = connection.Api ?? throw new InvalidOperationException("Нет подключения к серверу");
        SetState(RealtimeState.Connecting);

        // Точка отсчёта — текущий последний номер изменения на сервере.
        LastSeq = (await _api.GetChangesAsync(long.MaxValue, 1, cancellationToken)).LastSeq;

        var token = connection.Token;
        var handlerFactory = connection.HandlerFactory;
        _hub = new HubConnectionBuilder()
            .WithUrl(new Uri(connection.ServerUrl!, HubRoutes.Inventory), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(token);
                if (handlerFactory is not null)
                {
                    options.HttpMessageHandlerFactory = _ => handlerFactory();
                }

                if (LongPollingOnly)
                {
                    options.Transports = HttpTransportType.LongPolling;
                }
            })
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .AddJsonProtocol(o => o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, InvyJsonContext.Default))
            .Build();

        _hub.On<ChangeBatch>(nameof(IInventoryClient.Changed), OnChangedAsync);
        _hub.On<PendingPairingDto>(nameof(IInventoryClient.PairingRequested), request => dispatcher.Post(() => PairingRequested?.Invoke(request)));
        _hub.Reconnecting += _ =>
        {
            SetState(RealtimeState.Reconnecting);
            return Task.CompletedTask;
        };
        _hub.Reconnected += async _ =>
        {
            await CatchUpAsync(CancellationToken.None);
            SetState(RealtimeState.Connected);
        };
        _hub.Closed += _ =>
        {
            SetState(RealtimeState.Disconnected);
            return Task.CompletedTask;
        };

        await _hub.StartAsync(cancellationToken);
        await CatchUpAsync(cancellationToken);   // то, что изменилось между запросом seq и подключением
        SetState(RealtimeState.Connected);
    }

    public async Task StopAsync()
    {
        if (_hub is not null)
        {
            await _hub.DisposeAsync();
            _hub = null;
            SetState(RealtimeState.Disconnected);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _gate.Dispose();
    }

    private async Task OnChangedAsync(ChangeBatch batch)
    {
        await _gate.WaitAsync();
        try
        {
            if (batch.LastSeq <= LastSeq)
            {
                return;   // уже получено через догрузку
            }

            if (batch.FirstSeq > LastSeq + 1)
            {
                await CatchUpLockedAsync(CancellationToken.None);   // пропуск: догружаем всё по порядку
                return;
            }

            LastSeq = batch.LastSeq;
            Publish(batch);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task CatchUpAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await CatchUpLockedAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CatchUpLockedAsync(CancellationToken cancellationToken)
    {
        while (_api is not null)
        {
            var response = await _api.GetChangesAsync(LastSeq, 500, cancellationToken);
            if (response.Changes.Count == 0)
            {
                return;
            }

            LastSeq = response.Changes[^1].Seq;
            Publish(new ChangeBatch(response.Changes[0].Seq, LastSeq, null, null, response.Changes));
            if (!response.HasMore)
            {
                return;
            }
        }
    }

    private void Publish(ChangeBatch batch) => dispatcher.Post(() => ChangesReceived?.Invoke(batch));

    private void SetState(RealtimeState state)
    {
        State = state;
        dispatcher.Post(() => StateChanged?.Invoke(state));
    }

    /// <summary>Переподключаемся бесконечно: 0, 2, 5, 10 с, далее каждые 30 с.</summary>
    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        private static readonly TimeSpan[] Delays = [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

        public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
            retryContext.PreviousRetryCount < Delays.Length ? Delays[retryContext.PreviousRetryCount] : TimeSpan.FromSeconds(30);
    }
}

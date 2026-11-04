using Invy.Client.Core.Api;
using Invy.Client.Core.Catalog;
using Invy.Client.Core.Connection;
using Invy.Client.Core.Editor;
using Invy.Client.Core.Photos;
using Invy.Contracts.Catalog;
using Invy.Contracts.Items;
using Invy.Contracts.Sync;

namespace Invy.Client.Core.Session;

public enum DialogChoice
{
    Cancel,
    Primary,
    Secondary,
}

public enum NotifyKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>Выбранное место назначения: <see cref="Id"/> = null — корень (места) или «Неразобранное».</summary>
public sealed record MoveTarget(Guid? Id, string Name);

/// <summary>Файл, выбранный пользователем (диалог, перетаскивание, буфер обмена).</summary>
public sealed record PickedFile(string Name, Func<Task<Stream>> OpenAsync);

/// <summary>Диалоги и уведомления; реализация — Avalonia/FluentAvalonia в Invy.Client.UI.</summary>
public interface IDialogService
{
    Task<DialogChoice> AskAsync(string title, string message, string primary, string? secondary = null, string close = "Отмена");

    /// <summary>Редактор вещи в диалоге; результат — сохранённая вещь или <c>null</c>, если отменено.</summary>
    Task<ItemDetailsDto?> EditItemAsync(ItemEditorViewModel editor);

    /// <summary>Конструктор типа в диалоге; результат — сохранённый тип или <c>null</c>.</summary>
    Task<ItemTypeDto?> EditTypeAsync(TypeEditorViewModel editor);

    Task<MoveTarget?> PickLocationAsync(string title, ClientSession session);

    Task<IReadOnlyList<PickedFile>> PickImagesAsync();

    void Notify(string message, NotifyKind kind = NotifyKind.Info);
}

/// <summary>
/// Сеанс работы с сервером: API, справочники, изменения в реальном времени, диалоги.
/// Страницы подписываются на <see cref="Changed"/>; к этому моменту справочники уже обновлены.
/// </summary>
/// <param name="photoCacheDirectory">Каталог кэша фото на диске; <c>null</c> — только в памяти.</param>
public sealed class ClientSession(
    ServerConnection connection,
    ISettingsStore settings,
    IDialogService dialogs,
    IUiDispatcher dispatcher,
    RealtimeService? realtime = null,
    string? photoCacheDirectory = null)
    : IAsyncDisposable
{
    private readonly Dictionary<Guid, FieldValueDto> _rememberedValues = [];
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>Пауза между попытками поднять канал реального времени.</summary>
    public TimeSpan RealtimeRetryDelay { get; init; } = TimeSpan.FromSeconds(15);

    public event Action<ChangeBatch>? Changed;

    public ServerConnection Connection { get; } = connection;

    public InvyApiClient Api => Connection.Api ?? throw new InvalidOperationException("Нет подключения к серверу");

    public CatalogStore Catalog { get; } = new();

    /// <summary>Превью и фото с сервера (память + диск).</summary>
    public PhotoCache Photos { get; } = new(() => connection.Api ?? throw new InvalidOperationException("Нет подключения к серверу"), photoCacheDirectory);

    public RealtimeService Realtime { get; } = realtime ?? new RealtimeService(dispatcher);

    public IDialogService Dialogs { get; } = dialogs;

    public IUiDispatcher Dispatcher { get; } = dispatcher;

    public ISettingsStore Settings { get; } = settings;

    public bool CanEdit => Connection.CanEdit;

    /// <summary>
    /// Загрузить справочники и подписаться на изменения. Если канал реального времени не поднялся
    /// (например, прокси режет WebSocket), работаем без него и пробуем снова в фоне.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await Catalog.LoadAsync(Api, cancellationToken);
        Realtime.ChangesReceived += OnChanges;
        if (!await TryStartRealtimeAsync(cancellationToken))
        {
            Dialogs.Notify("Изменения с других устройств пока не приходят сами — нажмите «Обновить». Подключение повторится автоматически.", NotifyKind.Warning);
            _ = RetryRealtimeAsync(_stopping.Token);
        }
    }

    /// <summary>Недавние типы — первыми в списке выбора.</summary>
    public void RememberType(Guid typeId) =>
        Settings.Save(Settings.Current with { RecentTypeIds = [typeId, .. Settings.Current.RecentTypeIds.Where(id => id != typeId).Take(7)] });

    public void RememberPlace(Guid placeId) =>
        Settings.Save(Settings.Current with { RecentPlaceIds = [placeId, .. Settings.Current.RecentPlaceIds.Where(id => id != placeId).Take(7)] });

    /// <summary>Последние значения полей с «запоминать последнее» — подставляются в новые вещи.</summary>
    public void RememberValue(FieldValueDto value) => _rememberedValues[value.FieldId] = value;

    public FieldValueDto? RememberedValue(Guid fieldId) => _rememberedValues.GetValueOrDefault(fieldId);

    /// <summary>Выполнить запрос к серверу, показав понятную ошибку вместо исключения.</summary>
    public async Task<bool> TryAsync(Func<Task> action, string? failurePrefix = null)
    {
        try
        {
            await action();
            return true;
        }
        catch (ApiException ex)
        {
            Dialogs.Notify(failurePrefix is null ? ex.UserMessage : $"{failurePrefix}: {ex.UserMessage}", NotifyKind.Error);
        }
        catch (HttpRequestException)
        {
            Dialogs.Notify("Нет связи с сервером Invy. Проверьте, что он запущен и доступен по сети.", NotifyKind.Error);
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        Realtime.ChangesReceived -= OnChanges;
        await Realtime.DisposeAsync();
        _stopping.Dispose();
    }

    private async Task<bool> TryStartRealtimeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Realtime.StartAsync(Connection, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // SignalR бросает разные исключения транспорта — любое значит «пока без реального времени»
        catch (Exception)
#pragma warning restore CA1031
        {
            await Realtime.StopAsync();
            return false;
        }
    }

    private async Task RetryRealtimeAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(RealtimeRetryDelay, cancellationToken);
                if (await TryStartRealtimeAsync(cancellationToken))
                {
                    Dialogs.Notify("Изменения с других устройств снова приходят сами", NotifyKind.Success);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // сеанс закрыт
        }
    }

    private async void OnChanges(ChangeBatch batch)
    {
        try
        {
            await Catalog.HandleAsync(batch);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            // справочники обновятся при следующем изменении или переподключении
        }

        Changed?.Invoke(batch);
    }
}

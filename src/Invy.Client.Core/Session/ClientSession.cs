using Invy.Client.Core.Api;
using Invy.Client.Core.Catalog;
using Invy.Client.Core.Connection;
using Invy.Client.Core.Editor;
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

    Task<MoveTarget?> PickLocationAsync(string title, ClientSession session);

    Task<IReadOnlyList<PickedFile>> PickImagesAsync();

    void Notify(string message, NotifyKind kind = NotifyKind.Info);
}

/// <summary>
/// Сеанс работы с сервером: API, справочники, изменения в реальном времени, диалоги.
/// Страницы подписываются на <see cref="Changed"/>; к этому моменту справочники уже обновлены.
/// </summary>
public sealed class ClientSession(ServerConnection connection, ISettingsStore settings, IDialogService dialogs, IUiDispatcher dispatcher, RealtimeService? realtime = null)
    : IAsyncDisposable
{
    private readonly Dictionary<Guid, FieldValueDto> _rememberedValues = [];

    public event Action<ChangeBatch>? Changed;

    public ServerConnection Connection { get; } = connection;

    public InvyApiClient Api => Connection.Api ?? throw new InvalidOperationException("Нет подключения к серверу");

    public CatalogStore Catalog { get; } = new();

    public RealtimeService Realtime { get; } = realtime ?? new RealtimeService(dispatcher);

    public IDialogService Dialogs { get; } = dialogs;

    public IUiDispatcher Dispatcher { get; } = dispatcher;

    public ISettingsStore Settings { get; } = settings;

    public bool CanEdit => Connection.CanEdit;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await Catalog.LoadAsync(Api, cancellationToken);
        Realtime.ChangesReceived += OnChanges;
        await Realtime.StartAsync(Connection, cancellationToken);
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
        Realtime.ChangesReceived -= OnChanges;
        await Realtime.DisposeAsync();
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

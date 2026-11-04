using System.Text.Json.Serialization;

namespace Invy.Contracts.Sync;

/// <summary>
/// Событие SignalR <c>Changed</c>: что изменилось одной операцией. Клиент сам перечитывает нужное.
/// Если <see cref="FirstSeq"/> больше последнего известного клиенту seq + 1 — клиент пропустил изменения
/// и догружает их через <c>GET /api/v1/sync/changes?since=…</c>.
/// </summary>
public sealed record ChangeBatch(
    long FirstSeq,
    long LastSeq,
    Guid? DeviceId,
    Guid? CorrelationId,
    IReadOnlyList<ChangeDto> Changes);

/// <summary>Одно изменение: номер (seq), сущность и действие.</summary>
public sealed record ChangeDto(
    long Seq,
    ChangeEntityType EntityType,
    Guid EntityId,
    ChangeAction Action,
    Guid? ParentId = null,
    Guid? OldParentId = null);

/// <summary>Ответ <c>GET /api/v1/sync/changes</c>.</summary>
/// <param name="LastSeq">Последний номер изменения на сервере.</param>
/// <param name="HasMore">Есть ещё изменения — запросить снова с since = последнему seq в ответе.</param>
/// <param name="Changes">Изменения по возрастанию seq.</param>
public sealed record SyncChangesResponse(long LastSeq, bool HasMore, IReadOnlyList<ChangeDto> Changes);

[JsonConverter(typeof(JsonStringEnumConverter<ChangeEntityType>))]
public enum ChangeEntityType
{
    Item = 0,
    ItemType = 1,
    ValueList = 2,
    Tag = 3,
    Device = 4,
    System = 5,
}

[JsonConverter(typeof(JsonStringEnumConverter<ChangeAction>))]
public enum ChangeAction
{
    Created = 0,
    Updated = 1,
    Moved = 2,
    Deleted = 3,
    Restored = 4,
    Purged = 5,
}

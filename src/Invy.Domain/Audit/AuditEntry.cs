namespace Invy.Domain.Audit;

/// <summary>
/// Запись истории изменений. <see cref="Id"/> — монотонный номер изменения (seq),
/// по нему клиенты догружают пропущенные изменения.
/// </summary>
public sealed class AuditEntry
{
    public long Id { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public Guid? DeviceId { get; set; }

    public AuditEntityType EntityType { get; set; }

    public Guid EntityId { get; set; }

    public AuditAction Action { get; set; }

    /// <summary>Краткое описание для списка: «Перемещено: Пакет → Серая коробка».</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Что было → что стало, со снимками имён на момент изменения.</summary>
    public List<AuditChange> Changes { get; set; } = [];

    /// <summary>Общий id для записей одной пакетной операции.</summary>
    public Guid? CorrelationId { get; set; }

    /// <summary>Родитель вещи после изменения — клиенты обновляют открытый контейнер.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Родитель вещи до перемещения.</summary>
    public Guid? OldParentId { get; set; }
}

public sealed record AuditChange(string Key, string Label, string? Old, string? New);

public enum AuditEntityType
{
    Item = 0,
    ItemType = 1,
    ValueList = 2,
    Tag = 3,
    Device = 4,
    System = 5,
}

public enum AuditAction
{
    Created = 0,
    Updated = 1,
    Moved = 2,
    Deleted = 3,
    Restored = 4,
    Purged = 5,
}

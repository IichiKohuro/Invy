using System.Text.Json.Serialization;
using Invy.Contracts.Catalog;
using Invy.Contracts.Sync;

namespace Invy.Contracts.Items;

/// <summary>
/// Значение характеристики: заполняется одно свойство по типу данных поля —
/// <see cref="Text"/> (текст, URL), <see cref="Number"/>, <see cref="Date"/>, <see cref="Bool"/>,
/// <see cref="OptionIds"/> (списки), <see cref="ItemId"/> (ссылка на вещь).
/// </summary>
public sealed record FieldValueDto(
    Guid FieldId,
    string? Text = null,
    double? Number = null,
    DateOnly? Date = null,
    bool? Bool = null,
    IReadOnlyList<Guid>? OptionIds = null,
    Guid? ItemId = null,
    string? Display = null);

public sealed record PathSegmentDto(Guid Id, string Name);

public sealed record ItemCountsDto(int Direct, int Total);

/// <summary>Строка списка: содержимое контейнера, результаты.</summary>
public sealed record ItemSummaryDto(
    Guid Id,
    int Number,
    string Name,
    Guid TypeId,
    string TypeName,
    TypeRole Role,
    Guid? ParentId,
    double Quantity,
    string? Unit,
    IconDto? Icon,
    string? Color,
    Guid? CoverMediaId,
    int ChildCount,
    int Version,
    DateTimeOffset UpdatedAt);

/// <summary>Карточка вещи.</summary>
public sealed record ItemDetailsDto(
    Guid Id,
    int Number,
    Guid TypeId,
    string TypeName,
    TypeRole Role,
    Guid? ParentId,
    string Name,
    bool IsNameAuto,
    string? Description,
    double Quantity,
    string? Unit,
    double? MinQuantity,
    IconDto? Icon,
    string? Color,
    Guid? CoverMediaId,
    IReadOnlyList<FieldValueDto> Values,
    IReadOnlyList<Guid> TagIds,
    IReadOnlyList<PathSegmentDto> Path,
    ItemCountsDto Counts,
    bool IsDeleted,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Создание (без <see cref="Version"/>) или полная замена вещи (с <see cref="Version"/> из карточки).
/// Пустое <see cref="Name"/> + шаблон названия у типа = авто-название.
/// </summary>
public sealed record SaveItemRequest(
    Guid TypeId,
    Guid? ParentId = null,
    string? Name = null,
    string? Description = null,
    double Quantity = 1,
    string? Unit = null,
    double? MinQuantity = null,
    IconDto? Icon = null,
    string? Color = null,
    IReadOnlyList<FieldValueDto>? Values = null,
    IReadOnlyList<Guid>? TagIds = null,
    int? Version = null,
    Guid? Id = null);

/// <summary>Переместить вещи; <see cref="TargetId"/> = null — в корень (места) или в «Неразобранное».</summary>
public sealed record MoveItemsRequest(IReadOnlyList<Guid> ItemIds, Guid? TargetId);

public sealed record BatchGetRequest(IReadOnlyList<Guid> Ids);

/// <summary>Что делать с содержимым удаляемого контейнера.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeleteContentsMode>))]
public enum DeleteContentsMode
{
    /// <summary>Удалить вместе с содержимым.</summary>
    Delete = 0,

    /// <summary>Переложить содержимое на уровень выше.</summary>
    MoveUp = 1,
}

public sealed record TreeNodeDto(Guid Id, string Name, TypeRole Role, IconDto? Icon, string? Color, bool HasChildren);

/// <summary>Запись корзины: корень одной операции удаления и сколько вещей удалено вместе с ним.</summary>
public sealed record TrashEntryDto(Guid Id, string Name, Guid TypeId, string TypeName, DateTimeOffset DeletedAt, Guid DeletionId, int ContainedCount);

// ---------- История ----------

public sealed record AuditChangeDto(string Key, string Label, string? Old, string? New);

public sealed record AuditEntryDto(
    long Seq,
    DateTimeOffset Timestamp,
    Guid? DeviceId,
    string? DeviceName,
    ChangeEntityType EntityType,
    Guid EntityId,
    ChangeAction Action,
    string Summary,
    IReadOnlyList<AuditChangeDto> Changes,
    Guid? CorrelationId);

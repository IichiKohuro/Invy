using System.Text.Json.Serialization;

namespace Invy.Contracts.Catalog;

[JsonConverter(typeof(JsonStringEnumConverter<TypeRole>))]
public enum TypeRole
{
    Item = 0,
    Container = 1,
    Place = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter<FieldDataType>))]
public enum FieldDataType
{
    Text = 0,
    LongText = 1,
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Тип данных характеристики")]
    Integer = 2,
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Тип данных характеристики")]
    Decimal = 3,
    Boolean = 4,
    Date = 5,
    Choice = 6,
    MultiChoice = 7,
    ItemRef = 8,
    Url = 9,
}

[JsonConverter(typeof(JsonStringEnumConverter<IconKind>))]
public enum IconKind
{
    Fluent = 0,
    Emoji = 1,
    Custom = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter<ValueListSortMode>))]
public enum ValueListSortMode
{
    Manual = 0,
    Alphabetical = 1,
}

/// <summary>Иконка: символ Fluent («PlugConnected»), эмодзи («🔌») или id своей картинки.</summary>
public sealed record IconDto(IconKind Kind, string Value);

/// <summary>Страница результатов.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total);

// ---------- Типы и характеристики ----------

public sealed record FieldSettingsDto(
    double? Min = null,
    double? Max = null,
    int? Decimals = null,
    int? MaxLength = null,
    string? Placeholder = null,
    string? Pattern = null,
    string? DefaultValue = null);

public sealed record FieldDefinitionDto(
    Guid Id,
    Guid TypeId,
    string Name,
    FieldDataType DataType,
    string? Unit,
    Guid? ValueListId,
    string? GroupName,
    bool IsRequired,
    bool ShowInList,
    bool RememberLast,
    FieldSettingsDto Settings,
    int SortOrder,
    bool IsArchived);

/// <param name="Fields">Собственные характеристики типа.</param>
/// <param name="InheritedFields">Характеристики базовых типов (только чтение).</param>
public sealed record ItemTypeDto(
    Guid Id,
    string Name,
    Guid? BaseTypeId,
    TypeRole Role,
    IconDto? Icon,
    string? Color,
    string? TitleTemplate,
    string? DefaultUnit,
    int SortOrder,
    bool IsArchived,
    IReadOnlyList<FieldDefinitionDto> Fields,
    IReadOnlyList<FieldDefinitionDto> InheritedFields,
    int Version);

/// <summary>Характеристика в запросе сохранения типа. Без <see cref="Id"/> — новая; отсутствующие в запросе — архивируются.</summary>
public sealed record FieldDefinitionInput(
    Guid? Id,
    string Name,
    FieldDataType DataType,
    string? Unit = null,
    Guid? ValueListId = null,
    string? GroupName = null,
    bool IsRequired = false,
    bool ShowInList = false,
    bool RememberLast = false,
    FieldSettingsDto? Settings = null,
    bool IsArchived = false);

/// <summary>Создание (<see cref="Version"/> = null) или полная замена типа вместе с характеристиками.</summary>
public sealed record SaveItemTypeRequest(
    string Name,
    TypeRole Role,
    IReadOnlyList<FieldDefinitionInput> Fields,
    Guid? BaseTypeId = null,
    IconDto? Icon = null,
    string? Color = null,
    string? TitleTemplate = null,
    string? DefaultUnit = null,
    int SortOrder = 0,
    bool IsArchived = false,
    int? Version = null,
    Guid? Id = null);

// ---------- Справочники ----------

public sealed record ValueListOptionDto(Guid Id, string Value, string? Color, int SortOrder, bool IsArchived);

public sealed record ValueListDto(
    Guid Id,
    string Name,
    bool IsShared,
    bool AllowAddFromForm,
    ValueListSortMode SortMode,
    IReadOnlyList<ValueListOptionDto> Options,
    int Version);

public sealed record ValueListOptionInput(Guid? Id, string Value, string? Color = null, bool IsArchived = false);

public sealed record SaveValueListRequest(
    string Name,
    IReadOnlyList<ValueListOptionInput> Options,
    bool IsShared = true,
    bool AllowAddFromForm = true,
    ValueListSortMode SortMode = ValueListSortMode.Manual,
    int? Version = null,
    Guid? Id = null);

/// <summary>Быстрое добавление варианта прямо из формы вещи («+ Добавить "Type-C"»).</summary>
public sealed record AddOptionRequest(string Value, string? Color = null);

// ---------- Теги ----------

public sealed record TagDto(Guid Id, string Name, string? Color, int SortOrder, int ItemCount, int Version);

public sealed record SaveTagRequest(string Name, string? Color = null, int SortOrder = 0, int? Version = null, Guid? Id = null);

/// <summary>Слияние тегов: вещи исходного тега получают целевой, исходный удаляется.</summary>
public sealed record MergeTagRequest(Guid TargetTagId);

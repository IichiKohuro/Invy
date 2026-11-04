using Invy.Domain.Common;
using Invy.Domain.Items;
using Invy.Domain.Text;

namespace Invy.Domain.Types;

/// <summary>Тип вещи («Кабель», «Коробка», «Место») — набор характеристик, иконка и роль.</summary>
public sealed class ItemType : AggregateRoot
{
    public string Name { get; private set; } = string.Empty;

    public string NameNorm { get; private set; } = string.Empty;

    /// <summary>Базовый тип: его характеристики наследуются.</summary>
    public Guid? BaseTypeId { get; set; }

    public TypeRole Role { get; set; }

    public IconKind? IconKind { get; set; }

    public string? IconValue { get; set; }

    public string? Color { get; set; }

    /// <summary>Шаблон авто-названия: «{Разъём 1} → {Разъём 2}, {Длина} см».</summary>
    public string? TitleTemplate { get; set; }

    public string? DefaultUnit { get; set; }

    public int SortOrder { get; set; }

    public bool IsArchived { get; set; }

    public List<FieldDefinition> Fields { get; } = [];

    public void Rename(string name)
    {
        Name = name.Trim();
        NameNorm = TextNormalizer.Normalize(Name);
    }
}

/// <summary>Характеристика типа. Удаление — архивирование: значения у вещей сохраняются.</summary>
public sealed class FieldDefinition
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TypeId { get; set; }

    public string Name { get; set; } = string.Empty;

    public FieldDataType DataType { get; set; }

    public string? Unit { get; set; }

    public Guid? ValueListId { get; set; }

    public string? GroupName { get; set; }

    public bool IsRequired { get; set; }

    public bool ShowInList { get; set; }

    public bool RememberLast { get; set; }

    public FieldSettings Settings { get; set; } = FieldSettings.Empty;

    public int SortOrder { get; set; }

    public bool IsArchived { get; set; }

    public bool UsesValueList => DataType is FieldDataType.Choice or FieldDataType.MultiChoice;
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Названия типов данных характеристик — термины предметной области")]
public enum FieldDataType
{
    Text = 0,
    LongText = 1,
    Integer = 2,
    Decimal = 3,
    Boolean = 4,
    Date = 5,
    Choice = 6,
    MultiChoice = 7,
    ItemRef = 8,
    Url = 9,
}

/// <summary>Необязательные ограничения и подсказки поля (хранятся JSON).</summary>
public sealed record FieldSettings(
    double? Min = null,
    double? Max = null,
    int? Decimals = null,
    int? MaxLength = null,
    string? Placeholder = null,
    string? Pattern = null,
    string? DefaultValue = null)
{
    public static FieldSettings Empty { get; } = new();
}

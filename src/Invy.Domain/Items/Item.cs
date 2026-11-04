using Invy.Domain.Common;
using Invy.Domain.Text;

namespace Invy.Domain.Items;

/// <summary>
/// Вещь, контейнер или место — узел единого дерева. <see cref="ParentId"/> = null: место верхнего уровня
/// или «Неразобранное». Удаление мягкое: вещи одной операции удаления получают общий <see cref="DeletionId"/>.
/// </summary>
public sealed class Item : AggregateRoot
{
    /// <summary>Инвентарный номер (№ 123): для этикеток и поиска; назначается сервером при сохранении.</summary>
    public int Number { get; set; }

    public Guid TypeId { get; set; }

    public Guid? ParentId { get; set; }

    public string Name { get; private set; } = string.Empty;

    public string NameNorm { get; private set; } = string.Empty;

    public bool IsNameAuto { get; set; }

    public string? Description { get; set; }

    public double Quantity { get; set; } = 1;

    public string? Unit { get; set; }

    public double? MinQuantity { get; set; }

    public IconKind? IconKind { get; set; }

    public string? IconValue { get; set; }

    public string? Color { get; set; }

    public Guid? CoverMediaId { get; set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletionId { get; private set; }

    public List<ItemValue> Values { get; } = [];

    public List<ItemTag> Tags { get; } = [];

    public void Rename(string name, bool isAuto = false)
    {
        Name = name.Trim();
        NameNorm = TextNormalizer.Normalize(Name);
        IsNameAuto = isAuto;
    }

    public void MarkDeleted(Guid deletionId, DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
        DeletionId = deletionId;
    }

    public void Restore()
    {
        IsDeleted = false;
        DeletedAt = null;
        DeletionId = null;
    }
}

/// <summary>Значение характеристики (типизированный EAV). Для множественного выбора — несколько строк с разным <see cref="Ordinal"/>.</summary>
public sealed class ItemValue
{
    public long Id { get; set; }

    public Guid ItemId { get; set; }

    public Guid FieldId { get; set; }

    public int Ordinal { get; set; }

    public string? TextValue { get; set; }

    public double? NumberValue { get; set; }

    public DateOnly? DateValue { get; set; }

    public bool? BoolValue { get; set; }

    public Guid? OptionId { get; set; }

    public Guid? RefItemId { get; set; }
}

public sealed class ItemTag
{
    public Guid ItemId { get; set; }

    public Guid TagId { get; set; }
}

using Invy.Domain.Common;
using Invy.Domain.Text;

namespace Invy.Domain.Lists;

/// <summary>Справочник значений для полей-списков («Разъёмы»: Type-A / Type-C / …).</summary>
public sealed class ValueList : AggregateRoot
{
    public string Name { get; private set; } = string.Empty;

    public string NameNorm { get; private set; } = string.Empty;

    /// <summary>Общий справочник (виден в списке справочников) или принадлежит одному полю.</summary>
    public bool IsShared { get; set; } = true;

    /// <summary>Можно ли добавить вариант прямо из формы вещи («+ Добавить "Type-C"»).</summary>
    public bool AllowAddFromForm { get; set; } = true;

    public ValueListSortMode SortMode { get; set; }

    public List<ValueListOption> Options { get; } = [];

    public void Rename(string name)
    {
        Name = name.Trim();
        NameNorm = TextNormalizer.Normalize(Name);
    }
}

public sealed class ValueListOption
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ListId { get; set; }

    public string Value { get; private set; } = string.Empty;

    public string ValueNorm { get; private set; } = string.Empty;

    public string? Color { get; set; }

    public int SortOrder { get; set; }

    public bool IsArchived { get; set; }

    public void SetValue(string value)
    {
        Value = value.Trim();
        ValueNorm = TextNormalizer.Normalize(Value);
    }
}

public enum ValueListSortMode
{
    Manual = 0,
    Alphabetical = 1,
}

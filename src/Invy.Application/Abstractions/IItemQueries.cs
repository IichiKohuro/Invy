using Invy.Domain.Common;
using Invy.Domain.Items;

namespace Invy.Application.Abstractions;

/// <summary>Чтение дерева вещей (Dapper, рекурсивные CTE). Удалённые в корзину вещи не учитываются.</summary>
public interface IItemQueries
{
    /// <summary>Предки вещи от корня к родителю (без самой вещи).</summary>
    Task<IReadOnlyList<PathSegment>> GetPathAsync(Guid itemId, CancellationToken cancellationToken);

    /// <summary>Сколько внутри: непосредственно и на любой глубине.</summary>
    Task<ItemCounts> GetCountsAsync(Guid itemId, CancellationToken cancellationToken);

    /// <summary>Содержимое контейнера; <paramref name="scope"/> задаёт, что показывать в корне.</summary>
    Task<Page<ItemRow>> GetChildrenAsync(Guid? parentId, ChildrenScope scope, ItemSort sort, bool descending, int skip, int take, CancellationToken cancellationToken);

    /// <summary>Узлы дерева мест: только контейнеры и места.</summary>
    Task<IReadOnlyList<TreeNodeRow>> GetTreeAsync(Guid? parentId, CancellationToken cancellationToken);

    /// <summary>Все потомки (без самой вещи); <paramref name="includeDeleted"/> — вместе с удалёнными в корзину.</summary>
    Task<IReadOnlyList<Guid>> GetDescendantIdsAsync(Guid rootId, bool includeDeleted, CancellationToken cancellationToken);

    /// <summary>Лежит ли <paramref name="candidateId"/> внутри <paramref name="rootId"/> (или совпадает с ним) — защита от циклов.</summary>
    Task<bool> IsSelfOrDescendantAsync(Guid candidateId, Guid rootId, CancellationToken cancellationToken);

    /// <summary>Уже введённые значения текстового поля для автодополнения.</summary>
    Task<IReadOnlyList<string>> SuggestTextValuesAsync(Guid fieldId, string prefix, int take, CancellationToken cancellationToken);
}

public enum ChildrenScope
{
    /// <summary>Содержимое конкретного контейнера (parentId задан).</summary>
    Container = 0,

    /// <summary>Корень: места верхнего уровня.</summary>
    RootPlaces = 1,

    /// <summary>Корень: вещи без места («Неразобранное»).</summary>
    Unsorted = 2,
}

public enum ItemSort
{
    Name = 0,
    Updated = 1,
    Created = 2,
    Number = 3,
    Type = 4,
}

public sealed record Page<T>(IReadOnlyList<T> Items, int Total);

public sealed record PathSegment
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;
}

public sealed record ItemCounts(int Direct, int Total);

public sealed record ItemRow
{
    public Guid Id { get; init; }

    public int Number { get; init; }

    public string Name { get; init; } = string.Empty;

    public Guid TypeId { get; init; }

    public string TypeName { get; init; } = string.Empty;

    public TypeRole Role { get; init; }

    public Guid? ParentId { get; init; }

    public double Quantity { get; init; }

    public string? Unit { get; init; }

    public IconKind? IconKind { get; init; }

    public string? IconValue { get; init; }

    public string? Color { get; init; }

    public Guid? CoverMediaId { get; init; }

    public int ChildCount { get; init; }

    public int Version { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record TreeNodeRow
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public TypeRole Role { get; init; }

    public IconKind? IconKind { get; init; }

    public string? IconValue { get; init; }

    public string? Color { get; init; }

    public bool HasChildren { get; init; }
}

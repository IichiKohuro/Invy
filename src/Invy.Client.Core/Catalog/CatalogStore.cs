using Invy.Client.Core.Api;
using Invy.Contracts.Catalog;
using Invy.Contracts.Sync;

namespace Invy.Client.Core.Catalog;

/// <summary>
/// Справочники в памяти клиента: типы с характеристиками, справочники значений, теги.
/// Небольшие — загружаются целиком и перечитываются по событиям об их изменении.
/// </summary>
public sealed class CatalogStore
{
    private InvyApiClient? _api;

    public event Action? Changed;

    public IReadOnlyList<ItemTypeDto> Types { get; private set; } = [];

    public IReadOnlyList<ValueListDto> Lists { get; private set; } = [];

    public IReadOnlyList<TagDto> Tags { get; private set; } = [];

    public async Task LoadAsync(InvyApiClient api, CancellationToken cancellationToken = default)
    {
        _api = api;
        var types = api.GetTypesAsync(includeArchived: true, cancellationToken);
        var lists = api.GetListsAsync(cancellationToken);
        var tags = api.GetTagsAsync(cancellationToken);
        Types = await types;
        Lists = await lists;
        Tags = await tags;
        Changed?.Invoke();
    }

    /// <summary>Перечитывает то, что изменилось. Возвращает true, если справочники обновились.</summary>
    public async Task<bool> HandleAsync(ChangeBatch batch, CancellationToken cancellationToken = default)
    {
        if (_api is null)
        {
            return false;
        }

        var kinds = batch.Changes.Select(c => c.EntityType).ToHashSet();
        if (kinds.Contains(ChangeEntityType.ItemType))
        {
            Types = await _api.GetTypesAsync(includeArchived: true, cancellationToken);
        }

        if (kinds.Contains(ChangeEntityType.ValueList))
        {
            Lists = await _api.GetListsAsync(cancellationToken);
        }

        // Теги перечитываем и при изменении вещей: меняются счётчики.
        if (kinds.Contains(ChangeEntityType.Tag) || kinds.Contains(ChangeEntityType.Item))
        {
            Tags = await _api.GetTagsAsync(cancellationToken);
        }

        var changed = kinds.Overlaps([ChangeEntityType.ItemType, ChangeEntityType.ValueList, ChangeEntityType.Tag]);
        if (changed)
        {
            Changed?.Invoke();
        }

        return changed;
    }

    public ItemTypeDto? Type(Guid id) => Types.FirstOrDefault(t => t.Id == id);

    public ValueListDto? List(Guid? id) => id is null ? null : Lists.FirstOrDefault(l => l.Id == id);

    public TagDto? Tag(Guid id) => Tags.FirstOrDefault(t => t.Id == id);

    /// <summary>Действующие типы для выбора (без архивных).</summary>
    public IEnumerable<ItemTypeDto> ActiveTypes => Types.Where(t => !t.IsArchived);

    /// <summary>Характеристики вещи типа: унаследованные и собственные, без архивных.</summary>
    public static IReadOnlyList<FieldDefinitionDto> EffectiveFields(ItemTypeDto type) =>
        type.InheritedFields.Concat(type.Fields.OrderBy(f => f.SortOrder)).Where(f => !f.IsArchived).ToList();
}

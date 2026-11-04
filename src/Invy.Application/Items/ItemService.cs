using Invy.Application.Abstractions;
using Invy.Application.Catalog;
using Invy.Application.Errors;
using Invy.Contracts.Catalog;
using Invy.Contracts.Items;
using Invy.Domain.Items;
using Invy.Domain.Types;
using Microsoft.EntityFrameworkCore;
using DomainIconKind = Invy.Domain.Common.IconKind;
using DomainRole = Invy.Domain.Items.TypeRole;

namespace Invy.Application.Items;

/// <summary>Вещи: карточка, создание и изменение, перемещение, дублирование, корзина.</summary>
public sealed class ItemService(IInventoryDbContext db, IUnitOfWork unitOfWork, IItemQueries queries, TimeProvider clock)
{
    public const int MaxPageSize = 500;

    public async Task<ItemDetailsDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.Items.IgnoreQueryFilters().AsNoTracking().Include(i => i.Values).Include(i => i.Tags)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken) ?? throw NotFoundException.For("Вещь", id);
        return await ToDetailsAsync(item, await CatalogSnapshot.LoadAsync(db, cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<ItemDetailsDto>> BatchGetAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count > 100)
        {
            throw new ValidationException("ids", "Не больше 100 вещей за запрос");
        }

        var catalog = await CatalogSnapshot.LoadAsync(db, cancellationToken);
        var items = await db.Items.IgnoreQueryFilters().AsNoTracking().Include(i => i.Values).Include(i => i.Tags)
            .Where(i => ids.Contains(i.Id)).ToListAsync(cancellationToken);
        var result = new List<ItemDetailsDto>(items.Count);
        foreach (var item in items)
        {
            result.Add(await ToDetailsAsync(item, catalog, cancellationToken));
        }

        return result;
    }

    public async Task<PagedResult<ItemSummaryDto>> GetChildrenAsync(Guid? parentId, ChildrenScope scope, ItemSort sort, bool descending, int skip, int take, CancellationToken cancellationToken)
    {
        if (scope == ChildrenScope.Container && parentId is { } id && !await db.Items.AnyAsync(i => i.Id == id, cancellationToken))
        {
            throw NotFoundException.For("Вещь", id);
        }

        var page = await queries.GetChildrenAsync(parentId, scope, sort, descending, Math.Max(skip, 0), Math.Clamp(take, 1, MaxPageSize), cancellationToken);
        return new PagedResult<ItemSummaryDto>(page.Items.Select(ToSummary).ToList(), page.Total);
    }

    public async Task<IReadOnlyList<TreeNodeDto>> GetTreeAsync(Guid? parentId, CancellationToken cancellationToken) =>
        (await queries.GetTreeAsync(parentId, cancellationToken))
            .Select(n => new TreeNodeDto(n.Id, n.Name, (Contracts.Catalog.TypeRole)n.Role, CatalogMapping.ToIcon(n.IconKind, n.IconValue), n.Color, n.HasChildren))
            .ToList();

    public async Task<IReadOnlyList<string>> SuggestAsync(Guid fieldId, string? prefix, int take, CancellationToken cancellationToken) =>
        await queries.SuggestTextValuesAsync(fieldId, prefix ?? string.Empty, Math.Clamp(take, 1, 50), cancellationToken);

    public async Task<ItemDetailsDto> CreateAsync(SaveItemRequest request, CancellationToken cancellationToken)
    {
        var item = new Item();
        if (request.Id is { } id)
        {
            if (await db.Items.IgnoreQueryFilters().AnyAsync(i => i.Id == id, cancellationToken))
            {
                throw new ConflictException($"Вещь с id {id} уже существует");
            }

            item.Id = id;
        }

        var catalog = await CatalogSnapshot.LoadAsync(db, cancellationToken);
        await ApplyAsync(item, request, catalog, isNew: true, cancellationToken);
        db.Items.Add(item);
        await unitOfWork.SaveAsync(cancellationToken: cancellationToken);
        return await ToDetailsAsync(item, catalog, cancellationToken);
    }

    public async Task<ItemDetailsDto> UpdateAsync(Guid id, SaveItemRequest request, CancellationToken cancellationToken)
    {
        var item = await LoadForEditAsync(id, cancellationToken);
        var catalog = await CatalogSnapshot.LoadAsync(db, cancellationToken);
        if (request.Version != item.Version)
        {
            throw new ConflictException("Вещь изменена на другом устройстве", await ToDetailsAsync(item, catalog, cancellationToken));
        }

        await ApplyAsync(item, request, catalog, isNew: false, cancellationToken);
        await unitOfWork.SaveAsync(cancellationToken: cancellationToken);
        return await ToDetailsAsync(item, catalog, cancellationToken);
    }

    /// <summary>Переместить вещи; <paramref name="targetId"/> = null — в корень (места) или в «Неразобранное».</summary>
    public async Task MoveAsync(IReadOnlyList<Guid> itemIds, Guid? targetId, CancellationToken cancellationToken)
    {
        var ids = itemIds.Distinct().ToList();
        if (ids.Count is 0 or > MaxPageSize)
        {
            throw new ValidationException("itemIds", $"Выберите от 1 до {MaxPageSize} вещей");
        }

        var catalog = await CatalogSnapshot.LoadAsync(db, cancellationToken);
        Item? target = null;
        if (targetId is { } tid)
        {
            target = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == tid, cancellationToken)
                ?? throw new ValidationException("targetId", "Место назначения не найдено или в корзине");
        }

        var items = await db.Items.Where(i => ids.Contains(i.Id)).ToListAsync(cancellationToken);
        if (items.Count != ids.Count)
        {
            throw new NotFoundException("Некоторые вещи не найдены или уже в корзине");
        }

        var errors = new ValidationErrors();
        foreach (var item in items)
        {
            if (target is null)
            {
                continue;
            }

            var problem = TreeRules.ExplainCannotContain(catalog.Types[target.TypeId].Role, target.Name, catalog.Types[item.TypeId].Role, item.Name);
            if (problem is not null)
            {
                errors.Add($"itemIds[{item.Id}]", problem);
            }
            else if (await queries.IsSelfOrDescendantAsync(target.Id, item.Id, cancellationToken))
            {
                errors.Add($"itemIds[{item.Id}]", $"«{item.Name}» нельзя положить внутрь неё самой");
            }
        }

        errors.ThrowIfAny();

        foreach (var item in items)
        {
            item.ParentId = target?.Id;
        }

        await unitOfWork.SaveAsync(items.Count > 1 ? Guid.CreateVersion7() : null, cancellationToken);
    }

    public async Task<ItemDetailsDto> DuplicateAsync(Guid id, CancellationToken cancellationToken)
    {
        var source = await db.Items.AsNoTracking().Include(i => i.Values).Include(i => i.Tags)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken) ?? throw NotFoundException.For("Вещь", id);

        var copy = new Item
        {
            TypeId = source.TypeId,
            ParentId = source.ParentId,
            Description = source.Description,
            Quantity = source.Quantity,
            Unit = source.Unit,
            MinQuantity = source.MinQuantity,
            IconKind = source.IconKind,
            IconValue = source.IconValue,
            Color = source.Color,
        };
        copy.Rename(source.IsNameAuto ? source.Name : $"{source.Name} (копия)", source.IsNameAuto);
        copy.Values.AddRange(source.Values.Select(v => new ItemValue
        {
            ItemId = copy.Id,
            FieldId = v.FieldId,
            Ordinal = v.Ordinal,
            TextValue = v.TextValue,
            NumberValue = v.NumberValue,
            DateValue = v.DateValue,
            BoolValue = v.BoolValue,
            OptionId = v.OptionId,
            RefItemId = v.RefItemId,
        }));
        copy.Tags.AddRange(source.Tags.Select(t => new ItemTag { ItemId = copy.Id, TagId = t.TagId }));

        db.Items.Add(copy);
        await unitOfWork.SaveAsync(cancellationToken: cancellationToken);
        return await GetAsync(copy.Id, cancellationToken);
    }

    /// <summary>
    /// В корзину. Если внутри есть вещи, нужно выбрать: удалить их вместе с контейнером
    /// или переложить на уровень выше. Вещи одной операции получают общий DeletionId.
    /// </summary>
    public async Task DeleteAsync(Guid id, DeleteContentsMode? contents, CancellationToken cancellationToken)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, cancellationToken) ?? throw NotFoundException.For("Вещь", id);
        var descendants = await queries.GetDescendantIdsAsync(id, includeDeleted: false, cancellationToken);
        var deletionId = Guid.CreateVersion7();
        var now = clock.GetUtcNow();

        if (descendants.Count > 0)
        {
            switch (contents)
            {
                case null:
                    throw new ValidationException("contents", $"Внутри «{item.Name}» ещё {descendants.Count} шт.: удалите их вместе с контейнером (contents=Delete) или переложите на уровень выше (contents=MoveUp)");
                case DeleteContentsMode.Delete:
                    foreach (var child in await db.Items.Where(i => descendants.Contains(i.Id)).ToListAsync(cancellationToken))
                    {
                        child.MarkDeleted(deletionId, now);
                    }

                    break;
                case DeleteContentsMode.MoveUp:
                    var catalog = await CatalogSnapshot.LoadAsync(db, cancellationToken);
                    var parent = item.ParentId is { } pid ? await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == pid, cancellationToken) : null;
                    foreach (var child in await db.Items.Where(i => i.ParentId == id).ToListAsync(cancellationToken))
                    {
                        // Если родитель не может принять вещь (место в контейнер) — она уходит в корень.
                        var fits = parent is not null && TreeRules.CanContain(catalog.Types[parent.TypeId].Role, catalog.Types[child.TypeId].Role);
                        child.ParentId = fits ? parent!.Id : null;
                    }

                    break;
            }
        }

        item.MarkDeleted(deletionId, now);
        await unitOfWork.SaveAsync(deletionId, cancellationToken);
    }

    /// <summary>Корзина: по одной записи на операцию удаления (её корень).</summary>
    public async Task<PagedResult<TrashEntryDto>> GetTrashAsync(int skip, int take, CancellationToken cancellationToken)
    {
        var all = db.Items.IgnoreQueryFilters();
        var roots = all.Where(i => i.IsDeleted && !all.Any(p => p.Id == i.ParentId && p.IsDeleted && p.DeletionId == i.DeletionId));
        var total = await roots.CountAsync(cancellationToken);
        var page = await roots
            .OrderByDescending(i => i.DeletedAt)
            .Skip(Math.Max(skip, 0)).Take(Math.Clamp(take, 1, MaxPageSize))
            .Select(i => new { i.Id, i.Name, i.TypeId, i.DeletedAt, i.DeletionId, Count = all.Count(x => x.DeletionId == i.DeletionId) - 1 })
            .ToListAsync(cancellationToken);
        var types = await db.ItemTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);

        return new PagedResult<TrashEntryDto>(
            page.Select(r => new TrashEntryDto(r.Id, r.Name, r.TypeId, types.GetValueOrDefault(r.TypeId) ?? "?", r.DeletedAt!.Value, r.DeletionId!.Value, r.Count)).ToList(),
            total);
    }

    /// <summary>Восстановить вещь и то, что было удалено вместе с ней. Если прежнего места нет — в корень.</summary>
    public async Task<ItemDetailsDto> RestoreAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.Items.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == id, cancellationToken) ?? throw NotFoundException.For("Вещь", id);
        if (!item.IsDeleted)
        {
            throw new ValidationException("id", $"«{item.Name}» не в корзине");
        }

        var deletionId = item.DeletionId;
        var descendants = await queries.GetDescendantIdsAsync(id, includeDeleted: true, cancellationToken);
        var group = await db.Items.IgnoreQueryFilters()
            .Where(i => i.DeletionId == deletionId && (i.Id == id || descendants.Contains(i.Id)))
            .ToListAsync(cancellationToken);

        var catalog = await CatalogSnapshot.LoadAsync(db, cancellationToken);
        var parent = item.ParentId is { } pid ? await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == pid, cancellationToken) : null;
        if (parent is null || !TreeRules.CanContain(catalog.Types[parent.TypeId].Role, catalog.Types[item.TypeId].Role))
        {
            item.ParentId = null;
        }

        foreach (var restored in group)
        {
            restored.Restore();
        }

        await unitOfWork.SaveAsync(deletionId, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Очистить корзину: удалить навсегда.</summary>
    public async Task<int> EmptyTrashAsync(CancellationToken cancellationToken)
    {
        var deleted = await db.Items.IgnoreQueryFilters().Where(i => i.IsDeleted).ToListAsync(cancellationToken);
        if (deleted.Count == 0)
        {
            return 0;
        }

        db.Items.RemoveRange(deleted);
        await unitOfWork.SaveAsync(Guid.CreateVersion7(), cancellationToken);
        return deleted.Count;
    }

    private async Task<Item> LoadForEditAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.Items.IgnoreQueryFilters().Include(i => i.Values).Include(i => i.Tags)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken) ?? throw NotFoundException.For("Вещь", id);
        return item.IsDeleted ? throw new ValidationException("id", $"«{item.Name}» в корзине — сначала восстановите её") : item;
    }

    private async Task ApplyAsync(Item item, SaveItemRequest request, CatalogSnapshot catalog, bool isNew, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        if (!catalog.Types.TryGetValue(request.TypeId, out var type))
        {
            throw new ValidationException("typeId", "Тип не найден");
        }

        if (type.IsArchived && (isNew || item.TypeId != type.Id))
        {
            errors.Add("typeId", $"Тип «{type.Name}» в архиве");
        }

        // Значения характеристик.
        var fields = catalog.EffectiveFields(type);
        var inputs = request.Values ?? [];
        foreach (var duplicate in inputs.GroupBy(v => v.FieldId).Where(g => g.Count() > 1))
        {
            errors.Add($"values[{duplicate.Key}]", "Характеристика указана несколько раз");
        }

        foreach (var unknown in inputs.Where(v => fields.All(f => f.Id != v.FieldId)))
        {
            errors.Add($"values[{unknown.FieldId}]", $"Характеристика не относится к типу «{type.Name}»");
        }

        var values = new Dictionary<Guid, FieldValue>();
        foreach (var field in fields)
        {
            var input = inputs.FirstOrDefault(v => v.FieldId == field.Id);
            var value = input is null ? null : new FieldValue(field.Id, input.Text, input.Number, input.Date, input.Bool, input.OptionIds, input.ItemId);
            var allowedOptions = catalog.ActiveOptionIds(field.ValueListId);
            allowedOptions.UnionWith(item.Values.Where(v => v.FieldId == field.Id && v.OptionId is not null).Select(v => v.OptionId!.Value));
            errors.AddRange($"values[{field.Id}]", FieldValueValidator.Validate(field, value, allowedOptions));
            if (value is { IsEmpty: false })
            {
                values[field.Id] = value;
            }
        }

        var refs = values.Values.Where(v => v.ItemId is not null).Select(v => v.ItemId!.Value).Distinct().ToList();
        if (refs.Contains(item.Id))
        {
            errors.Add("values", "Вещь не может ссылаться на саму себя");
        }

        var existingRefs = await db.Items.Where(i => refs.Contains(i.Id)).Select(i => new { i.Id, i.Name }).ToDictionaryAsync(i => i.Id, i => i.Name, cancellationToken);
        foreach (var missing in refs.Where(r => r != item.Id && !existingRefs.ContainsKey(r)))
        {
            errors.Add("values", $"Вещь по ссылке не найдена: {missing}");
        }

        // Название: своё или по шаблону типа.
        var name = request.Name?.Trim() ?? string.Empty;
        var isAuto = false;
        if (name.Length == 0)
        {
            var formatted = fields.ToDictionary(
                f => f.Name,
                f => FieldValueFormatter.Format(f, values.GetValueOrDefault(f.Id), catalog.OptionName, id => existingRefs.GetValueOrDefault(id), includeUnit: false));
            name = TitleTemplate.Render(type.TitleTemplate, formatted) ?? string.Empty;
            isAuto = name.Length > 0;
            if (name.Length == 0)
            {
                errors.Add("name", "Укажите название");
            }
        }
        else if (name.Length > 300)
        {
            errors.Add("name", "Название — не длиннее 300 символов");
        }

        if (double.IsNaN(request.Quantity) || request.Quantity < 0)
        {
            errors.Add("quantity", "Количество не может быть отрицательным");
        }

        if (request.MinQuantity is < 0)
        {
            errors.Add("minQuantity", "Минимальный остаток не может быть отрицательным");
        }

        // Где лежит.
        if (request.ParentId is { } parentId)
        {
            var parent = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == parentId, cancellationToken);
            if (parent is null)
            {
                errors.Add("parentId", "Место не найдено или в корзине");
            }
            else if (TreeRules.ExplainCannotContain(catalog.Types[parent.TypeId].Role, parent.Name, type.Role, name) is { } problem)
            {
                errors.Add("parentId", problem);
            }
            else if (!isNew && await queries.IsSelfOrDescendantAsync(parentId, item.Id, cancellationToken))
            {
                errors.Add("parentId", "Вещь нельзя положить внутрь неё самой");
            }
        }

        // Новая роль типа не должна ломать содержимое.
        if (!isNew && type.Role != catalog.Types[item.TypeId].Role)
        {
            var childTypes = await db.Items.Where(i => i.ParentId == item.Id).Select(i => i.TypeId).Distinct().ToListAsync(cancellationToken);
            if (childTypes.Any(t => !TreeRules.CanContain(type.Role, catalog.Types[t].Role)))
            {
                errors.Add("typeId", $"Внутри «{item.Name}» лежат вещи, которые вещь типа «{type.Name}» содержать не может");
            }
        }

        var tagIds = (request.TagIds ?? []).Distinct().ToList();
        var knownTags = await db.Tags.Where(t => tagIds.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken);
        if (knownTags.Count != tagIds.Count)
        {
            errors.Add("tagIds", "Некоторые теги не найдены");
        }

        errors.ThrowIfAny();

        item.TypeId = type.Id;
        item.ParentId = request.ParentId;
        item.Rename(name, isAuto);
        item.Description = CatalogMapping.Trim(request.Description);
        item.Quantity = request.Quantity;
        item.Unit = CatalogMapping.Trim(request.Unit) ?? type.DefaultUnit;
        item.MinQuantity = request.MinQuantity;
        item.IconKind = request.Icon is null ? null : (DomainIconKind)request.Icon.Kind;
        item.IconValue = CatalogMapping.Trim(request.Icon?.Value);
        item.Color = CatalogMapping.Trim(request.Color);

        SyncValues(item, catalog.EffectiveFields(type, includeArchived: true), fields, values);
        SyncTags(item, tagIds);
    }

    /// <summary>
    /// Значения сверяются построчно (поле + порядковый номер): неизменные строки не трогаются — история
    /// показывает только реальные изменения. Значения архивных полей типа сохраняются; полей чужого типа — удаляются.
    /// </summary>
    private static void SyncValues(Item item, IReadOnlyList<FieldDefinition> allTypeFields, IReadOnlyList<FieldDefinition> activeFields, Dictionary<Guid, FieldValue> values)
    {
        var keepFieldIds = allTypeFields.Where(f => f.IsArchived).Select(f => f.Id).ToHashSet();
        var desired = activeFields.SelectMany(f => values.TryGetValue(f.Id, out var v) ? v.ToRows(item.Id, f) : []).ToList();

        foreach (var row in item.Values.ToList())
        {
            if (keepFieldIds.Contains(row.FieldId))
            {
                continue;
            }

            var match = desired.FirstOrDefault(d => d.FieldId == row.FieldId && d.Ordinal == row.Ordinal);
            if (match is null)
            {
                item.Values.Remove(row);
                continue;
            }

            desired.Remove(match);
            row.TextValue = match.TextValue;
            row.NumberValue = match.NumberValue;
            row.DateValue = match.DateValue;
            row.BoolValue = match.BoolValue;
            row.OptionId = match.OptionId;
            row.RefItemId = match.RefItemId;
        }

        item.Values.AddRange(desired);
    }

    private static void SyncTags(Item item, List<Guid> tagIds)
    {
        item.Tags.RemoveAll(t => !tagIds.Contains(t.TagId));
        foreach (var tagId in tagIds.Where(id => item.Tags.All(t => t.TagId != id)))
        {
            item.Tags.Add(new ItemTag { ItemId = item.Id, TagId = tagId });
        }
    }

    private async Task<ItemDetailsDto> ToDetailsAsync(Item item, CatalogSnapshot catalog, CancellationToken cancellationToken)
    {
        var type = catalog.Types[item.TypeId];
        var fields = catalog.EffectiveFields(type);
        var refIds = item.Values.Where(v => v.RefItemId is not null).Select(v => v.RefItemId!.Value).Distinct().ToList();
        var refNames = await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => refIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.Name, cancellationToken);

        var values = fields
            .Select(f => (Field: f, Value: FieldValue.FromRows(f, item.Values)))
            .Where(x => !x.Value.IsEmpty)
            .Select(x => new FieldValueDto(
                x.Field.Id, x.Value.Text, x.Value.Number, x.Value.Date, x.Value.Bool, x.Value.OptionIds, x.Value.ItemId,
                FieldValueFormatter.Format(x.Field, x.Value, catalog.OptionName, id => refNames.GetValueOrDefault(id))))
            .ToList();

        var path = await queries.GetPathAsync(item.Id, cancellationToken);
        var counts = await queries.GetCountsAsync(item.Id, cancellationToken);

        return new ItemDetailsDto(
            item.Id, item.Number, item.TypeId, type.Name, (Contracts.Catalog.TypeRole)type.Role, item.ParentId, item.Name, item.IsNameAuto,
            item.Description, item.Quantity, item.Unit, item.MinQuantity,
            CatalogMapping.ToIcon(item.IconKind ?? type.IconKind, item.IconValue ?? type.IconValue), item.Color ?? type.Color, item.CoverMediaId,
            values, item.Tags.Select(t => t.TagId).ToList(),
            path.Select(p => new PathSegmentDto(p.Id, p.Name)).ToList(),
            new ItemCountsDto(counts.Direct, counts.Total),
            item.IsDeleted, item.Version, item.CreatedAt, item.UpdatedAt);
    }

    private static ItemSummaryDto ToSummary(ItemRow r) => new(
        r.Id, r.Number, r.Name, r.TypeId, r.TypeName, (Contracts.Catalog.TypeRole)r.Role, r.ParentId, r.Quantity, r.Unit,
        CatalogMapping.ToIcon(r.IconKind, r.IconValue), r.Color, r.CoverMediaId, r.ChildCount, r.Version, r.UpdatedAt);
}

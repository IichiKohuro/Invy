using Invy.Application.Abstractions;
using Invy.Application.Errors;
using Invy.Contracts.Catalog;
using Invy.Domain.Items;
using Invy.Domain.Text;
using Invy.Domain.Types;
using Microsoft.EntityFrameworkCore;
using DomainDataType = Invy.Domain.Types.FieldDataType;
using DomainIconKind = Invy.Domain.Common.IconKind;
using DomainRole = Invy.Domain.Items.TypeRole;

namespace Invy.Application.Catalog;

/// <summary>Типы вещей и их характеристики.</summary>
public sealed class ItemTypeService(IInventoryDbContext db, IUnitOfWork unitOfWork)
{
    public async Task<IReadOnlyList<ItemTypeDto>> ListAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var all = await db.ItemTypes.AsNoTracking().Include(t => t.Fields).ToDictionaryAsync(t => t.Id, cancellationToken);
        return all.Values
            .Where(t => includeArchived || !t.IsArchived)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.NameNorm, StringComparer.Ordinal)
            .Select(t => t.ToDto(all))
            .ToList();
    }

    public async Task<ItemTypeDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var all = await db.ItemTypes.AsNoTracking().Include(t => t.Fields).ToDictionaryAsync(t => t.Id, cancellationToken);
        return all.TryGetValue(id, out var type) ? type.ToDto(all) : throw NotFoundException.For("Тип", id);
    }

    public async Task<ItemTypeDto> CreateAsync(SaveItemTypeRequest request, CancellationToken cancellationToken)
    {
        var all = await db.ItemTypes.Include(t => t.Fields).ToDictionaryAsync(t => t.Id, cancellationToken);
        var type = new ItemType();
        if (request.Id is { } id)
        {
            if (all.ContainsKey(id))
            {
                throw new ConflictException($"Тип с id {id} уже существует");
            }

            type.Id = id;
        }

        await ApplyAsync(type, request, all, isNew: true, cancellationToken);
        db.ItemTypes.Add(type);
        await unitOfWork.SaveAsync(cancellationToken: cancellationToken);
        return type.ToDto(all.Values.Append(type).ToDictionary(t => t.Id));
    }

    public async Task<ItemTypeDto> UpdateAsync(Guid id, SaveItemTypeRequest request, CancellationToken cancellationToken)
    {
        var all = await db.ItemTypes.Include(t => t.Fields).ToDictionaryAsync(t => t.Id, cancellationToken);
        var type = all.GetValueOrDefault(id) ?? throw NotFoundException.For("Тип", id);
        if (request.Version != type.Version)
        {
            throw new ConflictException("Тип изменён на другом устройстве", type.ToDto(all));
        }

        await ApplyAsync(type, request, all, isNew: false, cancellationToken);
        await unitOfWork.SaveAsync(cancellationToken: cancellationToken);
        return type.ToDto(all);
    }

    /// <summary>Удалить можно только неиспользуемый тип; используемый — архивировать (IsArchived в PUT).</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var type = await db.ItemTypes.Include(t => t.Fields).FirstOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw NotFoundException.For("Тип", id);
        var used = await db.Items.IgnoreQueryFilters().CountAsync(i => i.TypeId == id, cancellationToken);
        if (used > 0)
        {
            throw new ValidationException("id", $"Тип «{type.Name}» используется ({used} шт., включая корзину): переведите вещи на другой тип или отправьте тип в архив");
        }

        if (await db.ItemTypes.AnyAsync(t => t.BaseTypeId == id, cancellationToken))
        {
            throw new ValidationException("id", $"Тип «{type.Name}» — базовый для других типов");
        }

        db.ItemTypes.Remove(type);
        await unitOfWork.SaveAsync(cancellationToken: cancellationToken);
    }

    private async Task ApplyAsync(ItemType type, SaveItemTypeRequest request, Dictionary<Guid, ItemType> all, bool isNew, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var name = request.Name?.Trim() ?? string.Empty;
        var nameNorm = TextNormalizer.Normalize(name);
        if (name.Length == 0)
        {
            errors.Add("name", "Укажите название типа");
        }
        else if (name.Length > 100)
        {
            errors.Add("name", "Название — не длиннее 100 символов");
        }
        else if (all.Values.Any(t => t.Id != type.Id && t.NameNorm == nameNorm))
        {
            errors.Add("name", $"Тип «{name}» уже есть");
        }

        if (request.BaseTypeId is { } baseId)
        {
            if (!all.ContainsKey(baseId))
            {
                errors.Add("baseTypeId", "Базовый тип не найден");
            }
            else if (CreatesCycle(type.Id, baseId, all))
            {
                errors.Add("baseTypeId", "Тип не может наследовать сам себя");
            }
        }

        var role = (DomainRole)request.Role;
        if (!isNew && role != type.Role)
        {
            await ValidateRoleChangeAsync(type, role, errors, cancellationToken);
        }

        // Характеристики: имена уникальны вместе с унаследованными.
        var inherited = request.BaseTypeId is { } b && all.ContainsKey(b)
            ? TypeHierarchy.InheritedFields(new ItemType { Id = type.Id, BaseTypeId = b }, all)
            : [];
        var takenNames = inherited.Where(f => !f.IsArchived).Select(f => TextNormalizer.Normalize(f.Name)).ToHashSet();
        var listIds = await db.ValueLists.Select(l => l.Id).ToListAsync(cancellationToken);

        for (var i = 0; i < request.Fields.Count; i++)
        {
            var input = request.Fields[i];
            var key = $"fields[{i}]";
            var fieldName = input.Name?.Trim() ?? string.Empty;
            if (fieldName.Length == 0)
            {
                errors.Add(key, "Укажите название характеристики");
            }
            else if (!input.IsArchived && !takenNames.Add(TextNormalizer.Normalize(fieldName)))
            {
                errors.Add(key, $"Характеристика «{fieldName}» повторяется");
            }

            var dataType = (DomainDataType)input.DataType;
            if (dataType is DomainDataType.Choice or DomainDataType.MultiChoice && (input.ValueListId is not { } listId || !listIds.Contains(listId)))
            {
                errors.Add(key, $"Для списка «{fieldName}» выберите справочник");
            }

            var settings = input.Settings.ToDomain();
            if (settings.Min > settings.Max)
            {
                errors.Add(key, $"«{fieldName}»: минимум больше максимума");
            }

            if (settings.Decimals is < 0 or > 10)
            {
                errors.Add(key, $"«{fieldName}»: знаков после запятой — от 0 до 10");
            }

            if (settings.Pattern is { } pattern && !FieldValueValidator.IsValidPattern(pattern))
            {
                errors.Add(key, $"«{fieldName}»: некорректный шаблон проверки");
            }

            if (input.Id is { } fieldId)
            {
                var existing = type.Fields.FirstOrDefault(f => f.Id == fieldId);
                if (existing is null)
                {
                    errors.Add(key, "Характеристика не относится к этому типу");
                }
                else if ((existing.DataType != dataType || existing.ValueListId != input.ValueListId)
                    && await db.ItemValues.AnyAsync(v => v.FieldId == fieldId, cancellationToken))
                {
                    errors.Add(key, $"У вещей уже есть значения «{existing.Name}»: тип данных и справочник поля менять нельзя");
                }
            }
        }

        foreach (var placeholder in TitleTemplate.FieldNames(request.TitleTemplate))
        {
            if (!takenNames.Contains(TextNormalizer.Normalize(placeholder)))
            {
                errors.Add("titleTemplate", $"Шаблон ссылается на неизвестную характеристику «{placeholder}»");
            }
        }

        errors.ThrowIfAny();

        type.Rename(name);
        type.Role = role;
        type.BaseTypeId = request.BaseTypeId;
        type.IconKind = request.Icon is null ? null : (DomainIconKind)request.Icon.Kind;
        type.IconValue = CatalogMapping.Trim(request.Icon?.Value);
        type.Color = CatalogMapping.Trim(request.Color);
        type.TitleTemplate = CatalogMapping.Trim(request.TitleTemplate);
        type.DefaultUnit = CatalogMapping.Trim(request.DefaultUnit);
        type.SortOrder = request.SortOrder;
        type.IsArchived = request.IsArchived;
        SyncFields(type, request.Fields);
    }

    private static void SyncFields(ItemType type, IReadOnlyList<FieldDefinitionInput> inputs)
    {
        var keep = new HashSet<Guid>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            var field = input.Id is { } id ? type.Fields.First(f => f.Id == id) : new FieldDefinition { TypeId = type.Id };
            if (input.Id is null)
            {
                type.Fields.Add(field);
            }

            field.Name = input.Name.Trim();
            field.DataType = (DomainDataType)input.DataType;
            field.Unit = CatalogMapping.Trim(input.Unit);
            field.ValueListId = field.UsesValueList ? input.ValueListId : null;
            field.GroupName = CatalogMapping.Trim(input.GroupName);
            field.IsRequired = input.IsRequired;
            field.ShowInList = input.ShowInList;
            field.RememberLast = input.RememberLast;
            field.Settings = input.Settings.ToDomain();
            field.SortOrder = i;
            field.IsArchived = input.IsArchived;
            keep.Add(field.Id);
        }

        // Не переданные характеристики архивируются: значения у вещей сохраняются.
        foreach (var field in type.Fields.Where(f => !keep.Contains(f.Id)))
        {
            field.IsArchived = true;
        }
    }

    private static bool CreatesCycle(Guid typeId, Guid baseId, Dictionary<Guid, ItemType> all)
    {
        var current = baseId;
        for (var depth = 0; depth < 32; depth++)
        {
            if (current == typeId)
            {
                return true;
            }

            if (all.GetValueOrDefault(current)?.BaseTypeId is not { } next)
            {
                return false;
            }

            current = next;
        }

        return true;
    }

    /// <summary>Новая роль не должна нарушить уже сложенные вещи: содержимое и то, где лежат вещи этого типа.</summary>
    private async Task ValidateRoleChangeAsync(ItemType type, DomainRole newRole, ValidationErrors errors, CancellationToken cancellationToken)
    {
        DomainRole RoleOf(Guid typeId, DomainRole stored) => typeId == type.Id ? newRole : stored;

        var childRoles = await (
                from child in db.Items
                join parent in db.Items on child.ParentId equals parent.Id
                join childType in db.ItemTypes on child.TypeId equals childType.Id
                where parent.TypeId == type.Id
                select new { childType.Id, childType.Role })
            .Distinct().ToListAsync(cancellationToken);
        if (childRoles.Any(c => !TreeRules.CanContain(newRole, RoleOf(c.Id, c.Role))))
        {
            errors.Add("role", $"Вещи типа «{type.Name}» содержат то, что вещь с новой ролью содержать не может");
        }

        var parentRoles = await (
                from child in db.Items
                join parent in db.Items on child.ParentId equals parent.Id
                join parentType in db.ItemTypes on parent.TypeId equals parentType.Id
                where child.TypeId == type.Id
                select new { parentType.Id, parentType.Role })
            .Distinct().ToListAsync(cancellationToken);
        if (parentRoles.Any(p => !TreeRules.CanContain(RoleOf(p.Id, p.Role), newRole)))
        {
            errors.Add("role", $"Вещи типа «{type.Name}» лежат там, где вещь с новой ролью лежать не может");
        }
    }
}

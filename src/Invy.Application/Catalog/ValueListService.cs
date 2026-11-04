using Invy.Application.Abstractions;
using Invy.Application.Errors;
using Invy.Contracts.Catalog;
using Invy.Domain.Lists;
using Invy.Domain.Text;
using Microsoft.EntityFrameworkCore;
using DomainSortMode = Invy.Domain.Lists.ValueListSortMode;

namespace Invy.Application.Catalog;

/// <summary>Справочники значений для полей-списков.</summary>
public sealed class ValueListService(IInventoryDbContext db, IUnitOfWork unitOfWork)
{
    public async Task<IReadOnlyList<ValueListDto>> ListAsync(CancellationToken cancellationToken) =>
        (await db.ValueLists.AsNoTracking().Include(l => l.Options).ToListAsync(cancellationToken))
            .OrderBy(l => l.NameNorm, StringComparer.Ordinal)
            .Select(l => l.ToDto())
            .ToList();

    public async Task<ValueListDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.ValueLists.AsNoTracking().Include(l => l.Options).FirstOrDefaultAsync(l => l.Id == id, cancellationToken))?.ToDto()
        ?? throw NotFoundException.For("Справочник", id);

    public async Task<ValueListDto> CreateAsync(SaveValueListRequest request, CancellationToken cancellationToken)
    {
        var list = new ValueList();
        if (request.Id is { } id)
        {
            if (await db.ValueLists.AnyAsync(l => l.Id == id, cancellationToken))
            {
                throw new ConflictException($"Справочник с id {id} уже существует");
            }

            list.Id = id;
        }

        Apply(list, request);
        db.ValueLists.Add(list);
        await unitOfWork.SaveAsync(cancellationToken: cancellationToken);
        return list.ToDto();
    }

    public async Task<ValueListDto> UpdateAsync(Guid id, SaveValueListRequest request, CancellationToken cancellationToken)
    {
        var list = await db.ValueLists.Include(l => l.Options).FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            ?? throw NotFoundException.For("Справочник", id);
        if (request.Version != list.Version)
        {
            throw new ConflictException("Справочник изменён на другом устройстве", list.ToDto());
        }

        Apply(list, request);
        await unitOfWork.SaveAsync(cancellationToken: cancellationToken);
        return list.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var list = await db.ValueLists.Include(l => l.Options).FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            ?? throw NotFoundException.For("Справочник", id);
        var fields = await db.FieldDefinitions.Where(f => f.ValueListId == id).Select(f => f.Name).ToListAsync(cancellationToken);
        if (fields.Count > 0)
        {
            throw new ValidationException("id", $"Справочник используется характеристиками: {string.Join(", ", fields.Distinct())}");
        }

        db.ValueLists.Remove(list);
        await unitOfWork.SaveAsync(cancellationToken: cancellationToken);
    }

    /// <summary>Добавить вариант из формы вещи. Если такой уже есть (в т. ч. в архиве) — вернуть его.</summary>
    public async Task<ValueListOptionDto> AddOptionAsync(Guid listId, AddOptionRequest request, CancellationToken cancellationToken)
    {
        var list = await db.ValueLists.Include(l => l.Options).FirstOrDefaultAsync(l => l.Id == listId, cancellationToken)
            ?? throw NotFoundException.For("Справочник", listId);
        var value = request.Value?.Trim() ?? string.Empty;
        if (value.Length is 0 or > 200)
        {
            throw new ValidationException("value", "Вариант — от 1 до 200 символов");
        }

        var existing = list.Options.FirstOrDefault(o => o.ValueNorm == TextNormalizer.Normalize(value));
        if (existing is not null)
        {
            if (existing.IsArchived)
            {
                existing.IsArchived = false;
                await unitOfWork.SaveAsync(cancellationToken: cancellationToken);
            }

            return existing.ToDto();
        }

        if (!list.AllowAddFromForm)
        {
            throw new ValidationException("value", $"В справочник «{list.Name}» нельзя добавлять варианты из формы");
        }

        var option = new ValueListOption { ListId = list.Id, Color = CatalogMapping.Trim(request.Color), SortOrder = list.Options.Count == 0 ? 0 : list.Options.Max(o => o.SortOrder) + 1 };
        option.SetValue(value);
        list.Options.Add(option);
        await unitOfWork.SaveAsync(cancellationToken: cancellationToken);
        return option.ToDto();
    }

    private static void Apply(ValueList list, SaveValueListRequest request)
    {
        var errors = new ValidationErrors();
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100)
        {
            errors.Add("name", "Название справочника — от 1 до 100 символов");
        }

        var seen = new HashSet<string>();
        for (var i = 0; i < request.Options.Count; i++)
        {
            var option = request.Options[i];
            var value = option.Value?.Trim() ?? string.Empty;
            if (value.Length is 0 or > 200)
            {
                errors.Add($"options[{i}]", "Вариант — от 1 до 200 символов");
            }
            else if (!seen.Add(TextNormalizer.Normalize(value)))
            {
                errors.Add($"options[{i}]", $"Вариант «{value}» повторяется");
            }

            if (option.Id is { } id && list.Options.All(o => o.Id != id))
            {
                errors.Add($"options[{i}]", "Вариант не относится к этому справочнику");
            }
        }

        errors.ThrowIfAny();

        list.Rename(name);
        list.IsShared = request.IsShared;
        list.AllowAddFromForm = request.AllowAddFromForm;
        list.SortMode = (DomainSortMode)request.SortMode;

        var keep = new HashSet<Guid>();
        for (var i = 0; i < request.Options.Count; i++)
        {
            var input = request.Options[i];
            var option = input.Id is { } id ? list.Options.First(o => o.Id == id) : new ValueListOption { ListId = list.Id };
            if (input.Id is null)
            {
                list.Options.Add(option);
            }

            option.SetValue(input.Value);
            option.Color = CatalogMapping.Trim(input.Color);
            option.SortOrder = i;
            option.IsArchived = input.IsArchived;
            keep.Add(option.Id);
        }

        // Не переданные варианты архивируются: выбранные значения у вещей не ломаются.
        foreach (var option in list.Options.Where(o => !keep.Contains(o.Id)))
        {
            option.IsArchived = true;
        }
    }
}

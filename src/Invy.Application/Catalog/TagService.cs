using Invy.Application.Abstractions;
using Invy.Application.Errors;
using Invy.Contracts.Catalog;
using Invy.Domain.Items;
using Invy.Domain.Tags;
using Invy.Domain.Text;
using Microsoft.EntityFrameworkCore;

namespace Invy.Application.Catalog;

public sealed class TagService(IInventoryDbContext db, IUnitOfWork unitOfWork)
{
    public async Task<IReadOnlyList<TagDto>> ListAsync(CancellationToken cancellationToken)
    {
        var counts = await CountsAsync(null, cancellationToken);
        return (await db.Tags.AsNoTracking().ToListAsync(cancellationToken))
            .OrderBy(t => t.SortOrder).ThenBy(t => t.NameNorm, StringComparer.Ordinal)
            .Select(t => t.ToDto(counts.GetValueOrDefault(t.Id)))
            .ToList();
    }

    public async Task<TagDto> CreateAsync(SaveTagRequest request, CancellationToken cancellationToken)
    {
        var tag = new Tag();
        if (request.Id is { } id)
        {
            tag.Id = id;
        }

        await ApplyAsync(tag, request, cancellationToken);
        db.Tags.Add(tag);
        await unitOfWork.SaveAsync(cancellationToken: cancellationToken);
        return tag.ToDto(0);
    }

    public async Task<TagDto> UpdateAsync(Guid id, SaveTagRequest request, CancellationToken cancellationToken)
    {
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw NotFoundException.For("Тег", id);
        var count = (await CountsAsync(id, cancellationToken)).GetValueOrDefault(id);
        if (request.Version != tag.Version)
        {
            throw new ConflictException("Тег изменён на другом устройстве", tag.ToDto(count));
        }

        await ApplyAsync(tag, request, cancellationToken);
        await unitOfWork.SaveAsync(cancellationToken: cancellationToken);
        return tag.ToDto(count);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw NotFoundException.For("Тег", id);
        db.ItemTags.RemoveRange(await db.ItemTags.Where(t => t.TagId == id).ToListAsync(cancellationToken));
        db.Tags.Remove(tag);
        await unitOfWork.SaveAsync(Guid.CreateVersion7(), cancellationToken);
    }

    /// <summary>Вещи тега <paramref name="sourceId"/> получают тег <paramref name="targetId"/>; исходный тег удаляется.</summary>
    public async Task<TagDto> MergeAsync(Guid sourceId, Guid targetId, CancellationToken cancellationToken)
    {
        if (sourceId == targetId)
        {
            throw new ValidationException("targetTagId", "Тег нельзя объединить сам с собой");
        }

        var source = await db.Tags.FirstOrDefaultAsync(t => t.Id == sourceId, cancellationToken) ?? throw NotFoundException.For("Тег", sourceId);
        var target = await db.Tags.FirstOrDefaultAsync(t => t.Id == targetId, cancellationToken) ?? throw NotFoundException.For("Тег", targetId);

        var sourceLinks = await db.ItemTags.Where(t => t.TagId == sourceId).ToListAsync(cancellationToken);
        var alreadyTagged = (await db.ItemTags.Where(t => t.TagId == targetId).Select(t => t.ItemId).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var link in sourceLinks)
        {
            if (!alreadyTagged.Contains(link.ItemId))
            {
                db.ItemTags.Add(new ItemTag { ItemId = link.ItemId, TagId = targetId });
            }
        }

        db.ItemTags.RemoveRange(sourceLinks);
        db.Tags.Remove(source);
        await unitOfWork.SaveAsync(Guid.CreateVersion7(), cancellationToken);
        return target.ToDto((await CountsAsync(targetId, cancellationToken)).GetValueOrDefault(targetId));
    }

    private async Task ApplyAsync(Tag tag, SaveTagRequest request, CancellationToken cancellationToken)
    {
        var probe = new Tag();
        probe.Rename(request.Name ?? string.Empty);
        if (probe.Name.Length is 0 or > 100)
        {
            throw new ValidationException("name", "Название тега — от 1 до 100 символов");
        }

        if (await db.Tags.AnyAsync(t => t.Id != tag.Id && t.NameNorm == TextNormalizer.Normalize(probe.Name), cancellationToken))
        {
            throw new ValidationException("name", $"Тег #{probe.Name} уже есть");
        }

        tag.Rename(probe.Name);
        tag.Color = CatalogMapping.Trim(request.Color);
        tag.SortOrder = request.SortOrder;
    }

    /// <summary>Количество вещей (без корзины) по тегам.</summary>
    private async Task<Dictionary<Guid, int>> CountsAsync(Guid? tagId, CancellationToken cancellationToken) =>
        await (from link in db.ItemTags
               join item in db.Items on link.ItemId equals item.Id
               where tagId == null || link.TagId == tagId
               group link by link.TagId into g
               select new { TagId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TagId, x => x.Count, cancellationToken);
}

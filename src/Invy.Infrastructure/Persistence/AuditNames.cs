using Invy.Domain.Items;
using Invy.Domain.Lists;
using Invy.Domain.Tags;
using Invy.Domain.Types;
using Microsoft.EntityFrameworkCore;

namespace Invy.Infrastructure.Persistence;

/// <summary>
/// Имена для истории изменений: поля, варианты справочников, вещи, типы, теги.
/// Сначала — из отслеживаемых сущностей (созданных в этой же операции), остальное — одним запросом на вид.
/// </summary>
internal sealed class AuditNames
{
    public Dictionary<Guid, FieldDefinition> Fields { get; } = [];

    private Dictionary<Guid, string> Options { get; } = [];

    private Dictionary<Guid, string> Items { get; } = [];

    private Dictionary<Guid, string> Types { get; } = [];

    private Dictionary<Guid, string> Tags { get; } = [];

    public string? Option(Guid id) => Options.GetValueOrDefault(id);

    public string? Item(Guid id) => Items.GetValueOrDefault(id);

    public string? Type(Guid? id) => id is { } value ? Types.GetValueOrDefault(value) : null;

    public string Tag(Guid id) => Tags.GetValueOrDefault(id) ?? "?";

    public static async Task<AuditNames> LoadAsync(InvyDbContext db, CancellationToken cancellationToken)
    {
        var names = new AuditNames();
        var tracker = db.ChangeTracker;
        var itemIds = new HashSet<Guid>();
        var optionIds = new HashSet<Guid>();
        var typeIds = new HashSet<Guid>();
        var tagIds = new HashSet<Guid>();

        foreach (var e in tracker.Entries<Item>())
        {
            names.Items[e.Entity.Id] = e.Entity.Name;
            AddIfSet(itemIds, e.Entity.ParentId, e.Property(i => i.ParentId).OriginalValue);
            AddIfSet(typeIds, e.Entity.TypeId, e.Property(i => i.TypeId).OriginalValue);
        }

        foreach (var e in tracker.Entries<ItemValue>())
        {
            AddIfSet(optionIds, e.Entity.OptionId, e.Property(v => v.OptionId).OriginalValue);
            AddIfSet(itemIds, e.Entity.RefItemId, e.Property(v => v.RefItemId).OriginalValue);
        }

        foreach (var e in tracker.Entries<ItemTag>())
        {
            tagIds.Add(e.Entity.TagId);
        }

        foreach (var e in tracker.Entries<ItemType>())
        {
            names.Types[e.Entity.Id] = e.Entity.Name;
            AddIfSet(typeIds, e.Entity.BaseTypeId, e.Property(t => t.BaseTypeId).OriginalValue);
        }

        foreach (var e in tracker.Entries<ValueListOption>())
        {
            names.Options[e.Entity.Id] = e.Entity.Value;
        }

        foreach (var e in tracker.Entries<Tag>())
        {
            names.Tags[e.Entity.Id] = e.Entity.Name;
        }

        // Характеристик немного (десятки–сотни): загружаем все, отслеживаемые версии важнее.
        foreach (var field in await db.FieldDefinitions.AsNoTracking().ToListAsync(cancellationToken))
        {
            names.Fields[field.Id] = field;
        }

        foreach (var e in tracker.Entries<FieldDefinition>())
        {
            names.Fields[e.Entity.Id] = e.Entity;
        }

        var missingItems = Missing(itemIds, names.Items);
        if (missingItems.Count > 0)
        {
            foreach (var row in await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => missingItems.Contains(i.Id)).Select(i => new { i.Id, i.Name }).ToListAsync(cancellationToken))
            {
                names.Items[row.Id] = row.Name;
            }
        }

        var missingOptions = Missing(optionIds, names.Options);
        if (missingOptions.Count > 0)
        {
            foreach (var row in await db.ValueListOptions.AsNoTracking().Where(o => missingOptions.Contains(o.Id)).Select(o => new { o.Id, o.Value }).ToListAsync(cancellationToken))
            {
                names.Options[row.Id] = row.Value;
            }
        }

        var missingTypes = Missing(typeIds, names.Types);
        if (missingTypes.Count > 0)
        {
            foreach (var row in await db.ItemTypes.AsNoTracking().Where(t => missingTypes.Contains(t.Id)).Select(t => new { t.Id, t.Name }).ToListAsync(cancellationToken))
            {
                names.Types[row.Id] = row.Name;
            }
        }

        var missingTags = Missing(tagIds, names.Tags);
        if (missingTags.Count > 0)
        {
            foreach (var row in await db.Tags.AsNoTracking().Where(t => missingTags.Contains(t.Id)).Select(t => new { t.Id, t.Name }).ToListAsync(cancellationToken))
            {
                names.Tags[row.Id] = row.Name;
            }
        }

        return names;
    }

    private static void AddIfSet(HashSet<Guid> set, Guid? current, Guid? original)
    {
        if (current is { } c)
        {
            set.Add(c);
        }

        if (original is { } o)
        {
            set.Add(o);
        }
    }

    private static List<Guid> Missing(HashSet<Guid> ids, Dictionary<Guid, string> known) => ids.Where(id => !known.ContainsKey(id)).ToList();
}

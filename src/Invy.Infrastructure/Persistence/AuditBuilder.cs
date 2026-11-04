using System.Globalization;
using Invy.Domain.Audit;
using Invy.Domain.Devices;
using Invy.Domain.Files;
using Invy.Domain.Items;
using Invy.Domain.Lists;
using Invy.Domain.Tags;
using Invy.Domain.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Invy.Infrastructure.Persistence;

/// <summary>
/// История изменений из ChangeTracker: одна запись на агрегат (вещь, тип, справочник, тег, устройство),
/// изменения частей (значения, теги, фото, поля, варианты) входят в запись своего агрегата.
/// Имена и значения сохраняются снимком — переименование поля не ломает старую историю.
/// </summary>
internal static class AuditBuilder
{
    private const int MaxText = 200;
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public static async Task<List<AuditEntry>> BuildAsync(InvyDbContext db, CancellationToken cancellationToken)
    {
        var tracker = db.ChangeTracker;
        if (!tracker.Entries().Any(IsChanged))
        {
            return [];
        }

        var names = await AuditNames.LoadAsync(db, cancellationToken);
        var result = new List<AuditEntry>();
        result.AddRange(tracker.Entries<Item>().Where(IsChanged).Select(e => ForItem(tracker, e, names)));
        result.AddRange(tracker.Entries<ItemType>().Where(IsChanged).Select(e => ForType(tracker, e, names)));
        result.AddRange(tracker.Entries<ValueList>().Where(IsChanged).Select(e => ForList(tracker, e)));
        result.AddRange(tracker.Entries<Tag>().Where(IsChanged).Select(ForTag));
        result.AddRange(tracker.Entries<Device>().Where(IsChanged).Select(ForDevice).OfType<AuditEntry>());
        return result;
    }

    private static bool IsChanged(EntityEntry e) => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted;

    private static AuditEntry ForItem(ChangeTracker tracker, EntityEntry<Item> e, AuditNames names)
    {
        var item = e.Entity;
        var changes = new List<AuditChange>();
        var entry = new AuditEntry { EntityType = AuditEntityType.Item, EntityId = item.Id, ParentId = item.ParentId };

        switch (e.State)
        {
            case EntityState.Added:
                entry.Action = AuditAction.Created;
                entry.Summary = Limit($"Создано: {item.Name}");
                if (item.ParentId is { } parent)
                {
                    changes.Add(new("ParentId", "Место", null, names.Item(parent)));
                }

                if (Math.Abs(item.Quantity - 1) > double.Epsilon)
                {
                    changes.Add(new("Quantity", "Количество", null, Format(item.Quantity)));
                }

                if (!string.IsNullOrWhiteSpace(item.Description))
                {
                    changes.Add(new("Description", "Описание", null, Truncate(item.Description)));
                }

                break;

            case EntityState.Deleted:
                entry.Action = AuditAction.Purged;
                entry.Summary = Limit($"Удалено навсегда: {item.Name}");
                break;

            default:
                var wasDeleted = e.Property(i => i.IsDeleted).OriginalValue;
                var oldParent = e.Property(i => i.ParentId).OriginalValue;
                Changed(changes, e, nameof(Item.Name), "Название");
                Changed(changes, e, nameof(Item.Description), "Описание");
                Changed(changes, e, nameof(Item.Quantity), "Количество");
                Changed(changes, e, nameof(Item.Unit), "Единица");
                Changed(changes, e, nameof(Item.MinQuantity), "Минимальный остаток");
                Changed(changes, e, nameof(Item.TypeId), "Тип", v => names.Type(v as Guid?));
                Changed(changes, e, nameof(Item.ParentId), "Место", v => v is Guid id ? names.Item(id) : "Неразобранное");
                Changed(changes, e, nameof(Item.IconValue), "Иконка");
                Changed(changes, e, nameof(Item.Color), "Цвет");
                Changed(changes, e, nameof(Item.CoverMediaId), "Обложка", v => v is null ? null : "фото");

                if (!wasDeleted && item.IsDeleted)
                {
                    entry.Action = AuditAction.Deleted;
                    entry.Summary = Limit($"В корзину: {item.Name}");
                }
                else if (wasDeleted && !item.IsDeleted)
                {
                    entry.Action = AuditAction.Restored;
                    entry.Summary = Limit($"Восстановлено: {item.Name}");
                }
                else if (oldParent != item.ParentId && changes.Count == 1)
                {
                    entry.Action = AuditAction.Moved;
                    entry.OldParentId = oldParent;
                    entry.Summary = Limit($"Перемещено: {changes[0].Old} → {changes[0].New}");
                }
                else
                {
                    entry.Action = AuditAction.Updated;
                    entry.OldParentId = oldParent != item.ParentId ? oldParent : null;
                }

                break;
        }

        changes.AddRange(ValueChanges(tracker, item.Id, names));
        changes.AddRange(TagChanges(tracker, item.Id, names));
        changes.AddRange(MediaChanges(tracker, item.Id));

        if (entry.Action == AuditAction.Updated)
        {
            entry.Summary = Limit($"Изменено: {item.Name} — {string.Join(", ", changes.Select(c => c.Label).Distinct().Take(3))}{(changes.Count > 3 ? " и др." : string.Empty)}");
        }

        entry.Changes = changes;
        return entry;
    }

    private static IEnumerable<AuditChange> ValueChanges(ChangeTracker tracker, Guid itemId, AuditNames names)
    {
        var rows = tracker.Entries<ItemValue>().Where(v => v.Entity.ItemId == itemId).ToList();
        foreach (var fieldId in rows.Where(r => r.State != EntityState.Unchanged).Select(r => r.Entity.FieldId).Distinct())
        {
            if (!names.Fields.TryGetValue(fieldId, out var field))
            {
                continue;
            }

            var fieldRows = rows.Where(r => r.Entity.FieldId == fieldId).ToList();
            var oldRows = fieldRows.Where(r => r.State != EntityState.Added)
                .Select(r => r.State == EntityState.Modified ? (ItemValue)r.OriginalValues.ToObject() : r.Entity);
            var newRows = fieldRows.Where(r => r.State != EntityState.Deleted).Select(r => r.Entity);

            var oldText = FieldValueFormatter.Format(field, FieldValue.FromRows(field, oldRows), names.Option, names.Item);
            var newText = FieldValueFormatter.Format(field, FieldValue.FromRows(field, newRows), names.Option, names.Item);
            if (oldText != newText)
            {
                yield return new AuditChange($"field:{fieldId}", field.Name, Truncate(oldText), Truncate(newText));
            }
        }
    }

    private static IEnumerable<AuditChange> TagChanges(ChangeTracker tracker, Guid itemId, AuditNames names)
    {
        var rows = tracker.Entries<ItemTag>().Where(t => t.Entity.ItemId == itemId).ToList();
        if (!rows.Any(r => r.State is EntityState.Added or EntityState.Deleted))
        {
            yield break;
        }

        string? Join(IEnumerable<EntityEntry<ItemTag>> tags)
        {
            var list = tags.Select(t => "#" + names.Tag(t.Entity.TagId)).Order(StringComparer.CurrentCulture).ToList();
            return list.Count == 0 ? null : string.Join(", ", list);
        }

        yield return new AuditChange(
            "tags",
            "Теги",
            Join(rows.Where(r => r.State != EntityState.Added)),
            Join(rows.Where(r => r.State != EntityState.Deleted)));
    }

    private static IEnumerable<AuditChange> MediaChanges(ChangeTracker tracker, Guid itemId)
    {
        foreach (var media in tracker.Entries<MediaFile>().Where(m => m.Entity.ItemId == itemId))
        {
            if (media.State == EntityState.Added)
            {
                yield return new AuditChange($"media:{media.Entity.Id}", "Фото", null, media.Entity.FileName);
            }
            else if (media.State == EntityState.Deleted)
            {
                yield return new AuditChange($"media:{media.Entity.Id}", "Фото", media.Entity.FileName, null);
            }
        }
    }

    private static AuditEntry ForType(ChangeTracker tracker, EntityEntry<ItemType> e, AuditNames names)
    {
        var type = e.Entity;
        var changes = new List<AuditChange>();
        var (action, summary) = e.State switch
        {
            EntityState.Added => (AuditAction.Created, $"Создан тип: {type.Name}"),
            EntityState.Deleted => (AuditAction.Deleted, $"Удалён тип: {type.Name}"),
            _ => (AuditAction.Updated, $"Изменён тип: {type.Name}"),
        };

        if (e.State == EntityState.Modified)
        {
            Changed(changes, e, nameof(ItemType.Name), "Название");
            Changed(changes, e, nameof(ItemType.Role), "Роль", v => v is TypeRole r ? RoleName(r) : null);
            Changed(changes, e, nameof(ItemType.BaseTypeId), "Базовый тип", v => names.Type(v as Guid?));
            Changed(changes, e, nameof(ItemType.IconValue), "Иконка");
            Changed(changes, e, nameof(ItemType.Color), "Цвет");
            Changed(changes, e, nameof(ItemType.TitleTemplate), "Шаблон названия");
            Changed(changes, e, nameof(ItemType.DefaultUnit), "Единица по умолчанию");
            Changed(changes, e, nameof(ItemType.IsArchived), "В архиве");
        }

        foreach (var field in tracker.Entries<FieldDefinition>().Where(f => f.Entity.TypeId == type.Id && IsChanged(f)))
        {
            var label = $"Поле «{field.Entity.Name}»";
            var change = field.State switch
            {
                EntityState.Added => new AuditChange($"field:{field.Entity.Id}", label, null, Describe(field.Entity)),
                EntityState.Deleted => new AuditChange($"field:{field.Entity.Id}", label, Describe(field.Entity), null),
                _ => new AuditChange($"field:{field.Entity.Id}", label, Describe((FieldDefinition)field.OriginalValues.ToObject()), Describe(field.Entity)),
            };

            if (change.Old != change.New)
            {
                changes.Add(change);
            }
        }

        return new AuditEntry { EntityType = AuditEntityType.ItemType, EntityId = type.Id, Action = action, Summary = Limit(summary), Changes = changes };
    }

    private static AuditEntry ForList(ChangeTracker tracker, EntityEntry<ValueList> e)
    {
        var list = e.Entity;
        var changes = new List<AuditChange>();
        var (action, summary) = e.State switch
        {
            EntityState.Added => (AuditAction.Created, $"Создан справочник: {list.Name}"),
            EntityState.Deleted => (AuditAction.Deleted, $"Удалён справочник: {list.Name}"),
            _ => (AuditAction.Updated, $"Изменён справочник: {list.Name}"),
        };

        if (e.State == EntityState.Modified)
        {
            Changed(changes, e, nameof(ValueList.Name), "Название");
            Changed(changes, e, nameof(ValueList.SortMode), "Сортировка", v => v is ValueListSortMode.Alphabetical ? "по алфавиту" : "вручную");
        }

        static string Describe(ValueListOption o) => o.IsArchived ? $"{o.Value} (в архиве)" : o.Value;

        foreach (var option in tracker.Entries<ValueListOption>().Where(o => o.Entity.ListId == list.Id && IsChanged(o)))
        {
            var change = option.State switch
            {
                EntityState.Added => new AuditChange($"option:{option.Entity.Id}", "Вариант", null, Describe(option.Entity)),
                EntityState.Deleted => new AuditChange($"option:{option.Entity.Id}", "Вариант", Describe(option.Entity), null),
                _ => new AuditChange($"option:{option.Entity.Id}", "Вариант", Describe((ValueListOption)option.OriginalValues.ToObject()), Describe(option.Entity)),
            };

            if (change.Old != change.New)
            {
                changes.Add(change);
            }
        }

        return new AuditEntry { EntityType = AuditEntityType.ValueList, EntityId = list.Id, Action = action, Summary = Limit(summary), Changes = changes };
    }

    private static AuditEntry ForTag(EntityEntry<Tag> e)
    {
        var changes = new List<AuditChange>();
        Changed(changes, e, nameof(Tag.Name), "Название");
        Changed(changes, e, nameof(Tag.Color), "Цвет");
        var (action, summary) = e.State switch
        {
            EntityState.Added => (AuditAction.Created, $"Создан тег: #{e.Entity.Name}"),
            EntityState.Deleted => (AuditAction.Deleted, $"Удалён тег: #{e.Entity.Name}"),
            _ => (AuditAction.Updated, $"Изменён тег: #{e.Entity.Name}"),
        };

        return new AuditEntry { EntityType = AuditEntityType.Tag, EntityId = e.Entity.Id, Action = action, Summary = Limit(summary), Changes = changes };
    }

    /// <summary>Подключение, отзыв, смена роли и имени. Служебные поля (время последнего входа) в историю не пишутся.</summary>
    private static AuditEntry? ForDevice(EntityEntry<Device> e)
    {
        var device = e.Entity;
        if (e.State == EntityState.Added)
        {
            return new AuditEntry { EntityType = AuditEntityType.Device, EntityId = device.Id, Action = AuditAction.Created, Summary = Limit($"Подключено устройство: {device.Name}") };
        }

        if (e.State == EntityState.Deleted)
        {
            return new AuditEntry { EntityType = AuditEntityType.Device, EntityId = device.Id, Action = AuditAction.Purged, Summary = Limit($"Удалено устройство: {device.Name}") };
        }

        var changes = new List<AuditChange>();
        Changed(changes, e, nameof(Device.Name), "Название");
        Changed(changes, e, nameof(Device.Role), "Роль", v => v is DeviceRole r ? DeviceRoleName(r) : null);
        Changed(changes, e, nameof(Device.RevokedAt), "Доступ", v => v is null ? "разрешён" : "отозван");
        if (changes.Count == 0)
        {
            return null;
        }

        var revoked = e.Property(d => d.RevokedAt).OriginalValue is null && device.RevokedAt is not null;
        return new AuditEntry
        {
            EntityType = AuditEntityType.Device,
            EntityId = device.Id,
            Action = AuditAction.Updated,
            Summary = Limit(revoked ? $"Доступ отозван: {device.Name}" : $"Изменено устройство: {device.Name}"),
            Changes = changes,
        };
    }

    private static void Changed(List<AuditChange> changes, EntityEntry entry, string property, string label, Func<object?, string?>? format = null)
    {
        if (entry.State != EntityState.Modified)
        {
            return;
        }

        var p = entry.Property(property);
        if (!p.IsModified || Equals(p.OriginalValue, p.CurrentValue))
        {
            return;
        }

        format ??= Format;
        changes.Add(new AuditChange(property, label, Truncate(format(p.OriginalValue)), Truncate(format(p.CurrentValue))));
    }

    private static string? Format(object? value) => value switch
    {
        null => null,
        string s => string.IsNullOrWhiteSpace(s) ? null : s,
        bool b => b ? "Да" : "Нет",
        double d => d.ToString("0.######", Russian),
        DateTimeOffset t => t.ToString("dd.MM.yyyy HH:mm", Russian),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    private static string Describe(FieldDefinition f)
    {
        var parts = new List<string> { $"{f.Name}: {DataTypeName(f.DataType)}" };
        if (!string.IsNullOrWhiteSpace(f.Unit))
        {
            parts.Add(f.Unit);
        }

        if (f.IsRequired)
        {
            parts.Add("обязательное");
        }

        if (f.IsArchived)
        {
            parts.Add("в архиве");
        }

        return string.Join(", ", parts);
    }

    private static string? Truncate(string? text) => text is { Length: > MaxText } ? text[..(MaxText - 1)] + "…" : text;

    private static string Limit(string text) => text.Length > 500 ? text[..499] + "…" : text;

    internal static string RoleName(TypeRole role) => role switch
    {
        TypeRole.Container => "Контейнер",
        TypeRole.Place => "Место",
        _ => "Вещь",
    };

    internal static string DeviceRoleName(DeviceRole role) => role switch
    {
        DeviceRole.Admin => "Администратор",
        DeviceRole.Editor => "Редактор",
        _ => "Просмотр",
    };

    internal static string DataTypeName(FieldDataType type) => type switch
    {
        FieldDataType.LongText => "многострочный текст",
        FieldDataType.Integer => "целое число",
        FieldDataType.Decimal => "дробное число",
        FieldDataType.Boolean => "да/нет",
        FieldDataType.Date => "дата",
        FieldDataType.Choice => "список",
        FieldDataType.MultiChoice => "несколько из списка",
        FieldDataType.ItemRef => "ссылка на вещь",
        FieldDataType.Url => "ссылка (URL)",
        _ => "текст",
    };
}

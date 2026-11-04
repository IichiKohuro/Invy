using Invy.Domain.Audit;
using Invy.Domain.Devices;
using Invy.Domain.Files;
using Invy.Domain.Items;
using Invy.Domain.Lists;
using Invy.Domain.Tags;
using Invy.Domain.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Invy.Infrastructure.Persistence;

// Схема — см. PLAN.md, раздел 5.2. Guid хранятся как TEXT, время — INTEGER (Unix ms).

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> b)
    {
        b.ToTable("Items");
        b.Property(i => i.Version).IsConcurrencyToken();
        b.Property(i => i.Name).IsRequired().HasMaxLength(300);
        b.Property(i => i.NameNorm).IsRequired().HasMaxLength(300);
        b.Property(i => i.Unit).HasMaxLength(20);
        b.Property(i => i.IconValue).HasMaxLength(100);
        b.Property(i => i.Color).HasMaxLength(9);

        b.HasIndex(i => i.Number).IsUnique();
        b.HasIndex(i => new { i.ParentId, i.IsDeleted, i.NameNorm });
        b.HasIndex(i => i.TypeId);
        b.HasIndex(i => i.UpdatedAt);
        b.HasIndex(i => i.DeletionId);

        b.HasOne<ItemType>().WithMany().HasForeignKey(i => i.TypeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Item>().WithMany().HasForeignKey(i => i.ParentId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(i => i.Values).WithOne().HasForeignKey(v => v.ItemId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(i => i.Tags).WithOne().HasForeignKey(t => t.ItemId).OnDelete(DeleteBehavior.Cascade);

        // Корзина: удалённые вещи не видны обычным запросам; корзина и восстановление — через IgnoreQueryFilters().
        b.HasQueryFilter(i => !i.IsDeleted);
    }
}

internal sealed class ItemValueConfiguration : IEntityTypeConfiguration<ItemValue>
{
    public void Configure(EntityTypeBuilder<ItemValue> b)
    {
        b.ToTable("ItemValues");
        b.HasIndex(v => new { v.ItemId, v.FieldId, v.Ordinal }).IsUnique();
        b.HasIndex(v => new { v.FieldId, v.NumberValue });
        b.HasIndex(v => new { v.FieldId, v.OptionId });
        b.HasIndex(v => new { v.FieldId, v.TextValue });
        b.HasIndex(v => v.RefItemId);

        b.HasOne<FieldDefinition>().WithMany().HasForeignKey(v => v.FieldId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ValueListOption>().WithMany().HasForeignKey(v => v.OptionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Item>().WithMany().HasForeignKey(v => v.RefItemId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ItemTagConfiguration : IEntityTypeConfiguration<ItemTag>
{
    public void Configure(EntityTypeBuilder<ItemTag> b)
    {
        b.ToTable("ItemTags");
        b.HasKey(t => new { t.ItemId, t.TagId });
        b.HasIndex(t => t.TagId);
        b.HasOne<Tag>().WithMany().HasForeignKey(t => t.TagId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ItemTypeConfiguration : IEntityTypeConfiguration<ItemType>
{
    public void Configure(EntityTypeBuilder<ItemType> b)
    {
        b.ToTable("ItemTypes");
        b.Property(t => t.Version).IsConcurrencyToken();
        b.Property(t => t.Name).IsRequired().HasMaxLength(100);
        b.Property(t => t.NameNorm).IsRequired().HasMaxLength(100);
        b.Property(t => t.TitleTemplate).HasMaxLength(300);
        b.Property(t => t.DefaultUnit).HasMaxLength(20);
        b.HasIndex(t => t.NameNorm).IsUnique();
        b.HasOne<ItemType>().WithMany().HasForeignKey(t => t.BaseTypeId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(t => t.Fields).WithOne().HasForeignKey(f => f.TypeId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FieldDefinitionConfiguration : IEntityTypeConfiguration<FieldDefinition>
{
    public void Configure(EntityTypeBuilder<FieldDefinition> b)
    {
        b.ToTable("FieldDefinitions");
        b.Property(f => f.Name).IsRequired().HasMaxLength(100);
        b.Property(f => f.Unit).HasMaxLength(20);
        b.Property(f => f.GroupName).HasMaxLength(100);
        b.Property(f => f.Settings)
            .HasConversion(JsonColumn.Converter(() => FieldSettings.Empty), JsonColumn.Comparer<FieldSettings>())
            .IsRequired();
        b.HasIndex(f => new { f.TypeId, f.SortOrder });
        b.HasOne<ValueList>().WithMany().HasForeignKey(f => f.ValueListId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ValueListConfiguration : IEntityTypeConfiguration<ValueList>
{
    public void Configure(EntityTypeBuilder<ValueList> b)
    {
        b.ToTable("ValueLists");
        b.Property(l => l.Version).IsConcurrencyToken();
        b.Property(l => l.Name).IsRequired().HasMaxLength(100);
        b.Property(l => l.NameNorm).IsRequired().HasMaxLength(100);
        b.HasIndex(l => l.NameNorm);
        b.HasMany(l => l.Options).WithOne().HasForeignKey(o => o.ListId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ValueListOptionConfiguration : IEntityTypeConfiguration<ValueListOption>
{
    public void Configure(EntityTypeBuilder<ValueListOption> b)
    {
        b.ToTable("ValueListOptions");
        b.Property(o => o.Value).IsRequired().HasMaxLength(200);
        b.Property(o => o.ValueNorm).IsRequired().HasMaxLength(200);
        b.Property(o => o.Color).HasMaxLength(9);
        b.HasIndex(o => new { o.ListId, o.ValueNorm }).IsUnique();
    }
}

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> b)
    {
        b.ToTable("Tags");
        b.Property(t => t.Version).IsConcurrencyToken();
        b.Property(t => t.Name).IsRequired().HasMaxLength(100);
        b.Property(t => t.NameNorm).IsRequired().HasMaxLength(100);
        b.Property(t => t.Color).HasMaxLength(9);
        b.HasIndex(t => t.NameNorm).IsUnique();
    }
}

internal sealed class MediaFileConfiguration : IEntityTypeConfiguration<MediaFile>
{
    public void Configure(EntityTypeBuilder<MediaFile> b)
    {
        b.ToTable("Media");
        b.Property(m => m.Sha256).IsRequired().HasMaxLength(64);
        b.Property(m => m.ContentType).IsRequired().HasMaxLength(100);
        b.Property(m => m.FileName).IsRequired().HasMaxLength(260);
        b.HasIndex(m => m.Sha256);
        b.HasIndex(m => new { m.ItemId, m.SortOrder });
        b.HasOne<Item>().WithMany().HasForeignKey(m => m.ItemId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> b)
    {
        b.ToTable("Devices");
        b.Property(d => d.Name).IsRequired().HasMaxLength(100);
        b.Property(d => d.TokenHash).IsRequired().HasMaxLength(64);
        b.Property(d => d.AppVersion).HasMaxLength(50);
        b.HasIndex(d => d.TokenHash).IsUnique();
        b.Ignore(d => d.IsActive);
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.ToTable("AuditLog");
        b.Property(a => a.Summary).IsRequired().HasMaxLength(500);
        b.Property(a => a.Changes)
            .HasConversion(JsonColumn.Converter(() => new List<AuditChange>()), JsonColumn.Comparer<List<AuditChange>>())
            .IsRequired();
        b.HasIndex(a => new { a.EntityType, a.EntityId, a.Id });
        b.HasIndex(a => a.Timestamp);
        b.HasIndex(a => a.DeviceId);
    }
}

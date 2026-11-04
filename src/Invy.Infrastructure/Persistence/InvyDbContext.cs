using Invy.Application.Abstractions;
using Invy.Domain.Audit;
using Invy.Domain.Common;
using Invy.Domain.Devices;
using Invy.Domain.Files;
using Invy.Domain.Items;
using Invy.Domain.Lists;
using Invy.Domain.Tags;
using Invy.Domain.Types;
using Microsoft.EntityFrameworkCore;

namespace Invy.Infrastructure.Persistence;

/// <summary>
/// Контекст EF Core. При каждом сохранении: родительский агрегат «трогается» при изменении его частей,
/// проставляются версия/время/автор, новым вещам назначается инвентарный номер, пишется история изменений.
/// </summary>
public sealed class InvyDbContext : DbContext, IInventoryDbContext
{
    private readonly ICurrentActor _actor;
    private readonly TimeProvider _clock;
    private readonly List<AuditEntry> _savedAudit = [];

    public InvyDbContext(DbContextOptions<InvyDbContext> options, ICurrentActor? actor = null, TimeProvider? clock = null)
        : base(options)
    {
        _actor = actor ?? SystemActor.Instance;
        _clock = clock ?? TimeProvider.System;
    }

    public DbSet<Item> Items => Set<Item>();

    public DbSet<ItemValue> ItemValues => Set<ItemValue>();

    public DbSet<ItemTag> ItemTags => Set<ItemTag>();

    public DbSet<ItemType> ItemTypes => Set<ItemType>();

    public DbSet<FieldDefinition> FieldDefinitions => Set<FieldDefinition>();

    public DbSet<ValueList> ValueLists => Set<ValueList>();

    public DbSet<ValueListOption> ValueListOptions => Set<ValueListOption>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<MediaFile> Media => Set<MediaFile>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    /// <summary>Общий id записей истории следующего сохранения (пакетные операции).</summary>
    public Guid? CorrelationId { get; set; }

    /// <summary>Записи истории, сохранённые с прошлого вызова: их получают клиенты после коммита.</summary>
    public IReadOnlyList<AuditEntry> TakeSavedAudit()
    {
        var saved = _savedAudit.ToList();
        _savedAudit.Clear();
        return saved;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException("Используйте SaveChangesAsync: при сохранении выполняются асинхронные запросы для истории изменений.");

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        var now = _clock.GetUtcNow();

        await TouchParentAggregatesAsync(cancellationToken);
        StampAggregates(now);
        await AssignItemNumbersAsync(cancellationToken);

        var audit = await AuditBuilder.BuildAsync(this, cancellationToken);
        foreach (var entry in audit)
        {
            entry.Timestamp = now;
            entry.DeviceId = _actor.DeviceId;
            entry.CorrelationId = CorrelationId;
        }

        AuditLog.AddRange(audit);

        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        _savedAudit.AddRange(audit);
        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InvyDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UnixMillisecondsConverter>();

    /// <summary>Изменение значения, тега или поля — это изменение вещи (типа, справочника): версия агрегата растёт.</summary>
    private async Task TouchParentAggregatesAsync(CancellationToken cancellationToken)
    {
        var parents = new HashSet<(Type Type, Guid Id)>();
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            (Type, Guid)? parent = entry.Entity switch
            {
                ItemValue v => (typeof(Item), v.ItemId),
                ItemTag t => (typeof(Item), t.ItemId),
                MediaFile { ItemId: { } itemId } => (typeof(Item), itemId),
                FieldDefinition f => (typeof(ItemType), f.TypeId),
                ValueListOption o => (typeof(ValueList), o.ListId),
                _ => null,
            };

            if (parent is { } p)
            {
                parents.Add(p);
            }
        }

        foreach (var (type, id) in parents)
        {
            var root = ChangeTracker.Entries<AggregateRoot>().FirstOrDefault(e => e.Entity.Id == id && e.Entity.GetType() == type)
                ?? await LoadRootAsync(type, id, cancellationToken);

            if (root is { State: EntityState.Unchanged })
            {
                root.Property(r => r.UpdatedAt).IsModified = true;
            }
        }
    }

    private async Task<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<AggregateRoot>?> LoadRootAsync(Type type, Guid id, CancellationToken cancellationToken)
    {
        AggregateRoot? root = type == typeof(Item) ? await Items.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            : type == typeof(ItemType) ? await ItemTypes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            : await ValueLists.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        return root is null ? null : Entry(root);
    }

    private void StampAggregates(DateTimeOffset now)
    {
        foreach (var entry in ChangeTracker.Entries<AggregateRoot>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.CreatedBy = _actor.DeviceId;
                    entry.Entity.UpdatedBy = _actor.DeviceId;
                    entry.Entity.Version = 1;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = _actor.DeviceId;
                    entry.Entity.Version = entry.Property(e => e.Version).OriginalValue + 1;
                    break;
            }
        }

        foreach (var entry in ChangeTracker.Entries<MediaFile>().Where(e => e.State == EntityState.Added))
        {
            entry.Entity.CreatedAt = now;
            entry.Entity.CreatedBy ??= _actor.DeviceId;
        }

        foreach (var entry in ChangeTracker.Entries<Device>().Where(e => e.State == EntityState.Added && e.Entity.CreatedAt == default))
        {
            entry.Entity.CreatedAt = now;
        }
    }

    /// <summary>Номер = максимум + 1. Безопасно: запись в БД идёт строго по одной (<see cref="WriteGate"/>).</summary>
    private async Task AssignItemNumbersAsync(CancellationToken cancellationToken)
    {
        var added = ChangeTracker.Entries<Item>().Where(e => e.State == EntityState.Added && e.Entity.Number == 0).ToList();
        if (added.Count == 0)
        {
            return;
        }

        var max = await Items.IgnoreQueryFilters().MaxAsync(i => (int?)i.Number, cancellationToken) ?? 0;
        foreach (var entry in added)
        {
            entry.Entity.Number = ++max;
        }
    }
}

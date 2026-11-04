using Invy.Contracts.Sync;
using Invy.Domain.Audit;
using Invy.Domain.Devices;
using Invy.Domain.Files;
using Invy.Domain.Items;
using Invy.Domain.Lists;
using Invy.Domain.Tags;
using Invy.Domain.Types;
using Microsoft.EntityFrameworkCore;

namespace Invy.Application.Abstractions;

/// <summary>Данные для сценариев: LINQ через EF Core. Сохранение — только через <see cref="IUnitOfWork"/>.</summary>
public interface IInventoryDbContext
{
    DbSet<Item> Items { get; }

    DbSet<ItemValue> ItemValues { get; }

    DbSet<ItemTag> ItemTags { get; }

    DbSet<ItemType> ItemTypes { get; }

    DbSet<FieldDefinition> FieldDefinitions { get; }

    DbSet<ValueList> ValueLists { get; }

    DbSet<ValueListOption> ValueListOptions { get; }

    DbSet<Tag> Tags { get; }

    DbSet<MediaFile> Media { get; }

    DbSet<Device> Devices { get; }

    DbSet<AuditEntry> AuditLog { get; }
}

/// <summary>
/// Сохранение изменений одной операции: одна транзакция, записи истории, номер изменения (seq).
/// Уведомление клиентов (SignalR) отправляется только после успешного коммита.
/// </summary>
public interface IUnitOfWork
{
    /// <param name="correlationId">Общий id для записей истории пакетной операции («переместить 30 вещей»).</param>
    /// <exception cref="Errors.ConflictException">Запись изменена другим устройством.</exception>
    Task SaveAsync(Guid? correlationId = null, CancellationToken cancellationToken = default);
}

/// <summary>Рассылка клиентам сведений об изменениях (реализация — SignalR в Invy.Api).</summary>
public interface IChangeNotifier
{
    Task PublishAsync(ChangeBatch batch, CancellationToken cancellationToken = default);
}

/// <summary>Кто выполняет запрос: устройство и его роль. Пишется в историю изменений.</summary>
public interface ICurrentActor
{
    Guid? DeviceId { get; }

    DeviceRole Role { get; }
}

/// <summary>Действия системы (миграции, фоновые задачи, тесты): без устройства.</summary>
public sealed class SystemActor : ICurrentActor
{
    public static SystemActor Instance { get; } = new();

    public Guid? DeviceId => null;

    public DeviceRole Role => DeviceRole.Admin;
}

using Invy.Application.Abstractions;
using Invy.Contracts.Catalog;
using Invy.Contracts.Items;
using Invy.Contracts.Sync;
using Invy.Domain.Audit;
using Microsoft.EntityFrameworkCore;

namespace Invy.Application.History;

/// <summary>Журнал изменений и лента синхронизации (номер изменения = seq).</summary>
public sealed class HistoryService(IInventoryDbContext db)
{
    public const int MaxPageSize = 500;
    public const int MaxSyncBatch = 1000;

    public async Task<PagedResult<AuditEntryDto>> QueryAsync(
        ChangeEntityType? entityType,
        Guid? entityId,
        Guid? deviceId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = db.AuditLog.AsNoTracking();
        if (entityType is { } type)
        {
            query = query.Where(a => a.EntityType == (AuditEntityType)type);
        }

        if (entityId is { } id)
        {
            query = query.Where(a => a.EntityId == id);
        }

        if (deviceId is { } device)
        {
            query = query.Where(a => a.DeviceId == device);
        }

        if (from is { } start)
        {
            query = query.Where(a => a.Timestamp >= start);
        }

        if (to is { } end)
        {
            query = query.Where(a => a.Timestamp < end);
        }

        var total = await query.CountAsync(cancellationToken);
        var page = await query.OrderByDescending(a => a.Id).Skip(Math.Max(skip, 0)).Take(Math.Clamp(take, 1, MaxPageSize)).ToListAsync(cancellationToken);
        var deviceIds = page.Where(a => a.DeviceId is not null).Select(a => a.DeviceId!.Value).Distinct().ToList();
        var devices = await db.Devices.AsNoTracking().Where(d => deviceIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

        return new PagedResult<AuditEntryDto>(
            page.Select(a => new AuditEntryDto(
                a.Id, a.Timestamp, a.DeviceId, a.DeviceId is { } d ? devices.GetValueOrDefault(d) : null,
                (ChangeEntityType)a.EntityType, a.EntityId, (ChangeAction)a.Action, a.Summary,
                a.Changes.Select(c => new AuditChangeDto(c.Key, c.Label, c.Old, c.New)).ToList(),
                a.CorrelationId)).ToList(),
            total);
    }

    /// <summary>Изменения после <paramref name="since"/> по возрастанию seq — для догоняющей синхронизации клиентов.</summary>
    public async Task<SyncChangesResponse> GetChangesAsync(long since, int take, CancellationToken cancellationToken)
    {
        take = Math.Clamp(take, 1, MaxSyncBatch);
        var rows = await db.AuditLog.AsNoTracking()
            .Where(a => a.Id > since)
            .OrderBy(a => a.Id)
            .Take(take + 1)
            .ToListAsync(cancellationToken);
        var last = await db.AuditLog.MaxAsync(a => (long?)a.Id, cancellationToken) ?? 0;

        return new SyncChangesResponse(
            last,
            rows.Count > take,
            rows.Take(take).Select(a => new ChangeDto(a.Id, (ChangeEntityType)a.EntityType, a.EntityId, (ChangeAction)a.Action, a.ParentId, a.OldParentId)).ToList());
    }
}

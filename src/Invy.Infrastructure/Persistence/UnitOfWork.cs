using Invy.Application.Abstractions;
using Invy.Application.Errors;
using Invy.Contracts.Sync;
using Invy.Domain.Audit;
using Microsoft.EntityFrameworkCore;

namespace Invy.Infrastructure.Persistence;

/// <summary>
/// Запись в SQLite строго по одной операции за раз: у SQLite один писатель, а очередь в процессе
/// избавляет от SQLITE_BUSY и делает безопасным назначение номеров (максимум + 1).
/// </summary>
public sealed class WriteGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(_semaphore);
    }

    public void Dispose() => _semaphore.Dispose();

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}

internal sealed class UnitOfWork(InvyDbContext db, WriteGate gate, IChangeNotifier notifier) : IUnitOfWork
{
    public async Task SaveAsync(Guid? correlationId = null, CancellationToken cancellationToken = default)
    {
        db.CorrelationId = correlationId;
        try
        {
            using (await gate.EnterAsync(cancellationToken))
            {
                // Транзакция — задел на действия после сохранения (индекс FTS5 в фазе 3) в той же транзакции.
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException("Запись изменена на другом устройстве. Обновите данные и повторите.", ex.Entries.Count > 0 ? ex.Entries[0].Entity : null);
        }
        finally
        {
            db.CorrelationId = null;
        }

        var audit = db.TakeSavedAudit();
        if (audit.Count > 0)
        {
            // Уведомление — строго после коммита: клиенты не увидят незафиксированных данных.
            await notifier.PublishAsync(ToBatch(audit), CancellationToken.None);
        }
    }

    internal static ChangeBatch ToBatch(IReadOnlyList<AuditEntry> audit) => new(
        audit.Min(a => a.Id),
        audit.Max(a => a.Id),
        audit[0].DeviceId,
        audit[0].CorrelationId,
        audit.OrderBy(a => a.Id).Select(ToChange).ToList());

    internal static ChangeDto ToChange(AuditEntry a) =>
        new(a.Id, (ChangeEntityType)a.EntityType, a.EntityId, (ChangeAction)a.Action, a.ParentId, a.OldParentId);
}

/// <summary>Без SignalR (тесты, утилиты): изменения никуда не рассылаются.</summary>
internal sealed class NullChangeNotifier : IChangeNotifier
{
    public Task PublishAsync(ChangeBatch batch, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

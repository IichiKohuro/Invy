using System.Data.Common;
using Invy.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Invy.Infrastructure.Persistence;

/// <summary>Каждое соединение EF Core получает коллацию RU_NATURAL, функцию invy_norm() и PRAGMA (ADR 0001).</summary>
internal sealed class SqliteConnectionInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        SqliteConnectionSetup.Configure((SqliteConnection)connection);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        SqliteConnectionSetup.Configure((SqliteConnection)connection);
        return Task.CompletedTask;
    }
}

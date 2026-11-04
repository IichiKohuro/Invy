using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Invy.Infrastructure.Persistence;

/// <summary>Применение миграций при старте сервера. Перед изменением схемы существующей БД сохраняется её копия.</summary>
public static partial class DatabaseMigrator
{
    public static async Task MigrateAsync(IServiceProvider services, string backupDirectory, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InvyDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseMigrator));

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        if (applied.Count > 0)
        {
            Directory.CreateDirectory(backupDirectory);
            var backup = Path.Combine(backupDirectory, $"pre-migration-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");
            await db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", [backup], cancellationToken);
            LogBackup(logger, backup);
        }

        LogMigrating(logger, string.Join(", ", pending));
        await db.Database.MigrateAsync(cancellationToken);
    }

    [LoggerMessage(LogLevel.Information, "Копия БД перед миграцией: {Path}")]
    private static partial void LogBackup(ILogger logger, string path);

    [LoggerMessage(LogLevel.Information, "Применение миграций: {Migrations}")]
    private static partial void LogMigrating(ILogger logger, string migrations);
}

/// <summary>Для <c>dotnet ef migrations add</c>: контекст без DI и без реальной БД.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<InvyDbContext>
{
    public InvyDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<InvyDbContext>().UseSqlite("Data Source=design-time.db").Options);
}

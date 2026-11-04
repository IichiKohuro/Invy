using Invy.Application.Abstractions;
using Invy.Infrastructure.Media;
using Invy.Infrastructure.Persistence;
using Invy.Infrastructure.Queries;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Invy.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseFileName = "invy.db";

    /// <summary>SQLite в каталоге данных, EF Core для записи, Dapper для чтения.</summary>
    public static IServiceCollection AddInvyInfrastructure(this IServiceCollection services, string dataPath)
    {
        Directory.CreateDirectory(dataPath);
        DapperSetup.Register();

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataPath, DatabaseFileName),
            Pooling = true,
        }.ToString();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IChangeNotifier, NullChangeNotifier>();
        services.TryAddScoped<ICurrentActor>(_ => SystemActor.Instance);
        services.AddSingleton<WriteGate>();
        services.AddDbContext<InvyDbContext>(options => options
            .UseSqlite(connectionString)
            .AddInterceptors(new SqliteConnectionInterceptor()));
        services.AddScoped<IInventoryDbContext>(sp => sp.GetRequiredService<InvyDbContext>());
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IItemQueries, ItemQueries>();
        services.AddSingleton<IMediaStore>(new MediaStore(Path.Combine(dataPath, "media")));
        return services;
    }
}

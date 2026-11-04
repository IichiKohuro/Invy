using Invy.Application.Abstractions;
using Invy.Contracts.Sync;
using Invy.Domain.Items;
using Invy.Domain.Types;
using Invy.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Invy.Infrastructure.Tests.Persistence;

/// <summary>Отдельная БД во временном каталоге на каждый тест, миграции применены.</summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private TestDatabase(string dataPath, ServiceProvider services)
    {
        DataPath = dataPath;
        _services = services;
    }

    public string DataPath { get; }

    public RecordingNotifier Notifier => (RecordingNotifier)_services.GetRequiredService<IChangeNotifier>();

    public static async Task<TestDatabase> CreateAsync()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "invy-db-tests", Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton<IChangeNotifier, RecordingNotifier>()
            .AddInvyInfrastructure(dataPath)
            .BuildServiceProvider();

        await DatabaseMigrator.MigrateAsync(services, Path.Combine(dataPath, "backups"));
        return new TestDatabase(dataPath, services);
    }

    public Scope OpenScope() => new(_services.CreateAsyncScope());

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(DataPath, recursive: true);
    }

    internal sealed class Scope(AsyncServiceScope scope) : IAsyncDisposable
    {
        public InvyDbContext Db => scope.ServiceProvider.GetRequiredService<InvyDbContext>();

        public IUnitOfWork UnitOfWork => scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        public IItemQueries Queries => scope.ServiceProvider.GetRequiredService<IItemQueries>();

        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }

    internal sealed class RecordingNotifier : IChangeNotifier
    {
        public List<ChangeBatch> Batches { get; } = [];

        public Task PublishAsync(ChangeBatch batch, CancellationToken cancellationToken = default)
        {
            Batches.Add(batch);
            return Task.CompletedTask;
        }
    }
}

/// <summary>Тестовые данные: типы «Место», «Коробка», «Кабель» с характеристикой «Длина».</summary>
internal static class Seed
{
    public static ItemType Type(string name, TypeRole role, params FieldDefinition[] fields)
    {
        var type = new ItemType { Role = role };
        type.Rename(name);
        foreach (var field in fields)
        {
            field.TypeId = type.Id;
            type.Fields.Add(field);
        }

        return type;
    }

    public static Item Item(ItemType type, string name, Item? parent = null)
    {
        var item = new Item { TypeId = type.Id, ParentId = parent?.Id };
        item.Rename(name);
        return item;
    }
}

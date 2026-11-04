using System.Data.Common;
using Dapper;
using Invy.Application.Abstractions;
using Invy.Domain.Items;
using Invy.Infrastructure.Persistence;
using Invy.Infrastructure.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Invy.Infrastructure.Queries;

/// <summary>
/// Запросы дерева через Dapper на соединении EF Core: соединение открывается через EF,
/// поэтому получает коллацию RU_NATURAL и функцию invy_norm() (ADR 0001), а внутри транзакции — её же.
/// </summary>
internal sealed class ItemQueries(InvyDbContext db) : IItemQueries
{
    private const int MaxDepth = TreeRules.MaxDepth;

    private const string RowColumns = """
        i.Id, i.Number, i.Name, i.TypeId, t.Name AS TypeName, t.Role, i.ParentId, i.Quantity, i.Unit,
        COALESCE(i.IconKind, t.IconKind) AS IconKind, COALESCE(i.IconValue, t.IconValue) AS IconValue,
        COALESCE(i.Color, t.Color) AS Color, i.CoverMediaId,
        (SELECT count(*) FROM Items c WHERE c.ParentId = i.Id AND c.IsDeleted = 0) AS ChildCount,
        i.Version, i.UpdatedAt
        """;

    public Task<IReadOnlyList<PathSegment>> GetPathAsync(Guid itemId, CancellationToken cancellationToken) =>
        QueryAsync<PathSegment>(
            $"""
            WITH RECURSIVE path(Id, ParentId, Name, Lvl) AS (
                SELECT Id, ParentId, Name, 0 FROM Items WHERE Id = @itemId
                UNION ALL
                SELECT i.Id, i.ParentId, i.Name, p.Lvl + 1 FROM Items i JOIN path p ON i.Id = p.ParentId WHERE p.Lvl < {MaxDepth}
            )
            SELECT Id, Name FROM path WHERE Lvl > 0 ORDER BY Lvl DESC
            """,
            new { itemId },
            cancellationToken);

    public Task<ItemCounts> GetCountsAsync(Guid itemId, CancellationToken cancellationToken) =>
        WithConnectionAsync(async (connection, transaction) =>
        {
            var row = await connection.QuerySingleAsync<(long Direct, long Total)>(new CommandDefinition(
                $"""
                WITH RECURSIVE sub(Id, Depth) AS (
                    SELECT Id, 0 FROM Items WHERE Id = @itemId
                    UNION ALL
                    SELECT i.Id, s.Depth + 1 FROM Items i JOIN sub s ON i.ParentId = s.Id WHERE i.IsDeleted = 0 AND s.Depth < {MaxDepth}
                )
                SELECT (SELECT count(*) FROM Items WHERE ParentId = @itemId AND IsDeleted = 0) AS Direct,
                       (SELECT count(*) - 1 FROM sub) AS Total
                """,
                new { itemId },
                transaction,
                cancellationToken: cancellationToken));
            return new ItemCounts((int)row.Direct, (int)Math.Max(row.Total, 0));
        });

    public Task<Page<ItemRow>> GetChildrenAsync(Guid? parentId, ChildrenScope scope, ItemSort sort, bool descending, int skip, int take, CancellationToken cancellationToken)
    {
        var where = scope switch
        {
            ChildrenScope.RootPlaces => $"i.ParentId IS NULL AND t.Role = {(int)TypeRole.Place}",
            ChildrenScope.Unsorted => $"i.ParentId IS NULL AND t.Role <> {(int)TypeRole.Place}",
            _ => "i.ParentId = @parentId",
        };

        var direction = descending ? "DESC" : "ASC";
        var order = sort switch
        {
            ItemSort.Updated => $"i.UpdatedAt {direction}",
            ItemSort.Created => $"i.CreatedAt {direction}",
            ItemSort.Number => $"i.Number {direction}",
            ItemSort.Type => $"t.Name COLLATE {SqliteConnectionSetup.NaturalCollation} {direction}, i.Name COLLATE {SqliteConnectionSetup.NaturalCollation}",
            _ => $"i.Name COLLATE {SqliteConnectionSetup.NaturalCollation} {direction}",
        };

        // Как в Проводнике: контейнеры и места — выше вещей.
        var sql = $"""
            SELECT {RowColumns}
            FROM Items i JOIN ItemTypes t ON t.Id = i.TypeId
            WHERE i.IsDeleted = 0 AND {where}
            ORDER BY CASE WHEN t.Role = {(int)TypeRole.Item} THEN 1 ELSE 0 END, {order}, i.Number
            LIMIT @take OFFSET @skip;

            SELECT count(*) FROM Items i JOIN ItemTypes t ON t.Id = i.TypeId WHERE i.IsDeleted = 0 AND {where};
            """;

        return WithConnectionAsync(async (connection, transaction) =>
        {
            using var grid = await connection.QueryMultipleAsync(new CommandDefinition(sql, new { parentId, skip, take }, transaction, cancellationToken: cancellationToken));
            var rows = (await grid.ReadAsync<ItemRow>()).ToList();
            var total = await grid.ReadSingleAsync<long>();
            return new Page<ItemRow>(rows, (int)total);
        });
    }

    public Task<IReadOnlyList<TreeNodeRow>> GetTreeAsync(Guid? parentId, CancellationToken cancellationToken) =>
        QueryAsync<TreeNodeRow>(
            $"""
            SELECT i.Id, i.Name, t.Role,
                   COALESCE(i.IconKind, t.IconKind) AS IconKind, COALESCE(i.IconValue, t.IconValue) AS IconValue, COALESCE(i.Color, t.Color) AS Color,
                   EXISTS (SELECT 1 FROM Items c JOIN ItemTypes ct ON ct.Id = c.TypeId
                           WHERE c.ParentId = i.Id AND c.IsDeleted = 0 AND ct.Role <> {(int)TypeRole.Item}) AS HasChildren
            FROM Items i JOIN ItemTypes t ON t.Id = i.TypeId
            WHERE i.IsDeleted = 0 AND t.Role <> {(int)TypeRole.Item}
              AND {(parentId is null ? $"i.ParentId IS NULL AND t.Role = {(int)TypeRole.Place}" : "i.ParentId = @parentId")}
            ORDER BY i.Name COLLATE {SqliteConnectionSetup.NaturalCollation}
            """,
            new { parentId },
            cancellationToken);

    public Task<IReadOnlyList<Guid>> GetDescendantIdsAsync(Guid rootId, bool includeDeleted, CancellationToken cancellationToken) =>
        QueryAsync<Guid>(
            $"""
            WITH RECURSIVE sub(Id, Depth) AS (
                SELECT Id, 0 FROM Items WHERE Id = @rootId
                UNION ALL
                SELECT i.Id, s.Depth + 1 FROM Items i JOIN sub s ON i.ParentId = s.Id
                WHERE s.Depth < {MaxDepth} {(includeDeleted ? string.Empty : "AND i.IsDeleted = 0")}
            )
            SELECT Id FROM sub WHERE Depth > 0
            """,
            new { rootId },
            cancellationToken);

    public Task<bool> IsSelfOrDescendantAsync(Guid candidateId, Guid rootId, CancellationToken cancellationToken) =>
        WithConnectionAsync(async (connection, transaction) => await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            $"""
            WITH RECURSIVE up(Id, ParentId, Lvl) AS (
                SELECT Id, ParentId, 0 FROM Items WHERE Id = @candidateId
                UNION ALL
                SELECT i.Id, i.ParentId, u.Lvl + 1 FROM Items i JOIN up u ON i.Id = u.ParentId WHERE u.Lvl < {MaxDepth}
            )
            SELECT EXISTS (SELECT 1 FROM up WHERE Id = @rootId)
            """,
            new { candidateId, rootId },
            transaction,
            cancellationToken: cancellationToken)) == 1);

    public Task<IReadOnlyList<string>> SuggestTextValuesAsync(Guid fieldId, string prefix, int take, CancellationToken cancellationToken) =>
        QueryAsync<string>(
            $"""
            SELECT v.TextValue FROM ItemValues v JOIN Items i ON i.Id = v.ItemId
            WHERE v.FieldId = @fieldId AND v.TextValue IS NOT NULL AND i.IsDeleted = 0
              AND {SqliteConnectionSetup.NormalizeFunction}(v.TextValue) LIKE @pattern ESCAPE '\'
            GROUP BY {SqliteConnectionSetup.NormalizeFunction}(v.TextValue)
            ORDER BY count(*) DESC, v.TextValue COLLATE {SqliteConnectionSetup.NaturalCollation}
            LIMIT @take
            """,
            new { fieldId, take, pattern = EscapeLike(Domain.Text.TextNormalizer.Normalize(prefix)) + "%" },
            cancellationToken);

    private static string EscapeLike(string text) => text.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);

    private Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object parameters, CancellationToken cancellationToken) =>
        WithConnectionAsync<IReadOnlyList<T>>(async (connection, transaction) =>
            (await connection.QueryAsync<T>(new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken))).ToList());

    private async Task<T> WithConnectionAsync<T>(Func<DbConnection, DbTransaction?, Task<T>> query)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            return await query(db.Database.GetDbConnection(), db.Database.CurrentTransaction?.GetDbTransaction());
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}

using Invy.Domain.Text;
using Microsoft.Data.Sqlite;

namespace Invy.Infrastructure.Sqlite;

/// <summary>
/// Настройка каждого открытого соединения SQLite — и для EF Core (через интерсептор), и для Dapper.
/// </summary>
public static class SqliteConnectionSetup
{
    /// <summary>Коллация естественной сортировки без учёта регистра и «ё»: <c>ORDER BY Name COLLATE RU_NATURAL</c>.</summary>
    public const string NaturalCollation = "RU_NATURAL";

    /// <summary>SQL-функция нормализации текста: <c>invy_norm(Name)</c> = <see cref="TextNormalizer.Normalize"/>.</summary>
    public const string NormalizeFunction = "invy_norm";

    private const string Pragmas = """
        PRAGMA journal_mode = WAL;
        PRAGMA foreign_keys = ON;
        PRAGMA busy_timeout = 5000;
        PRAGMA synchronous = NORMAL;
        """;

    public static void Configure(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        connection.CreateCollation(NaturalCollation, static (x, y) => NaturalStringComparer.Instance.Compare(x, y));
        connection.CreateFunction<string?, string>(NormalizeFunction, TextNormalizer.Normalize, isDeterministic: true);

        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }
}

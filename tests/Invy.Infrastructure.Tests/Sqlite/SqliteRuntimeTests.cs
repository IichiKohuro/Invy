using Microsoft.Data.Sqlite;

namespace Invy.Infrastructure.Tests.Sqlite;

public class SqliteRuntimeTests
{
    [Fact]
    public void Bundled_sqlite_has_fts5_and_is_recent()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT sqlite_compileoption_used('ENABLE_FTS5')";
        ((long)command.ExecuteScalar()!).ShouldBe(1);

        // VACUUM INTO (бэкапы) — с 3.27, встроенный JSON — с 3.38.
        command.CommandText = "SELECT sqlite_version()";
        var version = Version.Parse((string)command.ExecuteScalar()!);
        version.ShouldBeGreaterThanOrEqualTo(new Version(3, 38));
    }
}

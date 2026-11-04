using Dapper;
using Invy.Domain.Text;
using Invy.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;

namespace Invy.Infrastructure.Tests.Sqlite;

/// <summary>
/// Спайк фазы 0: FTS5 + кириллица на той сборке SQLite, что поставляется с Microsoft.Data.Sqlite.
/// Тесты фиксируют и поведение самого SQLite, и решения Invy (нормализация, коллация).
/// </summary>
public sealed class CyrillicSearchTests : IDisposable
{
    private const string CreateFts = """
        CREATE VIRTUAL TABLE ItemsFts USING fts5(
            Name, Tags, TypeName, Fields, Description, Location,
            tokenize = 'unicode61 remove_diacritics 2',
            prefix = '2 3 4'
        );
        """;

    private const string Bm25 = "bm25(ItemsFts, 10.0, 6.0, 4.0, 2.0, 1.0, 0.5)";

    private readonly SqliteConnection _db;

    public CyrillicSearchTests()
    {
        _db = new SqliteConnection("Data Source=:memory:");
        _db.Open();
        SqliteConnectionSetup.Configure(_db);
        _db.Execute(CreateFts);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Builtin_sqlite_text_functions_do_not_understand_cyrillic()
    {
        // Поэтому Invy хранит нормализованные колонки и нормализует запросы в .NET.
        _db.ExecuteScalar<string>("SELECT lower('КАБЕЛЬ')").ShouldBe("КАБЕЛЬ");
        _db.ExecuteScalar<long>("SELECT 'Кабель' LIKE 'кабель'").ShouldBe(0);
        _db.ExecuteScalar<long>("SELECT 'Кабель' = 'кабель' COLLATE NOCASE").ShouldBe(0);
    }

    [Fact]
    public void Unicode61_folds_cyrillic_case_but_not_yo()
    {
        Index(1, name: "Кабель USB");
        Index(2, name: "Ёлочная гирлянда");

        MatchRaw("\"КАБЕЛЬ\"").ShouldBe([1]);
        MatchRaw("\"ёлочная\"").ShouldBe([2]);
        MatchRaw("\"елочная\"").ShouldBeEmpty(); // remove_diacritics работает только для латиницы
    }

    [Fact]
    public void Yo_is_found_both_ways_after_normalization()
    {
        IndexNormalized(1, name: "Ёлочная гирлянда");
        IndexNormalized(2, name: "Елка искусственная");

        Search("ёлочная").ShouldBe([1]);
        Search("ЕЛОЧНАЯ").ShouldBe([1]);
        Search("ёлка").ShouldBe([2]);
    }

    [Fact]
    public void Prefix_search_finds_word_forms()
    {
        IndexNormalized(1, name: "Кабели HDMI");
        IndexNormalized(2, name: "Кабель USB-C");
        IndexNormalized(3, name: "Кабельный канал");
        IndexNormalized(4, name: "Переходник");

        Search("кабел").ShouldBe([1, 2, 3], ignoreOrder: true);
        Search("КаБ").ShouldBe([1, 2, 3], ignoreOrder: true);
    }

    [Fact]
    public void Hyphenated_word_is_matched_as_phrase()
    {
        IndexNormalized(1, name: "Переходник USB-C на HDMI");
        IndexNormalized(2, name: "USB удлинитель, разъём C-типа");

        Search("usb-c").ShouldBe([1]);
        Search("usb").ShouldBe([1, 2], ignoreOrder: true);
    }

    [Fact]
    public void All_words_are_required_across_columns()
    {
        IndexNormalized(1, name: "Кабель", fields: "Версия 3.0 Разъём Type-C", location: "Чердак Серая коробка");
        IndexNormalized(2, name: "Кабель", fields: "Версия 2.0", location: "Сарай");

        Search("кабель 3.0").ShouldBe([1]);
        Search("кабель серая").ShouldBe([1]);
        Search("кабель сарай").ShouldBe([2]);
    }

    [Fact]
    public void Name_matches_rank_above_description_and_location_matches()
    {
        IndexNormalized(1, name: "Зарядка", description: "кабель в комплекте");
        IndexNormalized(2, name: "Кабель USB");
        IndexNormalized(3, name: "Пакет", location: "Коробка с кабелями");

        Search("кабел").ShouldBe([2, 1, 3]);
    }

    [Fact]
    public void Highlight_returns_text_close_to_original()
    {
        IndexNormalized(1, name: "Кабель USB-C, ёмкий чехол");

        var highlighted = _db.ExecuteScalar<string>(
            "SELECT highlight(ItemsFts, 0, '[', ']') FROM ItemsFts WHERE ItemsFts MATCH @q",
            new { q = FtsQuery.ForPrefixTerms("кабель емк") });

        highlighted.ShouldBe("[Кабель] USB-C, [емкий] чехол");
    }

    [Fact]
    public void Natural_collation_and_normalize_function_work_in_sql()
    {
        _db.Execute("CREATE TABLE Items (Name TEXT NOT NULL)");
        foreach (var name in new[] { "Коробка 10", "коробка 2", "Ёлка", "Антенна", "КОРОБКА 1" })
        {
            _db.Execute("INSERT INTO Items (Name) VALUES (@name)", new { name });
        }

        _db.Query<string>($"SELECT Name FROM Items ORDER BY Name COLLATE {SqliteConnectionSetup.NaturalCollation}")
            .ShouldBe(["Антенна", "Ёлка", "КОРОБКА 1", "коробка 2", "Коробка 10"]);

        _db.ExecuteScalar<string>($"SELECT {SqliteConnectionSetup.NormalizeFunction}('  ЁЛКА  Зелёная ')").ShouldBe("елка зеленая");
        _db.ExecuteScalar<long>(
                $"SELECT count(*) FROM Items WHERE {SqliteConnectionSetup.NormalizeFunction}(Name) = {SqliteConnectionSetup.NormalizeFunction}(@q)",
                new { q = "коробка 2" })
            .ShouldBe(1);
    }

    [Fact]
    public void File_database_gets_wal_and_foreign_keys()
    {
        var path = Path.Combine(Path.GetTempPath(), $"invy-{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                SqliteConnectionSetup.Configure(connection);

                connection.ExecuteScalar<string>("PRAGMA journal_mode").ShouldBe("wal");
                connection.ExecuteScalar<long>("PRAGMA foreign_keys").ShouldBe(1);
                connection.ExecuteScalar<long>("PRAGMA busy_timeout").ShouldBe(5000);
            }
        }
        finally
        {
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            {
                File.Delete(file);
            }
        }
    }

    private void Index(long rowId, string name) =>
        _db.Execute("INSERT INTO ItemsFts (rowid, Name) VALUES (@rowId, @name)", new { rowId, name });

    private void IndexNormalized(long rowId, string name, string? fields = null, string? description = null, string? location = null) =>
        _db.Execute(
            "INSERT INTO ItemsFts (rowid, Name, Fields, Description, Location) VALUES (@rowId, @name, @fields, @description, @location)",
            new
            {
                rowId,
                name = TextNormalizer.ForFullTextIndex(name),
                fields = TextNormalizer.ForFullTextIndex(fields),
                description = TextNormalizer.ForFullTextIndex(description),
                location = TextNormalizer.ForFullTextIndex(location),
            });

    private long[] MatchRaw(string ftsExpression) =>
        _db.Query<long>("SELECT rowid FROM ItemsFts WHERE ItemsFts MATCH @q ORDER BY rowid", new { q = ftsExpression }).ToArray();

    private long[] Search(string userInput) =>
        _db.Query<long>($"SELECT rowid FROM ItemsFts WHERE ItemsFts MATCH @q ORDER BY {Bm25}", new { q = FtsQuery.ForPrefixTerms(userInput) })
            .ToArray();
}

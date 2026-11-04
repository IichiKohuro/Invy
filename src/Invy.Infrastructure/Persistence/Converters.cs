using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Invy.Infrastructure.Persistence;

/// <summary>
/// Время — INTEGER (Unix ms, UTC): сортируется и сравнивается в SQL, в отличие от DateTimeOffset-текста,
/// с которым у провайдера SQLite ограничения.
/// </summary>
public sealed class UnixMillisecondsConverter() : ValueConverter<DateTimeOffset, long>(
    value => value.ToUnixTimeMilliseconds(),
    value => DateTimeOffset.FromUnixTimeMilliseconds(value));

/// <summary>Небольшие структуры (настройки поля, изменения в истории) хранятся JSON-текстом.</summary>
internal static class JsonColumn
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static ValueConverter<T, string> Converter<T>(Func<T> empty) => new(
        value => JsonSerializer.Serialize(value, Options),
        json => string.IsNullOrEmpty(json) ? empty() : JsonSerializer.Deserialize<T>(json, Options) ?? empty());

    /// <summary>Сравнение по содержимому — EF Core замечает изменения внутри JSON-значений.</summary>
    public static ValueComparer<T> Comparer<T>() => new(
        (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
        value => JsonSerializer.Serialize(value, Options).GetHashCode(StringComparison.Ordinal),
        value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!);
}

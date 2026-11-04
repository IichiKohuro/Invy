using System.Data;
using System.Globalization;
using Dapper;

namespace Invy.Infrastructure.Queries;

/// <summary>
/// Обработчики типов Dapper под формат хранения EF Core в SQLite:
/// Guid — TEXT, время — INTEGER (Unix ms), DateOnly — TEXT «yyyy-MM-dd».
/// </summary>
internal static class DapperSetup
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        SqlMapper.AddTypeHandler(new GuidHandler());
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
        SqlMapper.AddTypeHandler(new DateOnlyHandler());

        // Без этого Dapper обходит обработчики для «известных» типов (рекомендация Microsoft.Data.Sqlite).
        SqlMapper.RemoveTypeMap(typeof(Guid));
        SqlMapper.RemoveTypeMap(typeof(Guid?));
        SqlMapper.RemoveTypeMap(typeof(DateTimeOffset));
        SqlMapper.RemoveTypeMap(typeof(DateTimeOffset?));
    }

    private sealed class GuidHandler : SqlMapper.TypeHandler<Guid>
    {
        // Параметр передаём как Guid: Microsoft.Data.Sqlite запишет его в том же текстовом виде, что и EF Core.
        public override void SetValue(IDbDataParameter parameter, Guid value) => parameter.Value = value;

        public override Guid Parse(object value) => value switch
        {
            string text => Guid.Parse(text),
            byte[] bytes => new Guid(bytes),
            _ => (Guid)value,
        };
    }

    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) => parameter.Value = value.ToUnixTimeMilliseconds();

        public override DateTimeOffset Parse(object value) => DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(value, CultureInfo.InvariantCulture));
    }

    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(IDbDataParameter parameter, DateOnly value) => parameter.Value = value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public override DateOnly Parse(object value) => DateOnly.ParseExact((string)value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}

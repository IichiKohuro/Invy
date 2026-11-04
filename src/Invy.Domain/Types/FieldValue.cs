using System.Globalization;
using System.Text.RegularExpressions;
using Invy.Domain.Items;

namespace Invy.Domain.Types;

/// <summary>Значение характеристики в удобном виде (одно на поле; для множественного выбора — список вариантов).</summary>
public sealed record FieldValue(
    Guid FieldId,
    string? Text = null,
    double? Number = null,
    DateOnly? Date = null,
    bool? Bool = null,
    IReadOnlyList<Guid>? OptionIds = null,
    Guid? ItemId = null)
{
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Text) && Number is null && Date is null && Bool is null && (OptionIds is null || OptionIds.Count == 0) && ItemId is null;

    /// <summary>Строки <see cref="ItemValue"/> для хранения.</summary>
    public IEnumerable<ItemValue> ToRows(Guid itemId, FieldDefinition field)
    {
        if (IsEmpty)
        {
            yield break;
        }

        switch (field.DataType)
        {
            case FieldDataType.Choice or FieldDataType.MultiChoice:
                var ordinal = 0;
                foreach (var optionId in OptionIds!.Distinct())
                {
                    yield return new ItemValue { ItemId = itemId, FieldId = field.Id, Ordinal = ordinal++, OptionId = optionId };
                }

                break;
            case FieldDataType.Integer or FieldDataType.Decimal:
                var number = field.Settings.Decimals is { } decimals && field.DataType == FieldDataType.Decimal ? Math.Round(Number!.Value, decimals) : Number;
                yield return new ItemValue { ItemId = itemId, FieldId = field.Id, NumberValue = number };
                break;
            case FieldDataType.Boolean:
                yield return new ItemValue { ItemId = itemId, FieldId = field.Id, BoolValue = Bool };
                break;
            case FieldDataType.Date:
                yield return new ItemValue { ItemId = itemId, FieldId = field.Id, DateValue = Date };
                break;
            case FieldDataType.ItemRef:
                yield return new ItemValue { ItemId = itemId, FieldId = field.Id, RefItemId = ItemId };
                break;
            default:
                yield return new ItemValue { ItemId = itemId, FieldId = field.Id, TextValue = Text!.Trim() };
                break;
        }
    }

    /// <summary>Собирает значение поля из сохранённых строк.</summary>
    public static FieldValue FromRows(FieldDefinition field, IEnumerable<ItemValue> rows)
    {
        var list = rows.Where(r => r.FieldId == field.Id).OrderBy(r => r.Ordinal).ToList();
        var first = list.FirstOrDefault();
        return field.DataType switch
        {
            FieldDataType.Choice or FieldDataType.MultiChoice => new FieldValue(field.Id, OptionIds: list.Where(r => r.OptionId is not null).Select(r => r.OptionId!.Value).ToList()),
            FieldDataType.Integer or FieldDataType.Decimal => new FieldValue(field.Id, Number: first?.NumberValue),
            FieldDataType.Boolean => new FieldValue(field.Id, Bool: first?.BoolValue),
            FieldDataType.Date => new FieldValue(field.Id, Date: first?.DateValue),
            FieldDataType.ItemRef => new FieldValue(field.Id, ItemId: first?.RefItemId),
            _ => new FieldValue(field.Id, Text: first?.TextValue),
        };
    }
}

/// <summary>Проверка значения характеристики по её типу данных и ограничениям.</summary>
public static class FieldValueValidator
{
    public const int DefaultTextMaxLength = 500;
    public const int DefaultLongTextMaxLength = 10_000;

    /// <param name="field">Характеристика.</param>
    /// <param name="value">Значение или <c>null</c>, если не задано.</param>
    /// <param name="validOptionIds">Действующие варианты справочника поля (для списков).</param>
    /// <returns>Ошибки для пользователя; пусто — значение корректно.</returns>
    public static IReadOnlyList<string> Validate(FieldDefinition field, FieldValue? value, IReadOnlySet<Guid> validOptionIds)
    {
        var name = $"«{field.Name}»";
        if (value is null || value.IsEmpty)
        {
            return field.IsRequired ? [$"{name}: обязательное поле"] : [];
        }

        var errors = new List<string>();
        var settings = field.Settings;
        switch (field.DataType)
        {
            case FieldDataType.Text or FieldDataType.LongText or FieldDataType.Url:
                if (value.Text is null)
                {
                    errors.Add($"{name}: ожидается текст");
                    break;
                }

                var text = value.Text.Trim();
                var maxLength = settings.MaxLength ?? (field.DataType == FieldDataType.LongText ? DefaultLongTextMaxLength : DefaultTextMaxLength);
                if (text.Length > maxLength)
                {
                    errors.Add($"{name}: не длиннее {maxLength} символов");
                }

                if (field.DataType == FieldDataType.Url
                    && !(Uri.TryCreate(text, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)))
                {
                    errors.Add($"{name}: ожидается ссылка вида https://…");
                }

                if (!string.IsNullOrEmpty(settings.Pattern) && !MatchesPattern(text, settings.Pattern))
                {
                    errors.Add($"{name}: значение не соответствует шаблону");
                }

                break;

            case FieldDataType.Integer or FieldDataType.Decimal:
                if (value.Number is not { } number || double.IsNaN(number) || double.IsInfinity(number))
                {
                    errors.Add($"{name}: ожидается число");
                    break;
                }

                if (field.DataType == FieldDataType.Integer && Math.Abs(number - Math.Round(number)) > 1e-9)
                {
                    errors.Add($"{name}: ожидается целое число");
                }

                if (settings.Min is { } min && number < min)
                {
                    errors.Add($"{name}: не меньше {min.ToString(CultureInfo.InvariantCulture)}");
                }

                if (settings.Max is { } max && number > max)
                {
                    errors.Add($"{name}: не больше {max.ToString(CultureInfo.InvariantCulture)}");
                }

                break;

            case FieldDataType.Boolean when value.Bool is null:
                errors.Add($"{name}: ожидается «да» или «нет»");
                break;

            case FieldDataType.Date when value.Date is null:
                errors.Add($"{name}: ожидается дата");
                break;

            case FieldDataType.Choice or FieldDataType.MultiChoice:
                var options = value.OptionIds ?? [];
                if (options.Count == 0)
                {
                    errors.Add($"{name}: выберите вариант из списка");
                }
                else if (field.DataType == FieldDataType.Choice && options.Distinct().Count() > 1)
                {
                    errors.Add($"{name}: можно выбрать только один вариант");
                }
                else if (options.Any(id => !validOptionIds.Contains(id)))
                {
                    errors.Add($"{name}: вариант не из справочника поля");
                }

                break;

            case FieldDataType.ItemRef when value.ItemId is null:
                errors.Add($"{name}: выберите вещь");
                break;
        }

        return errors;
    }

    /// <summary>Корректен ли шаблон (regex) поля — проверяется при сохранении типа.</summary>
    public static bool IsValidPattern(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool MatchesPattern(string text, string pattern)
    {
        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return false;
        }
    }
}

using System.Globalization;
using System.Text.RegularExpressions;
using Invy.Domain.Text;

namespace Invy.Domain.Types;

/// <summary>Значение характеристики в виде текста: для истории изменений, авто-названий и списков.</summary>
public static class FieldValueFormatter
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    /// <param name="field">Характеристика.</param>
    /// <param name="value">Значение.</param>
    /// <param name="optionName">Текст варианта справочника по id.</param>
    /// <param name="itemName">Название вещи по id (для ссылок).</param>
    /// <param name="includeUnit">Добавить единицу измерения («100 см»).</param>
    public static string? Format(FieldDefinition field, FieldValue? value, Func<Guid, string?> optionName, Func<Guid, string?> itemName, bool includeUnit = true)
    {
        if (value is null || value.IsEmpty)
        {
            return null;
        }

        var text = field.DataType switch
        {
            FieldDataType.Integer or FieldDataType.Decimal when value.Number is { } n => FormatNumber(n, field),
            FieldDataType.Boolean when value.Bool is { } b => b ? "Да" : "Нет",
            FieldDataType.Date when value.Date is { } d => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            FieldDataType.Choice or FieldDataType.MultiChoice => string.Join(", ", (value.OptionIds ?? []).Select(id => optionName(id) ?? "?")),
            FieldDataType.ItemRef when value.ItemId is { } id => itemName(id) ?? "?",
            _ => value.Text?.Trim(),
        };

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return includeUnit && !string.IsNullOrWhiteSpace(field.Unit) && field.DataType is FieldDataType.Integer or FieldDataType.Decimal
            ? $"{text} {field.Unit.Trim()}"
            : text;
    }

    private static string FormatNumber(double number, FieldDefinition field)
    {
        var decimals = field.DataType == FieldDataType.Integer ? 0 : field.Settings.Decimals ?? 6;
        return Math.Round(number, decimals).ToString("0." + new string('#', Math.Max(decimals, 1)), Russian);
    }
}

/// <summary>
/// Шаблон авто-названия вещи: «{Разъём 1} → {Разъём 2}, {Длина} см».
/// Имена полей сравниваются без учёта регистра и «ё». Пустое значение выпадает вместе со своим «хвостом»
/// до ближайшего разделителя («{Длина} см» → ничего), повторяющиеся разделители схлопываются.
/// </summary>
public static partial class TitleTemplate
{
    private const string TrimChars = " ,;:–—-→/|";

    /// <returns>Название или <c>null</c>, если ни одно поле из шаблона не заполнено.</returns>
    public static string? Render(string? template, IReadOnlyDictionary<string, string?> valuesByFieldName)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }

        var byNorm = new Dictionary<string, string?>();
        foreach (var (name, value) in valuesByFieldName)
        {
            byNorm[TextNormalizer.Normalize(name)] = value;
        }

        var filled = false;
        var rendered = Placeholder().Replace(template, m =>
        {
            var value = byNorm.GetValueOrDefault(TextNormalizer.Normalize(m.Groups[1].Value))?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            filled = true;
            return value + m.Groups[2].Value;
        });

        if (!filled)
        {
            return null;
        }

        rendered = SeparatorRun().Replace(rendered, m => m.Groups[1].Captures[^1].Value);
        var collapsed = Spaces().Replace(rendered, " ").Trim(TrimChars.ToCharArray());
        return collapsed.Any(char.IsLetterOrDigit) ? collapsed : null;
    }

    /// <summary>Имена полей, упомянутые в шаблоне.</summary>
    public static IReadOnlyList<string> FieldNames(string? template) =>
        string.IsNullOrEmpty(template) ? [] : FieldName().Matches(template).Select(m => m.Groups[1].Value.Trim()).ToList();

    /// <summary>Поле и его «хвост» до разделителя: «{Длина} см».</summary>
    [GeneratedRegex(@"\{([^{}]+)\}([^{},;|/→–—-]*)")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex FieldName();

    /// <summary>Два и больше разделителя подряд (после выпавших значений): « → , » → «, ».</summary>
    [GeneratedRegex(@"(\s*[,;|/→–—-]\s*){2,}")]
    private static partial Regex SeparatorRun();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

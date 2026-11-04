using System.Text.RegularExpressions;

namespace Invy.Client.Core.Editor;

/// <summary>
/// Предпросмотр авто-названия на клиенте — те же правила, что у сервера (<c>Invy.Domain.Types.TitleTemplate</c>):
/// «{Разъём 1} → {Разъём 2}, {Длина} см»; имена полей без учёта регистра и «ё»; пустое значение выпадает
/// вместе со своим «хвостом» до разделителя, повторяющиеся разделители схлопываются.
/// </summary>
public static partial class TitleRenderer
{
    private const string TrimChars = " ,;:–—-→/|";

    public static string? Render(string? template, IReadOnlyDictionary<string, string?> valuesByFieldName)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }

        var byName = new Dictionary<string, string?>();
        foreach (var (name, value) in valuesByFieldName)
        {
            byName[Normalize(name)] = value;
        }

        var filled = false;
        var rendered = Placeholder().Replace(template, m =>
        {
            var value = byName.GetValueOrDefault(Normalize(m.Groups[1].Value))?.Trim();
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

    /// <summary>Совпадают ли имена полей по правилам шаблона (регистр, «ё», пробелы).</summary>
    public static bool SameName(string a, string b) => Normalize(a) == Normalize(b);

    private static string Normalize(string name) => Spaces().Replace(name.Trim(), " ").ToLowerInvariant().Replace('ё', 'е');

    [GeneratedRegex(@"\{([^{}]+)\}([^{},;|/→–—-]*)")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex FieldName();

    [GeneratedRegex(@"(\s*[,;|/→–—-]\s*){2,}")]
    private static partial Regex SeparatorRun();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

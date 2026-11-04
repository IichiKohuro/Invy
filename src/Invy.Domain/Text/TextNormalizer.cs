using System.Text;

namespace Invy.Domain.Text;

/// <summary>
/// Единая нормализация текста для поиска и сортировки: одинаково применяется
/// при записи (колонки <c>*Norm</c>, индекс FTS5) и к запросу пользователя.
/// </summary>
public static class TextNormalizer
{
    /// <summary>
    /// NFKC → нижний регистр (инвариантная культура) → «ё» в «е» → схлопывание пробелов.
    /// Встроенные <c>lower()</c>/<c>LIKE</c>/<c>NOCASE</c> SQLite кириллицу не понимают,
    /// поэтому нормализация выполняется в .NET.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var source = text.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(source.Length);
        var pendingSpace = false;

        foreach (var ch in source)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(Fold(char.ToLowerInvariant(ch)));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Текст для индекса FTS5: NFKC и «ё/Ё» → «е/Е», регистр сохраняется.
    /// Регистр (в т. ч. кириллицы) складывает токенизатор <c>unicode61</c>, а вот «ё» он не трогает
    /// (<c>remove_diacritics</c> работает только для латиницы). Сохранённый регистр нужен,
    /// чтобы <c>highlight()</c> возвращал текст в исходном виде.
    /// </summary>
    public static string ForFullTextIndex(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Normalize(NormalizationForm.FormKC).Replace('ё', 'е').Replace('Ё', 'Е');
    }

    private static char Fold(char ch) => ch == 'ё' ? 'е' : ch;
}

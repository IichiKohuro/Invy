using System.Globalization;

namespace Invy.Domain.Text;

/// <summary>
/// Естественная сортировка без учёта регистра и «ё»/«е»: «Коробка 2» &lt; «коробка 10» &lt; «Коробка 11».
/// Числа сравниваются по значению, остальной текст — по правилам инвариантной культуры.
/// Используется в .NET и как коллация SQLite <c>RU_NATURAL</c>.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string?>
{
    public static NaturalStringComparer Instance { get; } = new();

    private static readonly CompareInfo Culture = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions TextOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth;

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var result = CompareNatural(x.AsSpan(), y.AsSpan());

        // Равные «естественно» строки упорядочиваем стабильно, чтобы сортировка была детерминированной.
        return result != 0 ? result : string.CompareOrdinal(x, y);
    }

    private static int CompareNatural(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        while (!x.IsEmpty && !y.IsEmpty)
        {
            var xDigits = char.IsAsciiDigit(x[0]);
            var yDigits = char.IsAsciiDigit(y[0]);
            var xChunk = TakeChunk(x, xDigits);
            var yChunk = TakeChunk(y, yDigits);

            var result = xDigits && yDigits
                ? CompareNumbers(xChunk, yChunk)
                : Culture.Compare(xChunk, yChunk, TextOptions);

            if (result != 0)
            {
                return result;
            }

            x = x[xChunk.Length..];
            y = y[yChunk.Length..];
        }

        return x.Length.CompareTo(y.Length);
    }

    private static ReadOnlySpan<char> TakeChunk(ReadOnlySpan<char> text, bool digits)
    {
        var length = 1;
        while (length < text.Length && char.IsAsciiDigit(text[length]) == digits)
        {
            length++;
        }

        return text[..length];
    }

    private static int CompareNumbers(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        var xTrimmed = x.TrimStart('0');
        var yTrimmed = y.TrimStart('0');

        // Больше значащих цифр — больше число; при равной длине сравнение посимвольное.
        var result = xTrimmed.Length.CompareTo(yTrimmed.Length);
        if (result == 0)
        {
            result = xTrimmed.SequenceCompareTo(yTrimmed);
        }

        // «007» после «7»: при равных значениях меньше ведущих нулей — раньше.
        return result != 0 ? result : x.Length.CompareTo(y.Length);
    }
}

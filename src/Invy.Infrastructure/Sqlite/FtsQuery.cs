using System.Text;
using Invy.Domain.Text;

namespace Invy.Infrastructure.Sqlite;

/// <summary>
/// Безопасное построение выражений FTS5 <c>MATCH</c> из пользовательского ввода.
/// Пользовательский текст никогда не попадает в синтаксис FTS5 напрямую: каждое слово
/// нормализуется и берётся в кавычки, поэтому <c>AND</c>, <c>NEAR</c>, <c>*</c>, <c>"</c> в запросе — просто текст.
/// </summary>
public static class FtsQuery
{
    /// <summary>
    /// «кабель usb-c» → <c>"кабель"* "usb" + "c"*</c>: все слова обязательны,
    /// последнее слово каждой группы — префикс; слова через дефис/точку — фраза.
    /// Возвращает пустую строку, если искать нечего.
    /// </summary>
    public static string ForPrefixTerms(string? userInput)
    {
        var normalized = TextNormalizer.Normalize(userInput);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var word in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var tokens = SplitTokens(word);
            if (tokens.Count == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            // Токены одного слова («usb-c») — фраза FTS5: "usb" + "c"*
            for (var i = 0; i < tokens.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(" + ");
                }

                builder.Append('"').Append(tokens[i]).Append('"');
            }

            builder.Append('*');
        }

        return builder.ToString();
    }

    /// <summary>Делит слово на токены так же, как токенизатор unicode61: по всему, что не буква и не цифра.</summary>
    private static List<string> SplitTokens(string word)
    {
        var tokens = new List<string>();
        var start = -1;

        for (var i = 0; i <= word.Length; i++)
        {
            var isTokenChar = i < word.Length && char.IsLetterOrDigit(word[i]);
            if (isTokenChar && start < 0)
            {
                start = i;
            }
            else if (!isTokenChar && start >= 0)
            {
                tokens.Add(word[start..i]);
                start = -1;
            }
        }

        return tokens;
    }
}

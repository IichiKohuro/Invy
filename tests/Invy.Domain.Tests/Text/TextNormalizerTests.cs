using Invy.Domain.Text;

namespace Invy.Domain.Tests.Text;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("Кабель USB-C", "кабель usb-c")]
    [InlineData("ЁЛКА ёлка", "елка елка")]
    [InlineData("ёжик", "ежик")]               // «ё» из двух кодовых точек (е + U+0308)
    [InlineData("  Серая   коробка \t\n", "серая коробка")]
    [InlineData("ＵＳＢ　３.０", "usb 3.0")]          // полноширинные символы и пробел (NFKC)
    [InlineData("Ⅳ", "iv")]
    public void Normalizes_case_yo_whitespace_and_compatibility_forms(string input, string expected) =>
        TextNormalizer.Normalize(input).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_gives_empty_string(string? input) => TextNormalizer.Normalize(input).ShouldBeEmpty();

    [Theory]
    [InlineData("Ёлочная гирлянда", "Елочная гирлянда")]
    [InlineData("ЁЖ и ёж", "ЕЖ и еж")]
    [InlineData("Кабель USB-C", "Кабель USB-C")]
    [InlineData("ёжик", "ежик")]
    [InlineData(null, "")]
    public void Full_text_index_form_keeps_case_and_folds_yo(string? input, string expected) =>
        TextNormalizer.ForFullTextIndex(input).ShouldBe(expected);
}

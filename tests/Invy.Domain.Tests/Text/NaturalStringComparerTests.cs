using Invy.Domain.Text;

namespace Invy.Domain.Tests.Text;

public class NaturalStringComparerTests
{
    [Fact]
    public void Sorts_numbers_by_value_and_ignores_case()
    {
        string[] input = ["Коробка 10", "коробка 2", "Коробка 1", "КОРОБКА 11", "Коробка 3"];

        var sorted = input.Order(NaturalStringComparer.Instance).ToArray();

        sorted.ShouldBe(["Коробка 1", "коробка 2", "Коробка 3", "Коробка 10", "КОРОБКА 11"]);
    }

    [Fact]
    public void Sorts_cyrillic_alphabetically_with_yo_as_ye()
    {
        string[] input = ["Ящик", "ёлочные игрушки", "Антенна", "Елочная гирлянда", "Бокс"];

        var sorted = input.Order(NaturalStringComparer.Instance).ToArray();

        sorted.ShouldBe(["Антенна", "Бокс", "Елочная гирлянда", "ёлочные игрушки", "Ящик"]);
    }

    [Theory]
    [InlineData("Кабель 1.5 м", "Кабель 1.10 м", -1)]  // «5» < «10» в каждом числовом фрагменте
    [InlineData("a7", "a007", -1)]                     // равные значения: меньше ведущих нулей — раньше
    [InlineData("usb2", "USB10", -1)]
    [InlineData("Пакет", "Пакет 1", -1)]
    public void Compares_mixed_text_and_numbers(string x, string y, int expectedSign)
    {
        Math.Sign(NaturalStringComparer.Instance.Compare(x, y)).ShouldBe(expectedSign);
        Math.Sign(NaturalStringComparer.Instance.Compare(y, x)).ShouldBe(-expectedSign);
    }

    [Fact]
    public void Handles_nulls_and_is_deterministic_for_equal_texts()
    {
        var comparer = NaturalStringComparer.Instance;

        comparer.Compare(null, "a").ShouldBeLessThan(0);
        comparer.Compare("a", null).ShouldBeGreaterThan(0);
        comparer.Compare(null, null).ShouldBe(0);
        comparer.Compare("Ёж", "Ёж").ShouldBe(0);
        comparer.Compare("Еж", "ёж").ShouldNotBe(0);   // равны «естественно», но порядок стабилен
    }
}

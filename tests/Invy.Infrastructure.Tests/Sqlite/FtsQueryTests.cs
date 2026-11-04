using Invy.Infrastructure.Sqlite;

namespace Invy.Infrastructure.Tests.Sqlite;

public class FtsQueryTests
{
    [Theory]
    [InlineData("кабель", "\"кабель\"*")]
    [InlineData("Кабель USB-C", "\"кабель\"* \"usb\" + \"c\"*")]
    [InlineData("Ёлка", "\"елка\"*")]
    [InlineData("3.0", "\"3\" + \"0\"*")]
    [InlineData("a AND b", "\"a\"* \"and\"* \"b\"*")]                // операторы FTS5 — просто слова
    [InlineData("\"кабель\" OR -hdmi NEAR(x)", "\"кабель\"* \"or\"* \"hdmi\"* \"near\" + \"x\"*")]
    public void Builds_safe_prefix_query(string input, string expected) => FtsQuery.ForPrefixTerms(input).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("--- ** ()")]
    public void Returns_empty_when_nothing_to_search(string? input) => FtsQuery.ForPrefixTerms(input).ShouldBeEmpty();
}

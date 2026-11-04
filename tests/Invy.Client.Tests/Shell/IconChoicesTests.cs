using FluentAvalonia.UI.Controls;
using Invy.Client.Core.Editor;
using Invy.Client.Core.Explorer;
using Invy.Contracts.Catalog;

namespace Invy.Client.Tests.Shell;

public class IconChoicesTests
{
    [Fact]
    public void Every_symbol_exists_in_fluent_icons()
    {
        var symbols = IconChoices.Symbols
            .Concat([ItemVisuals.Symbol(null, TypeRole.Place), ItemVisuals.Symbol(null, TypeRole.Container), ItemVisuals.Symbol(null, TypeRole.Item)]);

        symbols.Where(s => !Enum.TryParse<FASymbol>(s, ignoreCase: true, out _)).ShouldBeEmpty();
        IconChoices.Symbols.ShouldBeUnique();
        IconChoices.Emojis.ShouldBeUnique();
    }

    [Fact]
    public void Colors_are_hex()
    {
        IconChoices.Colors.ShouldAllBe(c => c.Length == 7 && c[0] == '#' && c.Skip(1).All(Uri.IsHexDigit));
    }
}

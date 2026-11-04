using System.Globalization;
using Invy.Contracts.Catalog;

namespace Invy.Client.Core.Explorer;

/// <summary>Иконки, правила вложенности и русские подписи для списков и карточек.</summary>
public static class ItemVisuals
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>Эмодзи вещи или типа; <c>null</c> — показывается символ <see cref="Symbol"/>.</summary>
    public static string? Glyph(IconDto? icon) => icon is { Kind: IconKind.Emoji } ? icon.Value : null;

    /// <summary>Имя символа Fluent: своё или по роли (место, контейнер, вещь).</summary>
    public static string Symbol(IconDto? icon, TypeRole role) => icon is { Kind: IconKind.Fluent } ? icon.Value : role switch
    {
        TypeRole.Place => "MapPin",
        TypeRole.Container => "Folder",
        _ => "Tag",
    };

    /// <summary>Место содержит что угодно; контейнер — всё, кроме мест; обычная вещь — ничего (как на сервере).</summary>
    public static bool CanContain(TypeRole parent, TypeRole child) => parent switch
    {
        TypeRole.Place => true,
        TypeRole.Container => child != TypeRole.Place,
        _ => false,
    };

    /// <summary>1 вещь, 2 вещи, 5 вещей.</summary>
    public static string Plural(int count, string one, string few, string many)
    {
        var mod100 = Math.Abs(count) % 100;
        var mod10 = mod100 % 10;
        var word = mod100 is >= 11 and <= 14 ? many : mod10 switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many,
        };
        return $"{count} {word}";
    }

    public static string Things(int count) => Plural(count, "вещь", "вещи", "вещей");

    /// <summary>«3 шт.», «1,5 м»; для одной штуки — <c>null</c> (не загромождать список).</summary>
    public static string? Quantity(double quantity, string? unit)
    {
        var hasUnit = !string.IsNullOrWhiteSpace(unit);
        if (quantity == 1 && (!hasUnit || unit!.Trim().TrimEnd('.') == "шт"))
        {
            return null;
        }

        return $"{quantity.ToString("0.###", Russian)} {(hasUnit ? unit!.Trim() : "шт.")}";
    }
}

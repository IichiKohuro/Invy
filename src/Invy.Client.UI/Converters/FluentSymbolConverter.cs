using System.Globalization;
using Avalonia.Data.Converters;
using FluentAvalonia.UI.Controls;

namespace Invy.Client.UI.Converters;

/// <summary>
/// Имя символа Fluent из ViewModel («Home», «MapPin») → <see cref="FASymbol"/> или <see cref="FASymbolIconSource"/>,
/// в зависимости от того, куда идёт привязка. ViewModels не зависят от FluentAvalonia.
/// </summary>
public sealed class FluentSymbolConverter : IValueConverter
{
    public static FluentSymbolConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string name || !Enum.TryParse<FASymbol>(name, ignoreCase: true, out var symbol))
        {
            return null;
        }

        return targetType.IsAssignableFrom(typeof(FASymbol)) ? symbol : new FASymbolIconSource { Symbol = symbol };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

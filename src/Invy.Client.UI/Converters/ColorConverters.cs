using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Invy.Client.UI.Converters;

public static class ColorConverters
{
    /// <summary>«#0078D4» → кисть; пусто или ошибка — прозрачная.</summary>
    public static FuncValueConverter<string?, IBrush?> Brush { get; } = new(text =>
        text is not null && Color.TryParse(text, out var color) ? new SolidColorBrush(color) : Brushes.Transparent);
}

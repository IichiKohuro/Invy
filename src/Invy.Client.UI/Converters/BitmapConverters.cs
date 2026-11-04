using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Invy.Client.UI.Converters;

/// <summary>Байты фото (WebP, JPEG, PNG…) → картинка для <see cref="Avalonia.Controls.Image"/>.</summary>
public static class BitmapConverters
{
    /// <summary>Для лент и списков: уменьшается при декодировании, большие исходники не держатся в памяти целиком.</summary>
    public static FuncValueConverter<byte[]?, Bitmap?> Thumbnail { get; } = new(bytes => Decode(bytes, 320));

    /// <summary>Для просмотра.</summary>
    public static FuncValueConverter<byte[]?, Bitmap?> Preview { get; } = new(bytes => Decode(bytes, 1600));

    public static Bitmap? Decode(byte[]? bytes, int maxWidth)
    {
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            return Bitmap.DecodeToWidth(stream, maxWidth, BitmapInterpolationMode.MediumQuality);
        }
#pragma warning disable CA1031 // повреждённая или неподдерживаемая картинка — показываем иконку вместо неё
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }
}

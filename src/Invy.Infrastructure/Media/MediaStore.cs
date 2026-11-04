using System.Security.Cryptography;
using Invy.Application.Abstractions;
using Invy.Application.Errors;
using SkiaSharp;

namespace Invy.Infrastructure.Media;

/// <summary>
/// Файлы фото на диске сервера: оригинал по SHA-256 содержимого (одинаковые фото хранятся один раз)
/// и превью WebP 256 и 1024 px по длинной стороне — с поворотом по EXIF и без метаданных (в т. ч. GPS).
/// </summary>
internal sealed class MediaStore(string root) : IMediaStore
{
    private static readonly (MediaSize Size, int Pixels)[] Previews = [(MediaSize.Thumb, 256), (MediaSize.Preview, 1024)];

    public async Task<StoredMedia> SaveImageAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        var (contentType, extension) = DetectFormat(bytes)
            ?? throw new ValidationException("files", "Поддерживаются изображения JPEG, PNG, WebP, GIF и BMP");
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));

        using var codec = SKCodec.Create(new SKMemoryStream(bytes)) ?? throw new ValidationException("files", "Не удалось прочитать изображение");
        using var bitmap = SKBitmap.Decode(codec) ?? throw new ValidationException("files", "Не удалось прочитать изображение");
        var swap = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var (width, height) = swap ? (bitmap.Height, bitmap.Width) : (bitmap.Width, bitmap.Height);

        await WriteAtomicAsync(OriginalPath(sha, extension), bytes, cancellationToken);
        foreach (var (size, pixels) in Previews)
        {
            var path = PreviewPath(sha, size);
            if (!File.Exists(path))
            {
                await WriteAtomicAsync(path, RenderPreview(bitmap, codec.EncodedOrigin, width, height, pixels), cancellationToken);
            }
        }

        return new StoredMedia(sha, contentType, extension, bytes.LongLength, width, height);
    }

    public string? GetPath(string sha256, string extension, MediaSize size)
    {
        var path = size == MediaSize.Original ? OriginalPath(sha256, extension) : PreviewPath(sha256, size);
        return File.Exists(path) ? path : null;
    }

    private string OriginalPath(string sha, string extension) => Path.Combine(root, "original", sha[..2], sha[2..4], $"{sha}.{extension}");

    private string PreviewPath(string sha, MediaSize size) => Path.Combine(root, "thumbs", sha[..2], $"{sha}_{(size == MediaSize.Thumb ? 256 : 1024)}.webp");

    private static byte[] RenderPreview(SKBitmap bitmap, SKEncodedOrigin origin, int width, int height, int maxSide)
    {
        var scale = Math.Min(1f, maxSide / (float)Math.Max(width, height));
        var target = new SKImageInfo(Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)), SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(target);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(SKMatrix.Concat(SKMatrix.CreateScale(scale, scale), Orientation(origin, bitmap.Width, bitmap.Height)));
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Webp, 80);
        return data.ToArray();
    }

    /// <summary>Преобразование «пиксель исходника → пиксель повёрнутого изображения» для 8 ориентаций EXIF.</summary>
    private static SKMatrix Orientation(SKEncodedOrigin origin, int w, int h)
    {
        static SKMatrix M(float sx, float kx, float tx, float ky, float sy, float ty) =>
            new() { ScaleX = sx, SkewX = kx, TransX = tx, SkewY = ky, ScaleY = sy, TransY = ty, Persp2 = 1 };

        return origin switch
        {
            SKEncodedOrigin.TopRight => M(-1, 0, w, 0, 1, 0),
            SKEncodedOrigin.BottomRight => M(-1, 0, w, 0, -1, h),
            SKEncodedOrigin.BottomLeft => M(1, 0, 0, 0, -1, h),
            SKEncodedOrigin.LeftTop => M(0, 1, 0, 1, 0, 0),
            SKEncodedOrigin.RightTop => M(0, -1, h, 1, 0, 0),
            SKEncodedOrigin.RightBottom => M(0, -1, h, -1, 0, w),
            SKEncodedOrigin.LeftBottom => M(0, 1, 0, -1, 0, w),
            _ => SKMatrix.Identity,
        };
    }

    /// <summary>Формат — по сигнатуре файла, а не по расширению или заголовку от клиента.</summary>
    internal static (string ContentType, string Extension)? DetectFormat(ReadOnlySpan<byte> b) => b switch
    {
        [0xFF, 0xD8, 0xFF, ..] => ("image/jpeg", "jpg"),
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => ("image/png", "png"),
        [0x47, 0x49, 0x46, 0x38, ..] => ("image/gif", "gif"),
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => ("image/webp", "webp"),
        [0x42, 0x4D, ..] => ("image/bmp", "bmp"),
        _ => null,
    };

    private static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
        File.Move(temp, path, overwrite: true);
    }
}

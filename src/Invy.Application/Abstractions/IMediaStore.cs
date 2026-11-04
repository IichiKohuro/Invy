namespace Invy.Application.Abstractions;

/// <summary>Хранилище файлов фото (реализация — диск сервера, SkiaSharp для превью).</summary>
public interface IMediaStore
{
    /// <summary>Сохраняет изображение и превью. Неподдерживаемый формат → <see cref="Errors.ValidationException"/>.</summary>
    Task<StoredMedia> SaveImageAsync(Stream content, CancellationToken cancellationToken);

    /// <summary>Путь к файлу нужного размера или <c>null</c>, если файла нет.</summary>
    string? GetPath(string sha256, string extension, MediaSize size);
}

public sealed record StoredMedia(string Sha256, string ContentType, string Extension, long SizeBytes, int Width, int Height);

public enum MediaSize
{
    /// <summary>256 px по длинной стороне — списки, плитки.</summary>
    Thumb = 0,

    /// <summary>1024 px — карточка, просмотр.</summary>
    Preview = 1,

    /// <summary>Исходный файл.</summary>
    Original = 2,
}

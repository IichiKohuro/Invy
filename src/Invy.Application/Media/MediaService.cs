using Invy.Application.Abstractions;
using Invy.Application.Errors;
using Invy.Contracts.Items;
using Invy.Domain.Files;
using Microsoft.EntityFrameworkCore;

namespace Invy.Application.Media;

/// <summary>Файл для загрузки: имя, размер и открытие потока (абстракция над IFormFile).</summary>
public sealed record UploadFile(string FileName, long Length, Func<Stream> Open);

/// <summary>Файл для отдачи клиенту.</summary>
public sealed record MediaContent(string Path, string ContentType, string ETag);

/// <summary>Фото вещей: загрузка, отдача в нужном размере, обложка, удаление.</summary>
public sealed class MediaService(IInventoryDbContext db, IUnitOfWork unitOfWork, IMediaStore store)
{
    public const long MaxFileBytes = 25 * 1024 * 1024;
    public const int MaxFilesPerUpload = 20;

    public async Task<IReadOnlyList<MediaDto>> UploadAsync(Guid itemId, IReadOnlyList<UploadFile> files, CancellationToken cancellationToken)
    {
        if (files.Count is 0 or > MaxFilesPerUpload)
        {
            throw new ValidationException("files", $"Загрузите от 1 до {MaxFilesPerUpload} файлов");
        }

        if (files.FirstOrDefault(f => f.Length > MaxFileBytes) is { } tooBig)
        {
            throw new ValidationException("files", $"«{tooBig.FileName}» больше {MaxFileBytes / 1024 / 1024} МБ");
        }

        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken) ?? throw NotFoundException.For("Вещь", itemId);
        var sortOrder = await db.Media.Where(m => m.ItemId == itemId).MaxAsync(m => (int?)m.SortOrder, cancellationToken) ?? -1;

        var added = new List<MediaFile>();
        foreach (var file in files)
        {
            await using var stream = file.Open();
            var stored = await store.SaveImageAsync(stream, cancellationToken);
            var media = new MediaFile
            {
                ItemId = itemId,
                Kind = MediaKind.Photo,
                Sha256 = stored.Sha256,
                ContentType = stored.ContentType,
                FileName = SafeFileName(file.FileName, stored.Extension),
                SizeBytes = stored.SizeBytes,
                Width = stored.Width,
                Height = stored.Height,
                SortOrder = ++sortOrder,
            };
            db.Media.Add(media);
            added.Add(media);
        }

        // Первое фото становится обложкой: в списках и плитках сразу видно, как выглядит вещь.
        item.CoverMediaId ??= added[0].Id;
        await unitOfWork.SaveAsync(cancellationToken: cancellationToken);
        return added.Select(ToDto).ToList();
    }

    public async Task<MediaContent> GetAsync(Guid mediaId, MediaSize size, CancellationToken cancellationToken)
    {
        var media = await db.Media.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mediaId, cancellationToken) ?? throw NotFoundException.For("Фото", mediaId);
        var extension = ExtensionOf(media.ContentType);
        var path = store.GetPath(media.Sha256, extension, size) ?? throw new NotFoundException($"Файл фото не найден: {mediaId}");
        return new MediaContent(path, size == MediaSize.Original ? media.ContentType : "image/webp", $"\"{media.Sha256}-{size}\"");
    }

    public async Task SetCoverAsync(Guid itemId, Guid? mediaId, CancellationToken cancellationToken)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken) ?? throw NotFoundException.For("Вещь", itemId);
        if (mediaId is { } id && !await db.Media.AnyAsync(m => m.Id == id && m.ItemId == itemId, cancellationToken))
        {
            throw new ValidationException("mediaId", "Фото не относится к этой вещи");
        }

        item.CoverMediaId = mediaId;
        await unitOfWork.SaveAsync(cancellationToken: cancellationToken);
    }

    /// <summary>Удаляет фото из карточки. Файл на диске остаётся до очистки неиспользуемых файлов.</summary>
    public async Task DeleteAsync(Guid mediaId, CancellationToken cancellationToken)
    {
        var media = await db.Media.FirstOrDefaultAsync(m => m.Id == mediaId, cancellationToken) ?? throw NotFoundException.For("Фото", mediaId);
        if (media.ItemId is { } itemId && await db.Items.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken) is { } item && item.CoverMediaId == mediaId)
        {
            item.CoverMediaId = await db.Media.Where(m => m.ItemId == itemId && m.Id != mediaId).OrderBy(m => m.SortOrder).Select(m => (Guid?)m.Id).FirstOrDefaultAsync(cancellationToken);
        }

        db.Media.Remove(media);
        await unitOfWork.SaveAsync(cancellationToken: cancellationToken);
    }

    public static MediaDto ToDto(MediaFile m) => new(m.Id, m.FileName, m.ContentType, m.SizeBytes, m.Width, m.Height, m.SortOrder, m.CreatedAt);

    private static string ExtensionOf(string contentType) => contentType switch
    {
        "image/png" => "png",
        "image/gif" => "gif",
        "image/webp" => "webp",
        "image/bmp" => "bmp",
        _ => "jpg",
    };

    private static string SafeFileName(string? name, string extension)
    {
        var file = Path.GetFileName(name ?? string.Empty);
        file = string.Concat(file.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        return string.IsNullOrEmpty(file) ? $"photo.{extension}" : file.Length > 200 ? file[..200] : file;
    }
}

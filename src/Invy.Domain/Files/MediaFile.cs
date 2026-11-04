namespace Invy.Domain.Files;

/// <summary>Фото или вложение. Файл хранится на диске сервера по SHA-256 содержимого (загрузка — фаза 2).</summary>
public sealed class MediaFile
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>null — пользовательская иконка, не привязанная к вещи.</summary>
    public Guid? ItemId { get; set; }

    public MediaKind Kind { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public string? Caption { get; set; }

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }
}

public enum MediaKind
{
    Photo = 0,
    Icon = 1,
    Document = 2,
}

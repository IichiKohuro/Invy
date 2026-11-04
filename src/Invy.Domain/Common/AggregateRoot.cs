namespace Invy.Domain.Common;

/// <summary>
/// Корень агрегата: Guid v7 (клиент может создавать записи сам), версия для оптимистичной блокировки,
/// время и автор изменений. <see cref="Version"/>, время и автор проставляются при сохранении.
/// </summary>
public abstract class AggregateRoot
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public int Version { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public Guid? UpdatedBy { get; set; }
}

/// <summary>Иконка вещи или типа: символ Fluent, эмодзи или своя картинка (id медиафайла).</summary>
public enum IconKind
{
    Fluent = 0,
    Emoji = 1,
    Custom = 2,
}

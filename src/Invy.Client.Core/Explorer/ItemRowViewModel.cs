using CommunityToolkit.Mvvm.ComponentModel;
using Invy.Client.Core.Catalog;
using Invy.Contracts.Catalog;
using Invy.Contracts.Items;

namespace Invy.Client.Core.Explorer;

/// <summary>Строка списка (или плитка) в «Проводнике».</summary>
public sealed partial class ItemRowViewModel(ItemSummaryDto item) : ObservableObject
{
    public ItemSummaryDto Item { get; } = item;

    public Guid Id => Item.Id;

    public string Name => Item.Name;

    public string TypeName => Item.TypeName;

    public bool IsContainer => Item.Role != TypeRole.Item;

    public string? Glyph => ItemVisuals.Glyph(Item.Icon);

    public string Symbol => ItemVisuals.Symbol(Item.Icon, Item.Role);

    public bool HasGlyph => Glyph is not null;

    public string? Color => Item.Color;

    public Guid? CoverMediaId => Item.CoverMediaId;

    /// <summary>«Коробка · 3 вещи», «Кабель · 2 шт.».</summary>
    public string Subtitle => IsContainer
        ? $"{Item.TypeName} · {(Item.ChildCount == 0 ? "пусто" : ItemVisuals.Things(Item.ChildCount))}"
        : ItemVisuals.Quantity(Item.Quantity, Item.Unit) is { } quantity ? $"{Item.TypeName} · {quantity}" : Item.TypeName;

    public string UpdatedText => Item.UpdatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Вырезано (Ctrl+X) и ждёт вставки — показывается полупрозрачным.</summary>
    [ObservableProperty]
    public partial bool IsCut { get; set; }
}

/// <summary>Хлебная крошка: <see cref="Id"/> = null — корень раздела.</summary>
public sealed record Breadcrumb(Guid? Id, string Name);

/// <summary>Характеристика в карточке: «Длина — 100 см».</summary>
public sealed record CardField(string Label, string Value, string? Group);

/// <summary>Карточка вещи в панели деталей.</summary>
public sealed class ItemCardViewModel
{
    public ItemCardViewModel(ItemDetailsDto item, CatalogStore catalog)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(catalog);
        Item = item;
        // Порядок и подписи — как в форме типа; значения уже отформатированы сервером («100 см»).
        var fields = catalog.Type(item.TypeId) is { } type ? CatalogStore.EffectiveFields(type) : [];
        Fields = fields
            .Select(f => (Field: f, Value: item.Values.FirstOrDefault(v => v.FieldId == f.Id)?.Display))
            .Where(x => !string.IsNullOrEmpty(x.Value))
            .Select(x => new CardField(x.Field.Name, x.Value!, x.Field.GroupName))
            .ToList();
        Tags = item.TagIds.Select(catalog.Tag).OfType<TagDto>().ToList();
    }

    public ItemDetailsDto Item { get; }

    public Guid Id => Item.Id;

    public string Name => Item.Name;

    public string Subtitle => $"{Item.TypeName} · № {Item.Number}";

    public string? Glyph => ItemVisuals.Glyph(Item.Icon);

    public string Symbol => ItemVisuals.Symbol(Item.Icon, Item.Role);

    public bool HasGlyph => Glyph is not null;

    public bool IsContainer => Item.Role != TypeRole.Item;

    /// <summary>Где лежит: «Дом › Чердак › Серая коробка».</summary>
    public string PathText => Item.Path.Count > 0
        ? string.Join(" › ", Item.Path.Select(p => p.Name))
        : Item.Role == TypeRole.Place ? "Места — верхний уровень" : "Неразобранное (без места)";

    public string? Description => Item.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Item.Description);

    public string? QuantityText => ItemVisuals.Quantity(Item.Quantity, Item.Unit);

    public string? ContentsText => !IsContainer ? null
        : Item.Counts.Total == 0 ? "Пусто"
        : Item.Counts.Total == Item.Counts.Direct ? $"Внутри {ItemVisuals.Things(Item.Counts.Direct)}"
        : $"Внутри {ItemVisuals.Things(Item.Counts.Direct)}, всего с вложенными — {Item.Counts.Total}";

    public IReadOnlyList<CardField> Fields { get; }

    public bool HasFields => Fields.Count > 0;

    public IReadOnlyList<TagDto> Tags { get; }

    public bool HasTags => Tags.Count > 0;

    public IReadOnlyList<MediaDto> Photos => Item.Photos;

    public bool HasPhotos => Item.Photos.Count > 0;

    public Guid? CoverMediaId => Item.CoverMediaId;

    public string UpdatedText => $"Изменено {Item.UpdatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture)}";
}

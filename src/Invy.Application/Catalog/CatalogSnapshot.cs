using Invy.Application.Abstractions;
using Invy.Contracts.Catalog;
using Invy.Domain.Lists;
using Invy.Domain.Tags;
using Invy.Domain.Types;
using Microsoft.EntityFrameworkCore;
using DomainIconKind = Invy.Domain.Common.IconKind;

namespace Invy.Application.Catalog;

/// <summary>
/// Снимок справочников (типы с полями, варианты списков) для одной операции.
/// Справочники небольшие (десятки типов, сотни вариантов) — загружаются целиком.
/// </summary>
internal sealed class CatalogSnapshot
{
    private CatalogSnapshot(IReadOnlyDictionary<Guid, ItemType> types, IReadOnlyDictionary<Guid, ValueListOption> options)
    {
        Types = types;
        Options = options;
    }

    public IReadOnlyDictionary<Guid, ItemType> Types { get; }

    public IReadOnlyDictionary<Guid, ValueListOption> Options { get; }

    public static async Task<CatalogSnapshot> LoadAsync(IInventoryDbContext db, CancellationToken cancellationToken)
    {
        var types = await db.ItemTypes.AsNoTracking().Include(t => t.Fields).ToDictionaryAsync(t => t.Id, cancellationToken);
        var options = await db.ValueListOptions.AsNoTracking().ToDictionaryAsync(o => o.Id, cancellationToken);
        return new CatalogSnapshot(types, options);
    }

    public string? OptionName(Guid id) => Options.TryGetValue(id, out var option) ? option.Value : null;

    /// <summary>Действующие варианты справочника (без архивных).</summary>
    public HashSet<Guid> ActiveOptionIds(Guid? listId) =>
        listId is null ? [] : Options.Values.Where(o => o.ListId == listId && !o.IsArchived).Select(o => o.Id).ToHashSet();

    /// <summary>Характеристики базовых типов (от самого базового) — без собственных.</summary>
    public IReadOnlyList<FieldDefinition> InheritedFields(ItemType type) => TypeHierarchy.InheritedFields(type, Types);

    /// <summary>Все характеристики вещи типа: унаследованные + собственные, по порядку; архивные — по запросу.</summary>
    public IReadOnlyList<FieldDefinition> EffectiveFields(ItemType type, bool includeArchived = false) =>
        InheritedFields(type).Concat(type.Fields.OrderBy(f => f.SortOrder)).Where(f => includeArchived || !f.IsArchived).ToList();
}

internal static class TypeHierarchy
{
    public static IReadOnlyList<FieldDefinition> InheritedFields(ItemType type, IReadOnlyDictionary<Guid, ItemType> types)
    {
        var chain = new List<ItemType>();
        var current = type.BaseTypeId is { } baseId ? types.GetValueOrDefault(baseId) : null;
        while (current is not null && chain.Count < 16 && current.Id != type.Id && !chain.Contains(current))
        {
            chain.Insert(0, current);
            current = current.BaseTypeId is { } next ? types.GetValueOrDefault(next) : null;
        }

        return chain.SelectMany(t => t.Fields.OrderBy(f => f.SortOrder)).ToList();
    }
}

internal static class CatalogMapping
{
    public static IconDto? ToIcon(DomainIconKind? kind, string? value) =>
        kind is { } k && !string.IsNullOrWhiteSpace(value) ? new IconDto((IconKind)k, value) : null;

    public static FieldSettingsDto ToDto(this FieldSettings s) => new(s.Min, s.Max, s.Decimals, s.MaxLength, s.Placeholder, s.Pattern, s.DefaultValue);

    public static FieldSettings ToDomain(this FieldSettingsDto? s) =>
        s is null ? FieldSettings.Empty : new FieldSettings(s.Min, s.Max, s.Decimals, s.MaxLength, Trim(s.Placeholder), Trim(s.Pattern), Trim(s.DefaultValue));

    public static FieldDefinitionDto ToDto(this FieldDefinition f) => new(
        f.Id, f.TypeId, f.Name, (Contracts.Catalog.FieldDataType)f.DataType, f.Unit, f.ValueListId, f.GroupName,
        f.IsRequired, f.ShowInList, f.RememberLast, f.Settings.ToDto(), f.SortOrder, f.IsArchived);

    public static ItemTypeDto ToDto(this ItemType t, IReadOnlyDictionary<Guid, ItemType> all) => new(
        t.Id, t.Name, t.BaseTypeId, (Contracts.Catalog.TypeRole)t.Role, ToIcon(t.IconKind, t.IconValue), t.Color, t.TitleTemplate, t.DefaultUnit,
        t.SortOrder, t.IsArchived,
        t.Fields.OrderBy(f => f.SortOrder).Select(f => f.ToDto()).ToList(),
        TypeHierarchy.InheritedFields(t, all).Select(f => f.ToDto()).ToList(),
        t.Version);

    public static ValueListDto ToDto(this ValueList l) => new(
        l.Id, l.Name, l.IsShared, l.AllowAddFromForm, (Contracts.Catalog.ValueListSortMode)l.SortMode,
        l.Options.OrderBy(o => o.SortOrder).Select(o => o.ToDto()).ToList(), l.Version);

    public static ValueListOptionDto ToDto(this ValueListOption o) => new(o.Id, o.Value, o.Color, o.SortOrder, o.IsArchived);

    public static TagDto ToDto(this Tag t, int itemCount) => new(t.Id, t.Name, t.Color, t.SortOrder, itemCount, t.Version);

    public static string? Trim(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

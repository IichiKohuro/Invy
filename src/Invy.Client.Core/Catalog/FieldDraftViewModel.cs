using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Invy.Contracts.Catalog;

namespace Invy.Client.Core.Catalog;

/// <summary>Тип данных характеристики для выбора в конструкторе.</summary>
public sealed record DataTypeOption(FieldDataType Type, string Title);

/// <summary>Характеристика в конструкторе типа: черновик до сохранения.</summary>
public sealed partial class FieldDraftViewModel : ObservableObject
{
    private static readonly HashSet<string> QuietProperties =
        [nameof(Error), nameof(NewListName), nameof(NewListOptions), nameof(IsExpanded), nameof(Summary)];

    public FieldDraftViewModel(FieldDefinitionDto? field, CatalogStore catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Id = field?.Id;
        PreviewId = field?.Id ?? Guid.NewGuid();
        Name = field?.Name ?? string.Empty;
        DataType = DataTypes.First(d => d.Type == (field?.DataType ?? FieldDataType.Text));
        Unit = field?.Unit;
        List = catalog.List(field?.ValueListId);
        GroupName = field?.GroupName;
        IsRequired = field?.IsRequired ?? false;
        ShowInList = field?.ShowInList ?? false;
        RememberLast = field?.RememberLast ?? false;
        Min = field?.Settings.Min is { } min ? (decimal)min : null;
        Max = field?.Settings.Max is { } max ? (decimal)max : null;
        Decimals = field?.Settings.Decimals;
        Placeholder = field?.Settings.Placeholder;
        DefaultValue = field?.Settings.DefaultValue;
        IsExpanded = field is null;
    }

    public static IReadOnlyList<DataTypeOption> DataTypes { get; } =
    [
        new(FieldDataType.Text, "Текст"),
        new(FieldDataType.LongText, "Длинный текст"),
        new(FieldDataType.Integer, "Целое число"),
        new(FieldDataType.Decimal, "Дробное число"),
        new(FieldDataType.Boolean, "Да / нет"),
        new(FieldDataType.Date, "Дата"),
        new(FieldDataType.Choice, "Выбор из списка"),
        new(FieldDataType.MultiChoice, "Несколько из списка"),
        new(FieldDataType.Url, "Ссылка (URL)"),
        new(FieldDataType.ItemRef, "Ссылка на вещь"),
    ];

    /// <summary>Id сохранённой характеристики; <c>null</c> — новая.</summary>
    public Guid? Id { get; }

    /// <summary>Постоянный id для предпросмотра формы (значения не сбрасываются при правке).</summary>
    public Guid PreviewId { get; }

    public bool IsNew => Id is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsList), nameof(IsNumber), nameof(IsDecimal), nameof(IsTextual), nameof(Summary))]
    public partial DataTypeOption DataType { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string? Unit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial ValueListDto? List { get; set; }

    [ObservableProperty]
    public partial string? GroupName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial bool IsRequired { get; set; }

    [ObservableProperty]
    public partial bool ShowInList { get; set; }

    [ObservableProperty]
    public partial bool RememberLast { get; set; }

    [ObservableProperty]
    public partial decimal? Min { get; set; }

    [ObservableProperty]
    public partial decimal? Max { get; set; }

    [ObservableProperty]
    public partial int? Decimals { get; set; }

    [ObservableProperty]
    public partial string? Placeholder { get; set; }

    [ObservableProperty]
    public partial string? DefaultValue { get; set; }

    /// <summary>Ошибка сервера или проверки — показывается у характеристики.</summary>
    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>Подробные настройки раскрыты (новая характеристика — сразу).</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>Новый справочник прямо отсюда: название и варианты через запятую.</summary>
    [ObservableProperty]
    public partial string? NewListName { get; set; }

    [ObservableProperty]
    public partial string? NewListOptions { get; set; }

    public bool NeedsList => DataType.Type is FieldDataType.Choice or FieldDataType.MultiChoice;

    public bool IsNumber => DataType.Type is FieldDataType.Integer or FieldDataType.Decimal;

    public bool IsDecimal => DataType.Type == FieldDataType.Decimal;

    public bool IsTextual => DataType.Type is FieldDataType.Text or FieldDataType.LongText or FieldDataType.Url;

    /// <summary>«Целое число, см · обязательно», «Выбор из списка «Разъёмы»».</summary>
    public string Summary
    {
        get
        {
            var text = DataType.Title;
            if (NeedsList)
            {
                text += List is null ? " — справочник не выбран" : $" «{List.Name}»";
            }
            else if (IsNumber && !string.IsNullOrWhiteSpace(Unit))
            {
                text += $", {Unit.Trim()}";
            }

            return IsRequired ? text + " · обязательно" : text;
        }
    }

    /// <summary>Изменилось то, что видно в форме вещи, — обновить предпросмотр.</summary>
    public event Action? Changed;

    internal FieldDefinitionInput ToInput() => new(
        Id,
        Name.Trim(),
        DataType.Type,
        IsNumber && !string.IsNullOrWhiteSpace(Unit) ? Unit.Trim() : null,
        NeedsList ? List?.Id : null,
        string.IsNullOrWhiteSpace(GroupName) ? null : GroupName.Trim(),
        IsRequired,
        ShowInList,
        RememberLast,
        Settings());

    internal FieldDefinitionDto ToPreview(Guid typeId, int sortOrder) => new(
        PreviewId,
        typeId,
        string.IsNullOrWhiteSpace(Name) ? "Без названия" : Name.Trim(),
        DataType.Type,
        IsNumber ? Unit : null,
        NeedsList ? List?.Id : null,
        GroupName,
        IsRequired,
        ShowInList,
        RememberLast,
        Settings(),
        sortOrder,
        IsArchived: false);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is { } name && !QuietProperties.Contains(name))
        {
            Changed?.Invoke();
        }
    }

    private FieldSettingsDto Settings() => new(
        IsNumber ? (double?)Min : null,
        IsNumber ? (double?)Max : null,
        IsDecimal ? Decimals : null,
        Placeholder: IsTextual && !string.IsNullOrWhiteSpace(Placeholder) ? Placeholder.Trim() : null,
        DefaultValue: string.IsNullOrWhiteSpace(DefaultValue) ? null : DefaultValue.Trim());
}

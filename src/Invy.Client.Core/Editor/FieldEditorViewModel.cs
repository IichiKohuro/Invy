using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Invy.Client.Core.Session;
using Invy.Contracts.Catalog;
using Invy.Contracts.Items;

namespace Invy.Client.Core.Editor;

/// <summary>
/// Поле формы вещи. Шаблон в UI выбирается по <see cref="TemplateKey"/> (тип данных характеристики),
/// значение хранится в свойстве своего вида: текст, число, дата, да/нет, вариант(ы) списка.
/// </summary>
public sealed partial class FieldEditorViewModel : ObservableObject
{
    private readonly ClientSession? _session;
    private Guid? _itemRef;

    public FieldEditorViewModel(FieldDefinitionDto definition, ValueListDto? list, ClientSession? session)
    {
        Definition = definition;
        _session = session;
        Options = new ObservableCollection<ValueListOptionDto>(list?.Options.Where(o => !o.IsArchived) ?? []);
        OptionToggles = new ObservableCollection<OptionToggle>(Options.Select(o => new OptionToggle(o)));
        ListId = list?.Id;
        AllowAddOption = list?.AllowAddFromForm ?? false;
    }

    public FieldDefinitionDto Definition { get; }

    public Guid FieldId => Definition.Id;

    public string TemplateKey => Definition.DataType.ToString();

    public string Label => Definition.IsRequired ? $"{Definition.Name} *" : Definition.Name;

    public string? Unit => Definition.Unit;

    public string? Placeholder => Definition.Settings.Placeholder;

    public string? GroupName => Definition.GroupName;

    public Guid? ListId { get; }

    public bool AllowAddOption { get; }

    public ObservableCollection<ValueListOptionDto> Options { get; }

    public ObservableCollection<OptionToggle> OptionToggles { get; }

    public int Decimals => Definition.DataType == FieldDataType.Integer ? 0 : Definition.Settings.Decimals ?? 2;

    /// <summary>Формат числа в поле ввода: «0» или «0.##».</summary>
    public string NumberFormat => Decimals == 0 ? "0" : "0." + new string('#', Decimals);

    public decimal Increment => Definition.DataType == FieldDataType.Integer ? 1 : 0.1m;

    public decimal Minimum => (decimal)(Definition.Settings.Min ?? -1_000_000_000);

    public decimal Maximum => (decimal)(Definition.Settings.Max ?? 1_000_000_000);

    [ObservableProperty]
    public partial string? Text { get; set; }

    [ObservableProperty]
    public partial decimal? Number { get; set; }

    [ObservableProperty]
    public partial DateTime? Date { get; set; }

    [ObservableProperty]
    public partial bool Bool { get; set; }

    [ObservableProperty]
    public partial ValueListOptionDto? SelectedOption { get; set; }

    [ObservableProperty]
    public partial string? NewOptionText { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>Для ссылок на вещь — название (выбор вещи появится позже).</summary>
    [ObservableProperty]
    public partial string? ItemRefName { get; set; }

    public void Load(FieldValueDto? value)
    {
        Text = value?.Text;
        Number = value?.Number is { } n ? (decimal)n : null;
        Date = value?.Date?.ToDateTime(TimeOnly.MinValue);
        Bool = value?.Bool ?? false;
        SelectedOption = value?.OptionIds is [var single, ..] ? Options.FirstOrDefault(o => o.Id == single) : null;
        foreach (var toggle in OptionToggles)
        {
            toggle.IsSelected = value?.OptionIds?.Contains(toggle.Option.Id) == true;
        }

        _itemRef = value?.ItemId;
        ItemRefName = value?.ItemId is null ? null : value.Display;
        Error = null;
    }

    /// <summary>Значение для запроса или <c>null</c>, если поле пустое.</summary>
    public FieldValueDto? ToDto()
    {
        var dto = Definition.DataType switch
        {
            FieldDataType.Integer or FieldDataType.Decimal => Number is { } n ? new FieldValueDto(FieldId, Number: (double)n) : null,
            FieldDataType.Boolean => new FieldValueDto(FieldId, Bool: Bool),
            FieldDataType.Date => Date is { } d ? new FieldValueDto(FieldId, Date: DateOnly.FromDateTime(d)) : null,
            FieldDataType.Choice => SelectedOption is { } o ? new FieldValueDto(FieldId, OptionIds: [o.Id]) : null,
            FieldDataType.MultiChoice => OptionToggles.Where(t => t.IsSelected).Select(t => t.Option.Id).ToList() is { Count: > 0 } ids ? new FieldValueDto(FieldId, OptionIds: ids) : null,
            FieldDataType.ItemRef => _itemRef is { } r ? new FieldValueDto(FieldId, ItemId: r) : null,
            _ => string.IsNullOrWhiteSpace(Text) ? null : new FieldValueDto(FieldId, Text: Text.Trim()),
        };
        return dto;
    }

    /// <summary>Текст значения для авто-названия (без единицы измерения).</summary>
    public string? DisplayValue => Definition.DataType switch
    {
        FieldDataType.Integer or FieldDataType.Decimal => Number?.ToString(Decimals == 0 ? "0" : "0.##", CultureInfo.GetCultureInfo("ru-RU")),
        FieldDataType.Boolean => Bool ? "Да" : "Нет",
        FieldDataType.Date => Date?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        FieldDataType.Choice => SelectedOption?.Value,
        FieldDataType.MultiChoice => string.Join(", ", OptionToggles.Where(t => t.IsSelected).Select(t => t.Option.Value)) is { Length: > 0 } s ? s : null,
        FieldDataType.ItemRef => ItemRefName,
        _ => string.IsNullOrWhiteSpace(Text) ? null : Text.Trim(),
    };

    /// <summary>«+ Добавить "Type-C"» прямо из формы.</summary>
    [RelayCommand]
    private async Task AddOptionAsync()
    {
        var value = NewOptionText?.Trim();
        if (_session is null || ListId is not { } listId || string.IsNullOrEmpty(value))
        {
            return;
        }

        await _session.TryAsync(async () =>
        {
            var option = await _session.Api.AddOptionAsync(listId, new AddOptionRequest(value));
            if (Options.All(o => o.Id != option.Id))
            {
                Options.Add(option);
                OptionToggles.Add(new OptionToggle(option));
            }

            if (Definition.DataType == FieldDataType.MultiChoice)
            {
                OptionToggles.First(t => t.Option.Id == option.Id).IsSelected = true;
            }
            else
            {
                SelectedOption = Options.First(o => o.Id == option.Id);
            }

            NewOptionText = null;
        });
    }

    partial void OnTextChanged(string? value) => Changed();

    partial void OnNumberChanged(decimal? value) => Changed();

    partial void OnDateChanged(DateTime? value) => Changed();

    partial void OnBoolChanged(bool value) => Changed();

    partial void OnSelectedOptionChanged(ValueListOptionDto? value) => Changed();

    /// <summary>Значение изменилось — редактор пересчитывает авто-название.</summary>
    public event Action? ValueChanged;

    private void Changed()
    {
        Error = null;
        ValueChanged?.Invoke();
    }
}

/// <summary>Вариант для множественного выбора.</summary>
public sealed partial class OptionToggle(ValueListOptionDto option) : ObservableObject
{
    public ValueListOptionDto Option { get; } = option;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

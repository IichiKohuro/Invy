using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Invy.Client.Core.Api;
using Invy.Client.Core.Catalog;
using Invy.Client.Core.Session;
using Invy.Contracts;
using Invy.Contracts.Catalog;
using Invy.Contracts.Items;

namespace Invy.Client.Core.Editor;

/// <summary>
/// Редактор вещи: тип, название (своё или авто по шаблону типа), где лежит, количество, характеристики типа и теги.
/// Ошибки сервера (422) показываются у своих полей, конфликт версий (409) — вопросом «чьи изменения оставить».
/// </summary>
public sealed partial class ItemEditorViewModel : ObservableObject
{
    private readonly ClientSession _session;
    private ItemDetailsDto? _item;
    private string? _locationName;
    private bool _loading;

    private ItemEditorViewModel(ClientSession session)
    {
        _session = session;
        var recent = session.Settings.Current.RecentTypeIds.ToList();
        Types = new ObservableCollection<ItemTypeDto>(session.Catalog.ActiveTypes
            .OrderBy(t => recent.IndexOf(t.Id) is var i and >= 0 ? i : int.MaxValue)
            .ThenBy(t => t.SortOrder)
            .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase));
        Tags = new ObservableCollection<TagToggle>(session.Catalog.Tags.Select(t => new TagToggle(t)));
    }

    /// <summary>Новая вещь в <paramref name="location"/> (<c>null</c> — «Неразобранное» или верхний уровень мест).</summary>
    public static ItemEditorViewModel ForNew(ClientSession session, MoveTarget? location, Guid? typeId = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        var editor = new ItemEditorViewModel(session);
        editor.SetLocation(location);
        editor.SelectedType = editor.Types.FirstOrDefault(t => t.Id == typeId) ?? editor.Types.FirstOrDefault();
        return editor;
    }

    public static ItemEditorViewModel ForExisting(ClientSession session, ItemDetailsDto item)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(item);
        var editor = new ItemEditorViewModel(session);
        editor.Load(item);
        return editor;
    }

    public bool IsNew => _item is null;

    public string Title => _item is { } item ? $"Изменить «{item.Name}»" : "Новая вещь";

    public ObservableCollection<ItemTypeDto> Types { get; }

    public ObservableCollection<FieldEditorViewModel> Fields { get; } = [];

    public bool HasFields => Fields.Count > 0;

    public ObservableCollection<TagToggle> Tags { get; }

    [ObservableProperty]
    public partial ItemTypeDto? SelectedType { get; set; }

    [ObservableProperty]
    public partial string? Name { get; set; }

    [ObservableProperty]
    public partial string? NameError { get; set; }

    /// <summary>Название, которое сложится из характеристик по шаблону типа (если своё не указано).</summary>
    [ObservableProperty]
    public partial string? AutoName { get; private set; }

    [ObservableProperty]
    public partial string? Description { get; set; }

    [ObservableProperty]
    public partial decimal? Quantity { get; set; } = 1;

    [ObservableProperty]
    public partial string? QuantityError { get; set; }

    [ObservableProperty]
    public partial string? Unit { get; set; }

    [ObservableProperty]
    public partial Guid? ParentId { get; private set; }

    [ObservableProperty]
    public partial string? LocationError { get; set; }

    [ObservableProperty]
    public partial IconDto? Icon { get; set; }

    [ObservableProperty]
    public partial string? Color { get; set; }

    [ObservableProperty]
    public partial string? NewTagText { get; set; }

    /// <summary>Общая ошибка, не относящаяся к конкретному полю.</summary>
    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>«Добавлено: …» после «Сохранить и добавить ещё».</summary>
    [ObservableProperty]
    public partial string? SavedMessage { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(SaveAndAddAnotherCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>Последняя сохранённая вещь — результат диалога.</summary>
    public ItemDetailsDto? Result { get; private set; }

    public bool HasTemplate => !string.IsNullOrWhiteSpace(SelectedType?.TitleTemplate);

    public string NamePlaceholder => AutoName ?? (HasTemplate ? "Сложится из характеристик" : "Например, «Серая коробка»");

    public string? NameHint => HasTemplate ? $"Оставьте пустым — название сложится по шаблону «{SelectedType!.TitleTemplate}»" : null;

    public string UnitPlaceholder => SelectedType?.DefaultUnit ?? "шт.";

    public string LocationText => ParentId is null
        ? SelectedType?.Role == TypeRole.Place ? "Места — верхний уровень" : "Неразобранное (без места)"
        : _locationName ?? "…";

    public bool CanSaveAndAddAnother => IsNew;

    /// <summary>Диалог можно закрыть: сохранено или отменено.</summary>
    public event Action? CloseRequested;

    /// <summary>Сохранить. Возвращает вещь или <c>null</c>, если есть ошибки — они показаны у полей.</summary>
    public async Task<ItemDetailsDto?> TrySaveAsync()
    {
        ClearErrors();
        SavedMessage = null;
        if (SelectedType is not { } type)
        {
            Error = "Выберите тип вещи";
            return null;
        }

        if (!ValidateLocally())
        {
            return null;
        }

        IsBusy = true;
        try
        {
            var request = BuildRequest(type);
            while (true)
            {
                try
                {
                    var saved = _item is { } existing
                        ? await _session.Api.UpdateItemAsync(existing.Id, request)
                        : await _session.Api.CreateItemAsync(request);
                    Remember(type, saved);
                    Result = saved;
                    return saved;
                }
                catch (ApiException ex) when (ex.IsConflict && _item is not null && ex.CurrentAs(InvyJsonContext.Default.ItemDetailsDto) is { } current)
                {
                    var choice = await _session.Dialogs.AskAsync(
                        "Вещь изменили на другом устройстве",
                        $"Пока вы редактировали «{current.Name}», её изменили на другом устройстве. Сохранить ваши изменения поверх или загрузить новую версию?",
                        "Сохранить мои",
                        "Загрузить новую");
                    if (choice == DialogChoice.Primary)
                    {
                        _item = current;
                        request = request with { Version = current.Version };
                        continue;
                    }

                    if (choice == DialogChoice.Secondary)
                    {
                        Load(current);
                    }

                    return null;
                }
            }
        }
        catch (ApiException ex) when (ex.IsValidation)
        {
            ShowErrors(ex.Errors);
        }
        catch (ApiException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            Error = "Вещь не найдена — возможно, её удалили на другом устройстве";
        }
        catch (ApiException ex)
        {
            Error = ex.UserMessage;
        }
        catch (HttpRequestException)
        {
            Error = "Нет связи с сервером Invy — изменения не сохранены";
        }
        finally
        {
            IsBusy = false;
        }

        return null;
    }

    private bool CanSave() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (await TrySaveAsync() is not null)
        {
            CloseRequested?.Invoke();
        }
    }

    /// <summary>Сохранить и сразу начать следующую вещь: тип, место, теги и «запоминаемые» характеристики остаются.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAndAddAnotherAsync()
    {
        if (await TrySaveAsync() is { } saved && IsNewAfterSave())
        {
            ResetForNext();
            SavedMessage = $"Добавлено: «{saved.Name}». Можно заполнять следующую.";
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    [RelayCommand]
    private async Task ChangeLocationAsync()
    {
        if (await _session.Dialogs.PickLocationAsync("Где лежит?", _session) is { } target)
        {
            SetLocation(target);
            LocationError = null;
        }
    }

    [RelayCommand]
    private async Task AddTagAsync()
    {
        var name = NewTagText?.Trim().TrimStart('#');
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (Tags.FirstOrDefault(t => string.Equals(t.Tag.Name, name, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            existing.IsSelected = true;
            NewTagText = null;
            return;
        }

        await _session.TryAsync(async () =>
        {
            var tag = await _session.Api.CreateTagAsync(new SaveTagRequest(name));
            Tags.Add(new TagToggle(tag) { IsSelected = true });
            NewTagText = null;
        });
    }

    partial void OnSelectedTypeChanged(ItemTypeDto? oldValue, ItemTypeDto? newValue)
    {
        var previous = new Dictionary<Guid, FieldValueDto>();
        foreach (var field in Fields)
        {
            field.ValueChanged -= UpdateAutoName;
            if (field.ToDto() is { } value)
            {
                previous[field.FieldId] = value;
            }
        }

        Fields.Clear();
        if (newValue is not null)
        {
            foreach (var definition in CatalogStore.EffectiveFields(newValue))
            {
                var list = _session.Catalog.List(definition.ValueListId);
                var field = new FieldEditorViewModel(definition, list, _session);
                field.Load(previous.GetValueOrDefault(definition.Id) ?? (_item is null ? InitialValue(definition, list) : null));
                field.ValueChanged += UpdateAutoName;
                Fields.Add(field);
            }

            if (_item is null && (string.IsNullOrWhiteSpace(Unit) || Unit == oldValue?.DefaultUnit))
            {
                Unit = newValue.DefaultUnit;
            }
        }

        OnPropertyChanged(nameof(HasFields));
        OnPropertyChanged(nameof(HasTemplate));
        OnPropertyChanged(nameof(NameHint));
        OnPropertyChanged(nameof(UnitPlaceholder));
        OnPropertyChanged(nameof(LocationText));
        UpdateAutoName();
    }

    partial void OnNameChanged(string? value) => NameError = null;

    partial void OnQuantityChanged(decimal? value) => QuantityError = null;

    private bool IsNewAfterSave() => _item is null;

    private void Load(ItemDetailsDto item)
    {
        _loading = true;
        _item = item;
        var type = Types.FirstOrDefault(t => t.Id == item.TypeId);
        if (type is null && _session.Catalog.Type(item.TypeId) is { } archived)
        {
            Types.Add(archived);   // тип в архиве — показываем, чтобы не потерять характеристики
            type = archived;
        }

        SelectedType = type;
        Name = item.IsNameAuto ? null : item.Name;
        Description = item.Description;
        Quantity = (decimal)item.Quantity;
        Unit = item.Unit;
        // В карточке — действующие иконка и цвет (свои или типа); в редакторе — только свои.
        Icon = item.Icon == type?.Icon ? null : item.Icon;
        Color = item.Color == type?.Color ? null : item.Color;
        _locationName = item.Path.Count > 0 ? string.Join(" › ", item.Path.Select(p => p.Name)) : null;
        ParentId = item.ParentId;
        foreach (var field in Fields)
        {
            field.Load(item.Values.FirstOrDefault(v => v.FieldId == field.FieldId));
        }

        foreach (var tag in Tags)
        {
            tag.IsSelected = item.TagIds.Contains(tag.Tag.Id);
        }

        ClearErrors();
        _loading = false;
        UpdateAutoName();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(CanSaveAndAddAnother));
    }

    private void SetLocation(MoveTarget? location)
    {
        _locationName = location?.Name;
        ParentId = location?.Id;
        OnPropertyChanged(nameof(LocationText));
    }

    private void ResetForNext()
    {
        _loading = true;
        _item = null;
        Name = null;
        Description = null;
        Quantity = 1;
        Icon = null;
        Color = null;
        foreach (var field in Fields)
        {
            field.Load(InitialValue(field.Definition, _session.Catalog.List(field.Definition.ValueListId)));
        }

        ClearErrors();
        _loading = false;
        UpdateAutoName();
    }

    /// <summary>Значение новой вещи: последнее введённое («запоминать последнее») или по умолчанию из настроек поля.</summary>
    private FieldValueDto? InitialValue(FieldDefinitionDto definition, ValueListDto? list)
    {
        if (definition.RememberLast && _session.RememberedValue(definition.Id) is { } remembered)
        {
            return remembered;
        }

        if (definition.Settings.DefaultValue is not { Length: > 0 } text)
        {
            return null;
        }

        return definition.DataType switch
        {
            FieldDataType.Integer or FieldDataType.Decimal =>
                double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? new FieldValueDto(definition.Id, Number: n) : null,
            FieldDataType.Boolean => bool.TryParse(text, out var b) ? new FieldValueDto(definition.Id, Bool: b) : null,
            FieldDataType.Choice or FieldDataType.MultiChoice =>
                list?.Options.FirstOrDefault(o => !o.IsArchived && string.Equals(o.Value, text, StringComparison.OrdinalIgnoreCase)) is { } option
                    ? new FieldValueDto(definition.Id, OptionIds: [option.Id])
                    : null,
            FieldDataType.Date or FieldDataType.ItemRef => null,
            _ => new FieldValueDto(definition.Id, Text: text),
        };
    }

    private void UpdateAutoName()
    {
        if (_loading)
        {
            return;
        }

        var values = new Dictionary<string, string?>();
        foreach (var field in Fields)
        {
            values[field.Definition.Name] = field.DisplayValue;
        }

        AutoName = TitleRenderer.Render(SelectedType?.TitleTemplate, values);
        OnPropertyChanged(nameof(NamePlaceholder));
    }

    private bool ValidateLocally()
    {
        var valid = true;
        foreach (var field in Fields.Where(f => f.Definition.IsRequired && f.ToDto() is null))
        {
            field.Error = "Заполните поле";
            valid = false;
        }

        if (string.IsNullOrWhiteSpace(Name) && AutoName is null)
        {
            NameError = HasTemplate ? "Укажите название или заполните характеристики из шаблона" : "Укажите название";
            valid = false;
        }

        if (Quantity is < 0)
        {
            QuantityError = "Количество не может быть отрицательным";
            valid = false;
        }

        return valid;
    }

    private SaveItemRequest BuildRequest(ItemTypeDto type) => new(
        type.Id,
        ParentId,
        string.IsNullOrWhiteSpace(Name) ? null : Name.Trim(),
        string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
        (double)(Quantity ?? 1),
        string.IsNullOrWhiteSpace(Unit) ? null : Unit.Trim(),
        _item?.MinQuantity,
        Icon,
        Color,
        Fields.Select(f => f.ToDto()).OfType<FieldValueDto>().ToList(),
        Tags.Where(t => t.IsSelected).Select(t => t.Tag.Id).ToList(),
        _item?.Version);

    private void Remember(ItemTypeDto type, ItemDetailsDto saved)
    {
        _session.RememberType(type.Id);
        if (saved.ParentId is { } parentId)
        {
            _session.RememberPlace(parentId);
        }

        foreach (var field in Fields.Where(f => f.Definition.RememberLast))
        {
            if (field.ToDto() is { } value)
            {
                _session.RememberValue(value);
            }
        }
    }

    private void ShowErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        var other = new List<string>();
        foreach (var (key, messages) in errors)
        {
            var text = string.Join(" ", messages);
            switch (key.ToLowerInvariant())
            {
                case "name":
                    NameError = text;
                    break;
                case "parentid":
                    LocationError = text;
                    break;
                case "quantity":
                    QuantityError = text;
                    break;
                default:
                    if (FieldIdFromKey(key) is { } fieldId && Fields.FirstOrDefault(f => f.FieldId == fieldId) is { } field)
                    {
                        field.Error = text;
                    }
                    else
                    {
                        other.Add(text);
                    }

                    break;
            }
        }

        Error = other.Count > 0 ? string.Join("\n", other) : null;
    }

    /// <summary>Ключ ошибки характеристики: <c>values[{id}]</c>.</summary>
    private static Guid? FieldIdFromKey(string key) =>
        key.StartsWith("values[", StringComparison.OrdinalIgnoreCase) && key.EndsWith(']') && Guid.TryParse(key[7..^1], out var id) ? id : null;

    private void ClearErrors()
    {
        Error = null;
        NameError = null;
        QuantityError = null;
        LocationError = null;
        foreach (var field in Fields)
        {
            field.Error = null;
        }
    }
}

/// <summary>Тег в редакторе: выбран или нет.</summary>
public sealed partial class TagToggle(TagDto tag) : ObservableObject
{
    public TagDto Tag { get; } = tag;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

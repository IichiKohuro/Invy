using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Invy.Client.Core.Api;
using Invy.Client.Core.Editor;
using Invy.Client.Core.Session;
using Invy.Contracts;
using Invy.Contracts.Catalog;

namespace Invy.Client.Core.Catalog;

/// <summary>Роль типа: что можно класть внутрь.</summary>
public sealed record RoleOption(TypeRole Role, string Title, string Hint);

/// <summary>Базовый тип: его характеристики наследуются; <see cref="Id"/> = null — без базового.</summary>
public sealed record BaseTypeOption(Guid? Id, string Name);

/// <summary>
/// Конструктор типа: название, роль, базовый тип, иконка, шаблон авто-названия, характеристики.
/// Справа — живой предпросмотр формы вещи этого типа.
/// </summary>
public sealed partial class TypeEditorViewModel : ObservableObject, ILookEditor
{
    private readonly ClientSession _session;
    private ItemTypeDto? _type;
    private int _previewVersion;
    private bool _loading;

    private TypeEditorViewModel(ClientSession session, ItemTypeDto? type)
    {
        _session = session;
        _type = type;
        BaseTypes =
        [
            new BaseTypeOption(null, "Без базового типа"),
            .. session.Catalog.ActiveTypes.Where(t => t.Id != type?.Id).OrderBy(t => t.Name, StringComparer.CurrentCulture).Select(t => new BaseTypeOption(t.Id, t.Name)),
        ];
        Fields.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasFields));
            SchedulePreview();
        };
    }

    public static IReadOnlyList<RoleOption> Roles { get; } =
    [
        new(TypeRole.Item, "Вещь", "Кабель, инструмент, книга"),
        new(TypeRole.Container, "Контейнер", "Коробка, пакет, ящик — внутри лежат вещи"),
        new(TypeRole.Place, "Место", "Комната, чердак, сарай — внутри что угодно"),
    ];

    public static TypeEditorViewModel ForNew(ClientSession session, TypeRole role = TypeRole.Item)
    {
        ArgumentNullException.ThrowIfNull(session);
        var editor = new TypeEditorViewModel(session, null)
        {
            Role = Roles.First(r => r.Role == role),
        };
        editor.BaseType = editor.BaseTypes[0];
        editor.Name = string.Empty;
        editor.RefreshPreview();
        return editor;
    }

    public static TypeEditorViewModel ForExisting(ClientSession session, ItemTypeDto type)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(type);
        var editor = new TypeEditorViewModel(session, type);
        editor.Load(type);
        return editor;
    }

    public string Title => _type is { } type ? $"Тип «{type.Name}»" : "Новый тип";

    public bool IsNew => _type is null;

    public IReadOnlyList<BaseTypeOption> BaseTypes { get; }

    public ObservableCollection<FieldDraftViewModel> Fields { get; } = [];

    public bool HasFields => Fields.Count > 0;

    /// <summary>Характеристики базового типа — в форме идут первыми, здесь только для справки.</summary>
    public IReadOnlyList<FieldDefinitionDto> InheritedFields =>
        BaseType?.Id is { } baseId && _session.Catalog.Type(baseId) is { } baseType ? CatalogStore.EffectiveFields(baseType) : [];

    public bool HasInheritedFields => InheritedFields.Count > 0;

    public IReadOnlyList<ValueListDto> Lists => _session.Catalog.Lists;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? NameError { get; set; }

    [ObservableProperty]
    public partial RoleOption Role { get; set; } = Roles[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InheritedFields), nameof(HasInheritedFields))]
    public partial BaseTypeOption? BaseType { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOwnLook))]
    public partial IconDto? Icon { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOwnLook))]
    public partial string? Color { get; set; }

    public bool HasOwnLook => Icon is not null || Color is not null;

    /// <summary>Шаблон авто-названия: «{Разъём 1} → {Разъём 2}, {Длина} см».</summary>
    [ObservableProperty]
    public partial string? TitleTemplate { get; set; }

    [ObservableProperty]
    public partial string? TitleTemplateError { get; set; }

    [ObservableProperty]
    public partial string? DefaultUnit { get; set; }

    [ObservableProperty]
    public partial FieldDraftViewModel? SelectedField { get; set; }

    /// <summary>Форма вещи этого типа — как она будет выглядеть.</summary>
    [ObservableProperty]
    public partial ItemEditorViewModel? Preview { get; private set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsBusy { get; private set; }

    public ItemTypeDto? Result { get; private set; }

    /// <summary>Задержка обновления предпросмотра после правки; <see cref="TimeSpan.Zero"/> — сразу (тесты).</summary>
    public TimeSpan PreviewDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    public event Action? CloseRequested;

    /// <summary>Пересобрать предпросмотр сейчас (в UI — с небольшой задержкой после правок).</summary>
    public void RefreshPreview()
    {
        var typeId = _type?.Id ?? Guid.Empty;
        var fields = Fields.Select((f, i) => f.ToPreview(typeId, i)).ToList();
        var preview = new ItemTypeDto(
            typeId,
            string.IsNullOrWhiteSpace(Name) ? "Новый тип" : Name.Trim(),
            BaseType?.Id,
            Role.Role,
            Icon,
            Color,
            TitleTemplate,
            DefaultUnit,
            0,
            false,
            fields,
            InheritedFields,
            0);
        if (Preview is null)
        {
            Preview = ItemEditorViewModel.ForPreview(_session, preview);
        }
        else
        {
            Preview.ShowPreviewType(preview);
        }

        ValidateTemplate();
    }

    public async Task<ItemTypeDto?> TrySaveAsync()
    {
        ClearErrors();
        if (!ValidateLocally())
        {
            return null;
        }

        IsBusy = true;
        try
        {
            var request = BuildRequest();
            while (true)
            {
                try
                {
                    var saved = _type is { } existing
                        ? await _session.Api.UpdateTypeAsync(existing.Id, request)
                        : await _session.Api.CreateTypeAsync(request);
                    await _session.Catalog.LoadAsync(_session.Api);
                    Result = saved;
                    return saved;
                }
                catch (ApiException ex) when (ex.IsConflict && _type is not null && ex.CurrentAs(InvyJsonContext.Default.ItemTypeDto) is { } current)
                {
                    var choice = await _session.Dialogs.AskAsync(
                        "Тип изменили на другом устройстве",
                        $"Пока вы редактировали тип «{current.Name}», его изменили на другом устройстве. Сохранить ваши изменения поверх или загрузить новую версию?",
                        "Сохранить мои",
                        "Загрузить новую");
                    if (choice == DialogChoice.Primary)
                    {
                        _type = current;
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
            Error = "Тип не найден — возможно, его удалили на другом устройстве";
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

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    [RelayCommand]
    private void AddField()
    {
        var field = new FieldDraftViewModel(null, _session.Catalog);
        Track(field);
        Fields.Add(field);
        SelectedField = field;
    }

    [RelayCommand]
    private void RemoveField(FieldDraftViewModel? field)
    {
        if (field is not null && Fields.Remove(field))
        {
            field.Changed -= SchedulePreview;
        }
    }

    [RelayCommand]
    private void MoveFieldUp(FieldDraftViewModel? field) => Move(field, -1);

    [RelayCommand]
    private void MoveFieldDown(FieldDraftViewModel? field) => Move(field, +1);

    /// <summary>Добавить характеристику в шаблон названия: «{Длина}».</summary>
    [RelayCommand]
    private void InsertIntoTemplate(FieldDraftViewModel? field)
    {
        if (field is { Name.Length: > 0 })
        {
            TitleTemplate = string.IsNullOrWhiteSpace(TitleTemplate) ? $"{{{field.Name.Trim()}}}" : $"{TitleTemplate.TrimEnd()} {{{field.Name.Trim()}}}";
        }
    }

    /// <summary>Новый справочник для характеристики-списка: название и варианты через запятую.</summary>
    [RelayCommand]
    private async Task CreateListAsync(FieldDraftViewModel? field)
    {
        if (field is null)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(field.NewListName) ? field.Name.Trim() : field.NewListName.Trim();
        var options = (field.NewListOptions ?? string.Empty)
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(v => new ValueListOptionInput(null, v))
            .ToList();
        if (name.Length == 0)
        {
            field.Error = "Укажите название справочника";
            return;
        }

        await _session.TryAsync(
            async () =>
            {
                var list = await _session.Api.CreateListAsync(new SaveValueListRequest(name, options));
                await _session.Catalog.LoadAsync(_session.Api);
                OnPropertyChanged(nameof(Lists));
                field.List = _session.Catalog.List(list.Id);
                field.NewListName = null;
                field.NewListOptions = null;
                field.Error = null;
            },
            "Справочник не создан");
    }

    [RelayCommand]
    private void SetEmoji(string? emoji) => Icon = string.IsNullOrEmpty(emoji) ? null : new IconDto(IconKind.Emoji, emoji);

    [RelayCommand]
    private void SetSymbol(string? symbol) => Icon = string.IsNullOrEmpty(symbol) ? null : new IconDto(IconKind.Fluent, symbol);

    [RelayCommand]
    private void SetColor(string? color) => Color = string.IsNullOrEmpty(color) ? null : color;

    [RelayCommand]
    private void ResetLook()
    {
        Icon = null;
        Color = null;
    }

    partial void OnNameChanged(string value)
    {
        NameError = null;
        SchedulePreview();
    }

    partial void OnRoleChanged(RoleOption value) => SchedulePreview();

    partial void OnBaseTypeChanged(BaseTypeOption? value) => SchedulePreview();

    partial void OnIconChanged(IconDto? value) => SchedulePreview();

    partial void OnColorChanged(string? value) => SchedulePreview();

    partial void OnTitleTemplateChanged(string? value) => SchedulePreview();

    partial void OnDefaultUnitChanged(string? value) => SchedulePreview();

    private void Load(ItemTypeDto type)
    {
        _loading = true;
        _type = type;
        Name = type.Name;
        Role = Roles.First(r => r.Role == type.Role);
        BaseType = BaseTypes.FirstOrDefault(b => b.Id == type.BaseTypeId) ?? BaseTypes[0];
        Icon = type.Icon;
        Color = type.Color;
        TitleTemplate = type.TitleTemplate;
        DefaultUnit = type.DefaultUnit;
        foreach (var field in Fields)
        {
            field.Changed -= SchedulePreview;
        }

        Fields.Clear();
        foreach (var definition in type.Fields.Where(f => !f.IsArchived).OrderBy(f => f.SortOrder))
        {
            var field = new FieldDraftViewModel(definition, _session.Catalog);
            Track(field);
            Fields.Add(field);
        }

        _loading = false;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsNew));
        RefreshPreview();
    }

    private void Track(FieldDraftViewModel field) => field.Changed += SchedulePreview;

    private void Move(FieldDraftViewModel? field, int delta)
    {
        var index = field is null ? -1 : Fields.IndexOf(field);
        var target = index + delta;
        if (index >= 0 && target >= 0 && target < Fields.Count)
        {
            Fields.Move(index, target);
        }
    }

    private async void SchedulePreview()
    {
        if (_loading || Preview is null)
        {
            return;
        }

        if (PreviewDelay <= TimeSpan.Zero)
        {
            RefreshPreview();
            return;
        }

        var version = ++_previewVersion;
        await Task.Delay(PreviewDelay);
        if (version == _previewVersion)
        {
            RefreshPreview();
        }
    }

    private void ValidateTemplate()
    {
        var names = Fields.Select(f => f.Name).Concat(InheritedFields.Select(f => f.Name)).ToList();
        var unknown = TitleRenderer.FieldNames(TitleTemplate).FirstOrDefault(n => !names.Any(f => TitleRenderer.SameName(f, n)));
        TitleTemplateError = unknown is null ? null : $"Нет характеристики «{unknown}»";
    }

    private bool ValidateLocally()
    {
        var valid = true;
        if (string.IsNullOrWhiteSpace(Name))
        {
            NameError = "Укажите название типа";
            valid = false;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name))
            {
                field.Error = "Укажите название характеристики";
            }
            else if (!seen.Add(field.Name.Trim()))
            {
                field.Error = $"Характеристика «{field.Name.Trim()}» повторяется";
            }
            else if (field.NeedsList && field.List is null)
            {
                field.Error = "Выберите справочник или создайте новый";
            }
            else
            {
                continue;
            }

            field.IsExpanded = true;
            valid = false;
        }

        ValidateTemplate();
        return valid && TitleTemplateError is null;
    }

    private SaveItemTypeRequest BuildRequest() => new(
        Name.Trim(),
        Role.Role,
        Fields.Select(f => f.ToInput()).ToList(),
        BaseType?.Id,
        Icon,
        Color,
        string.IsNullOrWhiteSpace(TitleTemplate) ? null : TitleTemplate.Trim(),
        string.IsNullOrWhiteSpace(DefaultUnit) ? null : DefaultUnit.Trim(),
        _type?.SortOrder ?? 0,
        _type?.IsArchived ?? false,
        _type?.Version);

    private void ShowErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        var other = new List<string>();
        foreach (var (key, messages) in errors)
        {
            var text = string.Join(" ", messages);
            if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                NameError = text;
            }
            else if (key.Equals("titleTemplate", StringComparison.OrdinalIgnoreCase))
            {
                TitleTemplateError = text;
            }
            else if (key.StartsWith("fields[", StringComparison.OrdinalIgnoreCase) && key.EndsWith(']')
                && int.TryParse(key[7..^1], out var index) && index >= 0 && index < Fields.Count)
            {
                Fields[index].Error = text;
                Fields[index].IsExpanded = true;
            }
            else
            {
                other.Add(text);
            }
        }

        Error = other.Count > 0 ? string.Join("\n", other) : null;
    }

    private void ClearErrors()
    {
        Error = null;
        NameError = null;
        TitleTemplateError = null;
        foreach (var field in Fields)
        {
            field.Error = null;
        }
    }
}

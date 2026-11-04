using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Invy.Client.Core.Api;
using Invy.Client.Core.Session;
using Invy.Contracts;
using Invy.Contracts.Catalog;

namespace Invy.Client.Core.Catalog;

/// <summary>Вариант справочника в редакторе.</summary>
public sealed partial class OptionDraftViewModel(Guid? id, string value, string? color, bool isArchived) : ObservableObject
{
    public Guid? Id { get; } = id;

    [ObservableProperty]
    public partial string Value { get; set; } = value;

    [ObservableProperty]
    public partial string? Color { get; set; } = color;

    /// <summary>Скрыт из выбора; у вещей значение остаётся.</summary>
    [ObservableProperty]
    public partial bool IsArchived { get; set; } = isArchived;
}

/// <summary>Справочник: «Разъёмы» — Type-A, Type-C, HDMI… Общий для нескольких характеристик.</summary>
public sealed partial class ValueListEditorViewModel : ObservableObject
{
    private readonly ClientSession _session;
    private ValueListDto? _list;

    public ValueListEditorViewModel(ClientSession session, ValueListDto? list)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        Load(list);
        Options.CollectionChanged += (_, _) => IsDirty = true;
    }

    public bool IsNew => _list is null;

    public Guid? ListId => _list?.Id;

    public ObservableCollection<OptionDraftViewModel> Options { get; } = [];

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? NameError { get; set; }

    [ObservableProperty]
    public partial bool AllowAddFromForm { get; set; } = true;

    [ObservableProperty]
    public partial bool IsAlphabetical { get; set; }

    [ObservableProperty]
    public partial string? NewOptionText { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>Есть несохранённые изменения.</summary>
    [ObservableProperty]
    public partial bool IsDirty { get; private set; }

    /// <summary>«Используется: Кабель (Разъём 1, Разъём 2), Переходник (Вход)».</summary>
    public string UsageText
    {
        get
        {
            if (_list is null)
            {
                return "Новый справочник";
            }

            var usage = _session.Catalog.Types
                .Select(t => (Type: t, Fields: t.Fields.Where(f => !f.IsArchived && f.ValueListId == _list.Id).Select(f => f.Name).ToList()))
                .Where(x => x.Fields.Count > 0)
                .Select(x => $"{x.Type.Name} ({string.Join(", ", x.Fields)})")
                .ToList();
            return usage.Count == 0 ? "Пока не используется" : $"Используется: {string.Join(", ", usage)}";
        }
    }

    public event Action<ValueListDto>? Saved;

    public async Task<ValueListDto?> TrySaveAsync()
    {
        NameError = null;
        Error = null;
        if (string.IsNullOrWhiteSpace(Name))
        {
            NameError = "Укажите название справочника";
            return null;
        }

        var request = new SaveValueListRequest(
            Name.Trim(),
            Options.Where(o => !string.IsNullOrWhiteSpace(o.Value)).Select(o => new ValueListOptionInput(o.Id, o.Value.Trim(), o.Color, o.IsArchived)).ToList(),
            _list?.IsShared ?? true,
            AllowAddFromForm,
            IsAlphabetical ? ValueListSortMode.Alphabetical : ValueListSortMode.Manual,
            _list?.Version);
        try
        {
            while (true)
            {
                try
                {
                    var saved = _list is { } existing
                        ? await _session.Api.UpdateListAsync(existing.Id, request)
                        : await _session.Api.CreateListAsync(request);
                    Load(saved);
                    await _session.Catalog.LoadAsync(_session.Api);
                    Saved?.Invoke(saved);
                    return saved;
                }
                catch (ApiException ex) when (ex.IsConflict && _list is not null && ex.CurrentAs(InvyJsonContext.Default.ValueListDto) is { } current)
                {
                    var choice = await _session.Dialogs.AskAsync(
                        "Справочник изменили на другом устройстве",
                        $"Пока вы редактировали «{current.Name}», его изменили на другом устройстве. Сохранить ваши изменения поверх или загрузить новую версию?",
                        "Сохранить мои",
                        "Загрузить новую");
                    if (choice == DialogChoice.Primary)
                    {
                        _list = current;
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
            NameError = ex.Errors.TryGetValue("name", out var name) ? string.Join(" ", name) : null;
            Error = string.Join("\n", ex.Errors.Where(e => e.Key != "name").SelectMany(e => e.Value)) is { Length: > 0 } other ? other : null;
        }
        catch (ApiException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            Error = "Справочник не найден — возможно, его удалили на другом устройстве";
        }
        catch (ApiException ex)
        {
            Error = ex.UserMessage;
        }
        catch (HttpRequestException)
        {
            Error = "Нет связи с сервером Invy — изменения не сохранены";
        }

        return null;
    }

    /// <summary>Перечитать с сервера, если нет своих несохранённых правок.</summary>
    public void Refresh(ValueListDto? list)
    {
        if (!IsDirty && list is not null && list.Id == _list?.Id && list.Version != _list.Version)
        {
            Load(list);
        }

        OnPropertyChanged(nameof(UsageText));
    }

    [RelayCommand]
    private async Task SaveAsync() => await TrySaveAsync();

    [RelayCommand]
    private void AddOption()
    {
        foreach (var value in (NewOptionText ?? string.Empty).Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Options.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase)) is { } existing)
            {
                existing.IsArchived = false;
            }
            else
            {
                Track(new OptionDraftViewModel(null, value, null, false));
            }
        }

        NewOptionText = null;
    }

    /// <summary>Новый вариант — убрать; сохранённый — в архив (у вещей значение останется) или обратно.</summary>
    [RelayCommand]
    private void RemoveOption(OptionDraftViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        if (option.Id is null)
        {
            Options.Remove(option);
        }
        else
        {
            option.IsArchived = !option.IsArchived;
        }
    }

    [RelayCommand]
    private void MoveOptionUp(OptionDraftViewModel? option) => Move(option, -1);

    [RelayCommand]
    private void MoveOptionDown(OptionDraftViewModel? option) => Move(option, +1);

    partial void OnNameChanged(string value) => IsDirty = true;

    partial void OnAllowAddFromFormChanged(bool value) => IsDirty = true;

    partial void OnIsAlphabeticalChanged(bool value) => IsDirty = true;

    private void Track(OptionDraftViewModel option)
    {
        option.PropertyChanged += (_, _) => IsDirty = true;
        Options.Add(option);
    }

    private void Move(OptionDraftViewModel? option, int delta)
    {
        var index = option is null ? -1 : Options.IndexOf(option);
        var target = index + delta;
        if (index >= 0 && target >= 0 && target < Options.Count)
        {
            Options.Move(index, target);
        }
    }

    private void Load(ValueListDto? list)
    {
        _list = list;
        Name = list?.Name ?? string.Empty;
        AllowAddFromForm = list?.AllowAddFromForm ?? true;
        IsAlphabetical = list?.SortMode == ValueListSortMode.Alphabetical;
        Options.Clear();
        foreach (var option in list?.Options.OrderBy(o => o.SortOrder) ?? Enumerable.Empty<ValueListOptionDto>())
        {
            Track(new OptionDraftViewModel(option.Id, option.Value, option.Color, option.IsArchived));
        }

        IsDirty = false;
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(ListId));
        OnPropertyChanged(nameof(UsageText));
    }
}

/// <summary>Вкладка «Справочники»: список слева, редактор выбранного справа.</summary>
public sealed partial class ValueListsViewModel : ObservableObject, IDisposable
{
    private readonly ClientSession _session;

    public ValueListsViewModel(ClientSession session)
    {
        _session = session;
        session.Catalog.Changed += Refresh;
        Refresh();
    }

    public bool CanEdit => _session.CanEdit;

    public ObservableCollection<ValueListDto> Lists { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial ValueListDto? Selected { get; set; }

    [ObservableProperty]
    public partial ValueListEditorViewModel? Editor { get; private set; }

    public void Dispose() => _session.Catalog.Changed -= Refresh;

    [RelayCommand]
    private void NewList()
    {
        Selected = null;
        Editor = CreateEditor(null);
    }

    private bool CanDelete() => CanEdit && Selected is not null;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        if (Selected is not { } list
            || await _session.Dialogs.AskAsync($"Удалить справочник «{list.Name}»?", "Удалить можно только справочник, который не используется характеристиками.", "Удалить") != DialogChoice.Primary)
        {
            return;
        }

        if (await _session.TryAsync(() => _session.Api.DeleteListAsync(list.Id), "Справочник не удалён"))
        {
            Selected = null;
            Editor = null;
            await _session.Catalog.LoadAsync(_session.Api);
        }
    }

    partial void OnSelectedChanged(ValueListDto? value)
    {
        if (value is not null && value.Id != Editor?.ListId)
        {
            Editor = CreateEditor(value);
        }
    }

    private ValueListEditorViewModel CreateEditor(ValueListDto? list)
    {
        var editor = new ValueListEditorViewModel(_session, list);
        editor.Saved += saved => Selected = Lists.FirstOrDefault(l => l.Id == saved.Id);
        return editor;
    }

    private void Refresh()
    {
        var selectedId = Selected?.Id;
        Lists.Clear();
        foreach (var list in _session.Catalog.Lists.OrderBy(l => l.Name, StringComparer.CurrentCulture))
        {
            Lists.Add(list);
        }

        var selected = selectedId is { } id ? Lists.FirstOrDefault(l => l.Id == id) : null;
        Selected = selected;
        Editor?.Refresh(selected);
    }
}

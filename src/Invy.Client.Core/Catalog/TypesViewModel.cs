using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Invy.Client.Core.Explorer;
using Invy.Client.Core.Session;
using Invy.Client.Core.Shell;
using Invy.Contracts.Catalog;

namespace Invy.Client.Core.Catalog;

/// <summary>Тип в списке: «Кабель — Вещь · 5 характеристик: Длина, Разъём 1…».</summary>
public sealed class TypeRowViewModel(ItemTypeDto type, string? groupTitle = null)
{
    public ItemTypeDto Type { get; } = type;

    /// <summary>Заголовок группы над первой строкой роли («Места», «Контейнеры», «Вещи»).</summary>
    public string? GroupTitle { get; } = groupTitle;

    public bool IsGroupStart => GroupTitle is not null;

    public string Name => Type.Name;

    public bool IsArchived => Type.IsArchived;

    public string Subtitle
    {
        get
        {
            var fields = CatalogStore.EffectiveFields(Type);
            var text = fields.Count == 0 ? "без характеристик"
                : $"{ItemVisuals.Plural(fields.Count, "характеристика", "характеристики", "характеристик")}: {string.Join(", ", fields.Take(4).Select(f => f.Name))}{(fields.Count > 4 ? "…" : string.Empty)}";
            return Type.IsArchived ? $"В архиве · {text}" : text;
        }
    }
}

/// <summary>Группа типов по роли: места, контейнеры, вещи.</summary>
public sealed record TypeGroup(string Title, IReadOnlyList<TypeRowViewModel> Types);

/// <summary>Страница «Типы»: типы по ролям, конструктор, архив; вкладка «Справочники».</summary>
public sealed partial class TypesViewModel : PageViewModel, IDisposable
{
    private readonly ClientSession _session;

    public TypesViewModel(ClientSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        Lists = new ValueListsViewModel(session);
        session.Catalog.Changed += Refresh;
        Refresh();
    }

    public override string Title => "Типы";

    public bool CanEdit => _session.CanEdit;

    public ObservableCollection<TypeGroup> Groups { get; } = [];

    /// <summary>Все типы одним списком; у первого в группе — заголовок группы.</summary>
    public ObservableCollection<TypeRowViewModel> Rows { get; } = [];

    public ValueListsViewModel Lists { get; }

    [ObservableProperty]
    public partial bool ShowArchived { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(ToggleArchiveCommand), nameof(DeleteCommand))]
    public partial TypeRowViewModel? Selected { get; set; }

    public bool IsEmpty => !_session.Catalog.Types.Any();

    public void Dispose()
    {
        _session.Catalog.Changed -= Refresh;
        Lists.Dispose();
    }

    [RelayCommand]
    private async Task NewTypeAsync(TypeRole? role)
    {
        if (!CanEdit)
        {
            return;
        }

        var editor = TypeEditorViewModel.ForNew(_session, role ?? TypeRole.Item);
        if (await _session.Dialogs.EditTypeAsync(editor) is { } saved)
        {
            Refresh();
            Select(saved.Id);
        }
    }

    private bool HasSelection(TypeRowViewModel? row) => CanEdit && (row ?? Selected) is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task EditAsync(TypeRowViewModel? row)
    {
        if ((row ?? Selected) is not { } target)
        {
            return;
        }

        if (await _session.Dialogs.EditTypeAsync(TypeEditorViewModel.ForExisting(_session, target.Type)) is { } saved)
        {
            Refresh();
            Select(saved.Id);
        }
    }

    /// <summary>В архив: тип пропадает из выбора, вещи этого типа остаются. Из архива — обратно.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ToggleArchiveAsync(TypeRowViewModel? row)
    {
        if ((row ?? Selected) is not { } target)
        {
            return;
        }

        var type = target.Type;
        var request = new SaveItemTypeRequest(
            type.Name,
            type.Role,
            type.Fields.Where(f => !f.IsArchived).Select(f => new FieldDefinitionInput(
                f.Id, f.Name, f.DataType, f.Unit, f.ValueListId, f.GroupName, f.IsRequired, f.ShowInList, f.RememberLast, f.Settings)).ToList(),
            type.BaseTypeId,
            type.Icon,
            type.Color,
            type.TitleTemplate,
            type.DefaultUnit,
            type.SortOrder,
            !type.IsArchived,
            type.Version);
        if (await _session.TryAsync(() => _session.Api.UpdateTypeAsync(type.Id, request), $"«{type.Name}»"))
        {
            _session.Dialogs.Notify(type.IsArchived ? $"«{type.Name}» снова доступен" : $"«{type.Name}» — в архиве", NotifyKind.Success);
            await _session.Catalog.LoadAsync(_session.Api);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync(TypeRowViewModel? row)
    {
        if ((row ?? Selected) is not { } target)
        {
            return;
        }

        if (await _session.Dialogs.AskAsync($"Удалить тип «{target.Name}»?", "Удалить можно только тип, вещей которого нет. Иначе отправьте его в архив.", "Удалить") != DialogChoice.Primary)
        {
            return;
        }

        if (await _session.TryAsync(() => _session.Api.DeleteTypeAsync(target.Type.Id), "Тип не удалён"))
        {
            _session.Dialogs.Notify($"Тип «{target.Name}» удалён", NotifyKind.Success);
            Selected = null;
            await _session.Catalog.LoadAsync(_session.Api);
        }
    }

    [RelayCommand]
    private Task ApplyStarterKitAsync() => StarterKit.ApplyAsync(_session);

    partial void OnShowArchivedChanged(bool value) => Refresh();

    private void Refresh()
    {
        var selectedId = Selected?.Type.Id;
        Groups.Clear();
        Rows.Clear();
        var types = _session.Catalog.Types.Where(t => ShowArchived || !t.IsArchived).ToList();
        foreach (var (role, title) in new[] { (TypeRole.Place, "Места"), (TypeRole.Container, "Контейнеры"), (TypeRole.Item, "Вещи") })
        {
            var rows = types.Where(t => t.Role == role)
                .OrderBy(t => t.IsArchived)
                .ThenBy(t => t.SortOrder)
                .ThenBy(t => t.Name, StringComparer.CurrentCulture)
                .Select((t, i) => new TypeRowViewModel(t, i == 0 ? title : null))
                .ToList();
            if (rows.Count > 0)
            {
                Groups.Add(new TypeGroup(title, rows));
                foreach (var row in rows)
                {
                    Rows.Add(row);
                }
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
        Select(selectedId);
    }

    private void Select(Guid? id) =>
        Selected = id is null ? null : Rows.FirstOrDefault(t => t.Type.Id == id);
}

/// <summary>Стартовый набор: Место, Шкаф, Коробка, Пакет, Кабель… — повторно добавляет только недостающее.</summary>
public static class StarterKit
{
    public static async Task ApplyAsync(ClientSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.CanEdit)
        {
            return;
        }

        Contracts.Items.StarterKitResult? result = null;
        if (await session.TryAsync(async () => result = await session.Api.ApplyStarterKitAsync(), "Стартовый набор") && result is not null)
        {
            await session.Catalog.LoadAsync(session.Api);
            session.Dialogs.Notify(
                result.TypesCreated + result.ListsCreated == 0
                    ? "Стартовый набор уже добавлен"
                    : $"Добавлено: {ItemVisuals.Plural(result.TypesCreated, "тип", "типа", "типов")}, {ItemVisuals.Plural(result.ListsCreated, "справочник", "справочника", "справочников")}",
                NotifyKind.Success);
        }
    }
}

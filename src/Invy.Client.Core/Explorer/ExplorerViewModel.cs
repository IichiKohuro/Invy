using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Invy.Client.Core.Api;
using Invy.Client.Core.Catalog;
using Invy.Client.Core.Editor;
using Invy.Client.Core.Photos;
using Invy.Client.Core.Session;
using Invy.Client.Core.Shell;
using Invy.Contracts.Catalog;
using Invy.Contracts.Items;
using Invy.Contracts.Sync;

namespace Invy.Client.Core.Explorer;

public enum ExplorerMode
{
    /// <summary>Дерево мест: Дом → Чердак → Коробка → Пакет.</summary>
    Places,

    /// <summary>Вещи без места.</summary>
    Unsorted,
}

/// <summary>
/// «Проводник»: дерево мест слева, содержимое текущего места (список или плитки), хлебные крошки,
/// карточка выбранной вещи. Добавление, правка, удаление, копия, перемещение (меню, вырезать/вставить,
/// перетаскивание). Изменения с других устройств приходят через <see cref="ClientSession.Changed"/>.
/// </summary>
public sealed partial class ExplorerViewModel : PageViewModel, IDisposable
{
    private static readonly string[] SortKeys = ["Name", "Updated", "Number", "Type"];

    private readonly ClientSession _session;
    private readonly ExplorerMode _mode;
    private ItemDetailsDto? _current;
    private Guid? _targetId;
    private List<Guid> _cut = [];
    private string? _cutWhat;
    private int _listVersion;
    private int _detailsVersion;
    private bool _syncingTree;
    private bool _loaded;

    public ExplorerViewModel(ClientSession session, ExplorerMode mode = ExplorerMode.Places)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _mode = mode;
        Breadcrumbs.Add(new Breadcrumb(null, Title));
        session.Changed += OnChanged;
        session.Catalog.Changed += OnCatalogChanged;
    }

    public override string Title => _mode == ExplorerMode.Places ? "Места" : "Неразобранное";

    public ExplorerMode Mode => _mode;

    public bool ShowTree => _mode == ExplorerMode.Places;

    public bool CanEdit => _session.CanEdit;

    public ObservableCollection<PlaceNodeViewModel> Tree { get; } = [];

    public ObservableCollection<Breadcrumb> Breadcrumbs { get; } = [];

    public ObservableCollection<ItemRowViewModel> Items { get; } = [];

    /// <summary>Выделенные строки (несколько — Ctrl/Shift); задаёт View.</summary>
    public IReadOnlyList<ItemRowViewModel> Selection
    {
        get;
        set
        {
            field = value;
            NotifySelectionCommands();
        }
    } = [];

    public IReadOnlyList<string> SortOptions { get; } = ["По названию", "По дате изменения", "По номеру", "По типу"];

    [ObservableProperty]
    public partial int SortIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAtRoot), nameof(AddText))]
    [NotifyCanExecuteChangedFor(nameof(UpCommand), nameof(EditCommand))]
    public partial Guid? CurrentId { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    public partial ItemRowViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial PlaceNodeViewModel? SelectedNode { get; set; }

    /// <summary>Карточка выбранной вещи, а если ничего не выбрано — текущего места.</summary>
    [ObservableProperty]
    public partial ItemCardViewModel? Details { get; private set; }

    [ObservableProperty]
    public partial bool IsTiles { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial int Total { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCut))]
    [NotifyCanExecuteChangedFor(nameof(PasteCommand))]
    public partial string? CutText { get; private set; }

    public bool IsAtRoot => CurrentId is null;

    public bool IsEmpty => !IsLoading && Items.Count == 0;

    public bool HasCut => CutText is not null;

    public string AddText => IsAtRoot && _mode == ExplorerMode.Places ? "Добавить место" : "Добавить";

    /// <summary>Типов ещё нет — предложить стартовый набор (Место, Коробка, Кабель…).</summary>
    public bool NeedsStarterKit => CanEdit && !_session.Catalog.ActiveTypes.Any();

    public string EmptyTitle => (IsAtRoot, _mode) switch
    {
        (true, ExplorerMode.Places) => "Пока нет ни одного места",
        (true, ExplorerMode.Unsorted) => "Всё разобрано",
        _ => "Здесь пусто",
    };

    public string EmptyText => (IsAtRoot, _mode) switch
    {
        (true, ExplorerMode.Places) => "Добавьте первое место — например, «Чердак» или «Сарай», а в него — коробки и вещи.",
        (true, ExplorerMode.Unsorted) => "Здесь появятся вещи, у которых не указано место.",
        _ => "Добавьте вещь или перетащите её сюда.",
    };

    /// <summary>Путь текущего места: «Дом › Чердак».</summary>
    public string CurrentPathText => string.Join(" › ", Breadcrumbs.Skip(1).Select(b => b.Name));

    public override async Task ActivateAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (ShowTree)
        {
            await LoadTreeAsync();
        }

        await NavigateAsync(null);
    }

    /// <summary>Открыть место или контейнер (<c>null</c> — корень раздела).</summary>
    public async Task NavigateAsync(Guid? id)
    {
        _targetId = id;
        var version = ++_listVersion;
        IsLoading = true;
        try
        {
            var current = id is { } currentId ? await _session.Api.GetItemAsync(currentId) : null;
            if (current is { IsDeleted: true })
            {
                _session.Dialogs.Notify($"«{current.Name}» в корзине", NotifyKind.Warning);
                await NavigateAsync(current.Path.Count > 0 ? current.Path[^1].Id : null);
                return;
            }

            var page = await LoadPageAsync(id);
            if (version != _listVersion)
            {
                return;   // пока грузили, пользователь ушёл в другое место
            }

            _current = current;
            CurrentId = id;
            SetBreadcrumbs(current);
            SetItems(page, keepSelection: false);
            Details = current is null ? null : Card(current);
            if (ShowTree)
            {
                await RevealInTreeAsync(current);
            }
        }
        catch (ApiException ex) when (ex.Status == HttpStatusCode.NotFound && id is not null)
        {
            _session.Dialogs.Notify("Место не найдено — возможно, его удалили на другом устройстве", NotifyKind.Warning);
            await NavigateAsync(null);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            _session.Dialogs.Notify(ex is ApiException api ? api.UserMessage : "Нет связи с сервером Invy", NotifyKind.Error);
        }
        finally
        {
            if (version == _listVersion)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>Перечитать текущее место, сохранив выделение.</summary>
    public async Task ReloadAsync()
    {
        if (_targetId != CurrentId)
        {
            await NavigateAsync(_targetId);   // переход ещё идёт — не отменять его старым местом
            return;
        }

        var version = ++_listVersion;
        try
        {
            var current = CurrentId is { } id ? await _session.Api.GetItemAsync(id) : null;
            if (current is { IsDeleted: true })
            {
                await NavigateAsync(current.Path.Count > 0 ? current.Path[^1].Id : null);
                return;
            }

            var page = await LoadPageAsync(CurrentId);
            if (version != _listVersion)
            {
                return;
            }

            _current = current;
            SetBreadcrumbs(current);
            SetItems(page, keepSelection: true);
            if (SelectedItem is null)
            {
                Details = current is null ? null : Card(current);
            }
            else
            {
                await LoadDetailsAsync(SelectedItem.Id);
            }
        }
        catch (ApiException ex) when (ex.Status == HttpStatusCode.NotFound && CurrentId is not null)
        {
            await NavigateAsync(null);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            _session.Dialogs.Notify(ex is ApiException api ? api.UserMessage : "Нет связи с сервером Invy", NotifyKind.Error);
        }
    }

    /// <summary>Переместить вещи (перетаскивание, вставка). Возвращает true, если получилось.</summary>
    /// <param name="ids">Что переместить.</param>
    /// <param name="targetId">Куда; <c>null</c> — без места.</param>
    /// <param name="targetName">Название места назначения для уведомления.</param>
    /// <param name="what">Что перемещается — для уведомления («Коробка», «3 вещи»).</param>
    public async Task<bool> MoveAsync(IReadOnlyList<Guid> ids, Guid? targetId, string? targetName = null, string? what = null)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0 || (targetId is { } t && ids.Contains(t)))
        {
            return false;
        }

        var moved = await _session.TryAsync(() => _session.Api.MoveItemsAsync(ids, targetId), "Не удалось переместить");
        if (moved)
        {
            what ??= ids.Count == 1 && Items.FirstOrDefault(i => i.Id == ids[0]) is { } row ? $"«{row.Name}»" : ItemVisuals.Things(ids.Count);
            _session.Dialogs.Notify($"{what} → {targetName ?? (targetId is null ? "без места" : "новое место")}", NotifyKind.Success);
            await ReloadAsync();
        }

        return moved;
    }

    /// <summary>Добавить фото к вещи (перетаскивание файлов, вставка из буфера).</summary>
    public async Task<bool> AddPhotosAsync(Guid itemId, IReadOnlyList<PickedFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (!CanEdit || files.Count == 0)
        {
            return false;
        }

        var strip = new PhotoStripViewModel(_session, itemId);
        if (!await strip.AddFilesAsync(files))
        {
            return false;
        }

        var name = Items.FirstOrDefault(i => i.Id == itemId)?.Name ?? (itemId == CurrentId ? Breadcrumbs[^1].Name : null);
        _session.Dialogs.Notify(
            $"{ItemVisuals.Plural(strip.Photos.Count, "фото добавлено", "фото добавлены", "фото добавлены")}{(name is null ? string.Empty : $" — «{name}»")}",
            NotifyKind.Success);
        await ReloadAsync();
        return true;
    }

    /// <summary>Куда добавлять фото из буфера: выбранная вещь или текущее место.</summary>
    public Guid? PhotoTargetId => SelectedItem?.Id ?? CurrentId;

    public void Dispose()
    {
        _session.Changed -= OnChanged;
        _session.Catalog.Changed -= OnCatalogChanged;
    }

    [RelayCommand]
    private Task ApplyStarterKitAsync() => StarterKit.ApplyAsync(_session);

    private void OnCatalogChanged() => OnPropertyChanged(nameof(NeedsStarterKit));

    [RelayCommand]
    private async Task OpenAsync(ItemRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (row.IsContainer)
        {
            await NavigateAsync(row.Id);
        }
        else if (CanEdit)
        {
            await EditAsync(row);
        }
    }

    [RelayCommand]
    private Task GoToAsync(Breadcrumb? crumb) => NavigateAsync(crumb?.Id);

    private bool CanGoUp() => CurrentId is not null;

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private Task UpAsync() => NavigateAsync(_current is { Path.Count: > 0 } current ? current.Path[^1].Id : null);

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (ShowTree)
        {
            await LoadTreeAsync();
        }

        await ReloadAsync();
    }

    [RelayCommand]
    private Task AddAsync() => AddOfRoleAsync(null);

    /// <summary>Ctrl+Shift+N: новый контейнер здесь (коробка, пакет…); в корне мест — место.</summary>
    [RelayCommand]
    private Task AddContainerAsync() => AddOfRoleAsync(IsAtRoot && _mode == ExplorerMode.Places ? TypeRole.Place : TypeRole.Container);

    [RelayCommand]
    private void ShowList() => IsTiles = false;

    [RelayCommand]
    private void ShowTiles() => IsTiles = true;

    private async Task AddOfRoleAsync(TypeRole? role)
    {
        if (!CanEdit)
        {
            return;
        }

        var location = CurrentId is { } id ? new MoveTarget(id, CurrentPathText) : null;
        var editor = ItemEditorViewModel.ForNew(_session, location, PreferredTypeId(role));
        if (await _session.Dialogs.EditItemAsync(editor) is { } saved)
        {
            await ReloadAsync();
            SelectedItem = Items.FirstOrDefault(i => i.Id == saved.Id) ?? SelectedItem;
        }
    }

    private bool CanEditItem(ItemRowViewModel? row) => CanEdit && ((row ?? SelectedItem) is not null || CurrentId is not null);

    [RelayCommand(CanExecute = nameof(CanEditItem))]
    private async Task EditAsync(ItemRowViewModel? row)
    {
        var id = row?.Id ?? SelectedItem?.Id ?? CurrentId;
        if (!CanEdit || id is null)
        {
            return;
        }

        ItemDetailsDto? item = null;
        if (!await _session.TryAsync(async () => item = await _session.Api.GetItemAsync(id.Value)) || item is null)
        {
            return;
        }

        if (await _session.Dialogs.EditItemAsync(ItemEditorViewModel.ForExisting(_session, item)) is not null)
        {
            await ReloadAsync();
        }
    }

    private bool CanActOn(ItemRowViewModel? row) => CanEdit && (row is not null || SelectedItem is not null || Selection.Count > 0);

    [RelayCommand(CanExecute = nameof(CanActOn))]
    private async Task DeleteAsync(ItemRowViewModel? row)
    {
        var targets = Targets(row);
        if (!CanEdit || targets.Count == 0)
        {
            return;
        }

        var mode = (DeleteContentsMode?)null;
        var withContents = targets.Where(t => t.Item.ChildCount > 0).ToList();
        if (targets.Count == 1 && withContents.Count == 1)
        {
            var choice = await _session.Dialogs.AskAsync(
                $"Удалить «{targets[0].Name}»?",
                $"Внутри {ItemVisuals.Things(targets[0].Item.ChildCount)}. Удалённое попадёт в корзину — его можно восстановить.",
                "Удалить вместе с содержимым",
                "Переложить содержимое выше");
            if (choice == DialogChoice.Cancel)
            {
                return;
            }

            mode = choice == DialogChoice.Primary ? DeleteContentsMode.Delete : DeleteContentsMode.MoveUp;
        }
        else
        {
            var title = targets.Count == 1 ? $"Удалить «{targets[0].Name}»?" : $"Удалить {ItemVisuals.Things(targets.Count)}?";
            var message = withContents.Count > 0
                ? "Вместе с содержимым. Удалённое попадёт в корзину — его можно восстановить."
                : "Удалённое попадёт в корзину — его можно восстановить.";
            if (await _session.Dialogs.AskAsync(title, message, "Удалить") != DialogChoice.Primary)
            {
                return;
            }

            mode = withContents.Count > 0 ? DeleteContentsMode.Delete : null;
        }

        var deleted = 0;
        foreach (var target in targets)
        {
            if (await _session.TryAsync(() => _session.Api.DeleteItemAsync(target.Id, target.Item.ChildCount > 0 ? mode : null), $"«{target.Name}»"))
            {
                deleted++;
            }
        }

        if (deleted > 0)
        {
            _session.Dialogs.Notify(targets.Count == 1 ? $"«{targets[0].Name}» — в корзине" : $"В корзину: {ItemVisuals.Things(deleted)}", NotifyKind.Success);
            SelectedItem = null;
            await ReloadAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanActOn))]
    private async Task DuplicateAsync(ItemRowViewModel? row)
    {
        if (!CanEdit || (row ?? SelectedItem) is not { } source)
        {
            return;
        }

        ItemDetailsDto? copy = null;
        if (await _session.TryAsync(async () => copy = await _session.Api.DuplicateItemAsync(source.Id), "Не удалось создать копию") && copy is not null)
        {
            _session.Dialogs.Notify($"Создана копия «{copy.Name}»", NotifyKind.Success);
            await ReloadAsync();
            SelectedItem = Items.FirstOrDefault(i => i.Id == copy.Id) ?? SelectedItem;
        }
    }

    [RelayCommand(CanExecute = nameof(CanActOn))]
    private async Task MoveToAsync(ItemRowViewModel? row)
    {
        var targets = Targets(row);
        if (!CanEdit || targets.Count == 0)
        {
            return;
        }

        var title = targets.Count == 1 ? $"Переместить «{targets[0].Name}» в…" : $"Переместить {ItemVisuals.Things(targets.Count)} в…";
        if (await _session.Dialogs.PickLocationAsync(title, _session) is { } target)
        {
            await MoveAsync(targets.Select(t => t.Id).ToList(), target.Id, target.Name, Describe(targets));
        }
    }

    [RelayCommand(CanExecute = nameof(CanActOn))]
    private void Cut(ItemRowViewModel? row)
    {
        var targets = Targets(row);
        if (!CanEdit || targets.Count == 0)
        {
            return;
        }

        _cut = targets.Select(t => t.Id).ToList();
        foreach (var item in Items)
        {
            item.IsCut = _cut.Contains(item.Id);
        }

        _cutWhat = Describe(targets);
        CutText = $"Вырезано: {_cutWhat}";
    }

    private bool CanPaste() => HasCut;

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private async Task PasteAsync()
    {
        var ids = _cut;
        if (await MoveAsync(ids, CurrentId, CurrentId is null ? Title : Breadcrumbs[^1].Name, _cutWhat))
        {
            ClearCut();
        }
    }

    [RelayCommand]
    private void ClearCut()
    {
        _cut = [];
        _cutWhat = null;
        CutText = null;
        foreach (var item in Items)
        {
            item.IsCut = false;
        }
    }

    partial void OnSelectedItemChanged(ItemRowViewModel? value)
    {
        NotifySelectionCommands();
        if (value is not null)
        {
            _ = LoadDetailsAsync(value.Id);
        }
        else
        {
            _detailsVersion++;
            Details = _current is null ? null : Card(_current);
        }
    }

    partial void OnSelectedNodeChanged(PlaceNodeViewModel? value)
    {
        if (!_syncingTree && value is { IsPlaceholder: false } node && node.Id != CurrentId)
        {
            _ = NavigateAsync(node.Id);
        }
    }

    partial void OnSortIndexChanged(int value) => _ = ReloadAsync();

    private Task<PagedResult<ItemSummaryDto>> LoadPageAsync(Guid? id)
    {
        var sort = SortKeys[Math.Clamp(SortIndex, 0, SortKeys.Length - 1)];
        var descending = sort == "Updated";
        return id is { } parentId ? _session.Api.GetChildrenAsync(parentId, sort, descending)
            : _mode == ExplorerMode.Places ? _session.Api.GetRootsAsync(sort, descending)
            : _session.Api.GetUnsortedAsync();
    }

    private async Task LoadDetailsAsync(Guid id)
    {
        var version = ++_detailsVersion;
        try
        {
            var item = await _session.Api.GetItemAsync(id);
            if (version == _detailsVersion)
            {
                Details = Card(item);
            }
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            if (version == _detailsVersion)
            {
                Details = null;
            }
        }
    }

    private void SetItems(PagedResult<ItemSummaryDto> page, bool keepSelection)
    {
        var selectedId = keepSelection ? SelectedItem?.Id : null;
        Items.Clear();
        foreach (var item in page.Items)
        {
            Items.Add(new ItemRowViewModel(item) { IsCut = _cut.Contains(item.Id) });
        }

        Total = page.Total;
        _ = LoadThumbnailsAsync(Items.Where(i => i.CoverMediaId is not null).ToList());
        var selected = selectedId is { } id ? Items.FirstOrDefault(i => i.Id == id) : null;
        if (!ReferenceEquals(selected, SelectedItem))
        {
            _detailsVersion++;
            SelectedItem = selected;
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyText));
    }

    private ItemCardViewModel Card(ItemDetailsDto item)
    {
        var card = new ItemCardViewModel(item, _session);
        card.Photos.Changed += () => _ = ReloadAsync();
        return card;
    }

    private async Task LoadThumbnailsAsync(List<ItemRowViewModel> rows)
    {
        foreach (var row in rows)
        {
            if (row.CoverMediaId is { } mediaId)
            {
                row.Thumbnail = await _session.Photos.GetAsync(mediaId, Api.PhotoSize.Thumb);
            }
        }
    }

    private void SetBreadcrumbs(ItemDetailsDto? current)
    {
        Breadcrumbs.Clear();
        Breadcrumbs.Add(new Breadcrumb(null, Title));
        if (current is not null)
        {
            foreach (var segment in current.Path)
            {
                Breadcrumbs.Add(new Breadcrumb(segment.Id, segment.Name));
            }

            Breadcrumbs.Add(new Breadcrumb(current.Id, current.Name));
        }

        OnPropertyChanged(nameof(CurrentPathText));
    }

    private async Task LoadTreeAsync() =>
        await _session.TryAsync(async () => PlaceNodeViewModel.Merge(Tree, await _session.Api.GetTreeAsync(null), null, LoadTreeChildrenAsync));

    private Task<IReadOnlyList<TreeNodeDto>> LoadTreeChildrenAsync(Guid? parentId) => _session.Api.GetTreeAsync(parentId);

    /// <summary>Раскрыть в дереве путь к текущему месту и выделить его.</summary>
    private async Task RevealInTreeAsync(ItemDetailsDto? current)
    {
        _syncingTree = true;
        try
        {
            if (current is null)
            {
                SelectedNode = null;
                return;
            }

            var level = Tree;
            PlaceNodeViewModel? node = null;
            foreach (var id in current.Path.Select(p => p.Id).Append(current.Id))
            {
                node = level.FirstOrDefault(n => n.Id == id);
                if (node is null)
                {
                    break;
                }

                if (id != current.Id)
                {
                    await node.EnsureLoadedAsync();
                    node.IsExpanded = true;
                    level = node.Children;
                }
            }

            SelectedNode = node;
        }
        finally
        {
            _syncingTree = false;
        }
    }

    private void NotifySelectionCommands()
    {
        DeleteCommand.NotifyCanExecuteChanged();
        DuplicateCommand.NotifyCanExecuteChanged();
        MoveToCommand.NotifyCanExecuteChanged();
        CutCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
    }

    private static string Describe(List<ItemRowViewModel> rows) => rows.Count == 1 ? $"«{rows[0].Name}»" : ItemVisuals.Things(rows.Count);

    private List<ItemRowViewModel> Targets(ItemRowViewModel? row)
    {
        if (row is not null && !(Selection.Count > 1 && Selection.Contains(row)))
        {
            return [row];
        }

        if (Selection.Count > 0)
        {
            return Selection.ToList();
        }

        return SelectedItem is { } selected ? [selected] : [];
    }

    /// <summary>Тип по умолчанию для «Добавить»: недавний из подходящих по месту (в корне мест — место).</summary>
    private Guid? PreferredTypeId(TypeRole? only = null)
    {
        var recent = _session.Settings.Current.RecentTypeIds.ToList();
        var parentRole = _current?.Role;
        return _session.Catalog.ActiveTypes
            .Where(t => only is null || t.Role == only)
            .Where(t => parentRole is { } role ? ItemVisuals.CanContain(role, t.Role)
                : _mode == ExplorerMode.Places ? t.Role == TypeRole.Place : t.Role != TypeRole.Place)
            .OrderBy(t => recent.IndexOf(t.Id) is var i and >= 0 ? i : int.MaxValue)
            .ThenBy(t => RolePreference(parentRole, t.Role))
            .ThenBy(t => t.SortOrder)
            .FirstOrDefault()?.Id;
    }

    /// <summary>Без истории: в контейнер обычно кладут вещи, в место — контейнеры, в корень мест — места.</summary>
    private static int RolePreference(TypeRole? parent, TypeRole child) => (parent, child) switch
    {
        (TypeRole.Container, TypeRole.Item) => 0,
        (TypeRole.Place, TypeRole.Container) => 0,
        (TypeRole.Place, TypeRole.Item) => 1,
        _ => 2,
    };

    private async void OnChanged(ChangeBatch batch)
    {
        try
        {
            await HandleChangesAsync(batch);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            // следующее изменение или «Обновить» приведут вид в порядок
        }
    }

    private async Task HandleChangesAsync(ChangeBatch batch)
    {
        if (!_loaded)
        {
            return;
        }

        var items = batch.Changes.Where(c => c.EntityType == ChangeEntityType.Item).ToList();
        var catalogChanged = batch.Changes.Any(c => c.EntityType is ChangeEntityType.ItemType or ChangeEntityType.Tag);
        if (items.Count == 0 && !catalogChanged)
        {
            return;
        }

        if (ShowTree && items.Count > 0)
        {
            await RefreshTreeAsync(items);
        }

        var visible = Items.Select(i => i.Id).ToHashSet();
        var here = new[] { CurrentId, _targetId };
        var affectsList = catalogChanged || items.Any(c =>
            here.Contains(c.ParentId) || (c.OldParentId is not null && here.Contains(c.OldParentId)) || here.Contains(c.EntityId) || visible.Contains(c.EntityId)
            || (_current?.Path.Any(p => p.Id == c.EntityId) ?? false));
        if (affectsList)
        {
            await ReloadAsync();
        }
        else if (SelectedItem is { } selected && items.Any(c => c.EntityId == selected.Id))
        {
            await LoadDetailsAsync(selected.Id);
        }
    }

    /// <summary>Перечитать ветки дерева, где что-то добавилось, переименовалось, переехало или удалилось.</summary>
    private async Task RefreshTreeAsync(IReadOnlyList<ChangeDto> changes)
    {
        var nodes = Tree.SelectMany(n => n.SelfAndDescendants()).ToDictionary(n => n.Id);
        var parents = new HashSet<Guid?>();
        foreach (var change in changes)
        {
            parents.Add(change.ParentId);
            if (change.Action == ChangeAction.Moved)
            {
                parents.Add(change.OldParentId);
            }

            if (nodes.TryGetValue(change.EntityId, out var node))
            {
                parents.Add(node.Parent?.Id);
            }
        }

        foreach (var parentId in parents)
        {
            if (parentId is null)
            {
                await LoadTreeAsync();
            }
            else if (nodes.TryGetValue(parentId.Value, out var parent))
            {
                if (parent.IsLoaded)
                {
                    await parent.ReloadAsync();
                }
                else if (parent.Parent is null)
                {
                    await LoadTreeAsync();   // у узла могли появиться дети — обновить стрелку
                }
                else if (parent.Parent.IsLoaded)
                {
                    await parent.Parent.ReloadAsync();
                }
            }
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Invy.Client.Core.Explorer;
using Invy.Client.UI.Services;

namespace Invy.Client.UI.Views.Explorer;

/// <summary>
/// «Проводник». Перетаскивание: вещи из списка — на контейнер в списке или на узел дерева;
/// файлы-картинки — на вещь, карточку или пустое место списка (фото текущего места).
/// Ctrl+V: вставить вырезанное, а если ничего не вырезано — картинки из буфера как фото; Ctrl+Shift+V — всегда фото.
/// Двойной щелчок или Enter — открыть контейнер или изменить вещь.
/// </summary>
public partial class ExplorerPage : UserControl
{
    /// <summary>Перетаскиваемые вещи: id через запятую (только внутри приложения).</summary>
    private static readonly DataFormat<string> ItemsFormat = DataFormat.CreateStringApplicationFormat("invy-items");

    private const double DragThreshold = 6;

    private PointerPressedEventArgs? _pressed;
    private Point _pressedAt;
    private Control? _dropHighlight;

    public ExplorerPage()
    {
        InitializeComponent();
        Breadcrumbs.ItemClicked += OnBreadcrumbClicked;
        foreach (var list in new[] { ItemsList, TilesList })
        {
            list.DoubleTapped += (_, _) => Open(list);
            list.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Open(list);
                    e.Handled = true;
                }
            };
            list.SelectionChanged += (_, _) =>
            {
                if (ViewModel is { } vm && list.IsVisible)
                {
                    vm.Selection = list.SelectedItems?.OfType<ItemRowViewModel>().ToList() ?? [];
                }
            };
            list.AddHandler(PointerPressedEvent, OnItemPointerPressed, RoutingStrategies.Tunnel);
            list.AddHandler(PointerMovedEvent, OnItemPointerMoved, RoutingStrategies.Tunnel);
            list.AddHandler(PointerReleasedEvent, (_, _) => _pressed = null, RoutingStrategies.Tunnel);
            AddDropHandlers(list);
        }

        AddDropHandlers(PlacesTree);
        AddDropHandlers(CardPane);
        KeyDown += OnKeyDown;

        // Горячие клавиши страницы (Ctrl+N, Delete, F2…) работают, только пока фокус внутри неё:
        // щелчок по пустому месту не должен уводить фокус из страницы.
        Focusable = true;
        AddHandler(PointerPressedEvent, (_, _) => KeepFocus(), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        KeepFocus();
    }

    private void KeepFocus()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Visual focused || !this.IsVisualAncestorOf(focused))
        {
            Focus();
        }
    }

    private ExplorerViewModel? ViewModel => DataContext as ExplorerViewModel;

    private void OnBreadcrumbClicked(FABreadcrumbBar sender, FABreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is Breadcrumb crumb)
        {
            ViewModel?.GoToCommand.Execute(crumb);
        }
    }

    private void Open(ListBox list)
    {
        if (list.SelectedItem is ItemRowViewModel row)
        {
            ViewModel?.OpenCommand.Execute(row);
        }
    }

    // ---------- Перетаскивание: источник ----------
    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressed = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && ViewModel is { CanEdit: true } ? e : null;
        _pressedAt = e.GetPosition(this);
    }

    private async void OnItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is not { } pressed || sender is not ListBox list)
        {
            return;
        }

        var delta = e.GetPosition(this) - _pressedAt;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
        {
            return;
        }

        _pressed = null;
        var row = (pressed.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as ItemRowViewModel;
        if (row is null)
        {
            return;
        }

        var rows = list.SelectedItems?.OfType<ItemRowViewModel>().ToList() ?? [];
        var ids = (rows.Contains(row) ? rows : [row]).Select(r => r.Id).ToList();
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(ItemsFormat, string.Join(',', ids)));
        await DragDrop.DoDragDropAsync(pressed, data, DragDropEffects.Move);
        ClearDropHighlight();
    }

    // ---------- Перетаскивание: приёмники ----------
    private void AddDropHandlers(Control target)
    {
        target.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        target.AddHandler(DragDrop.DragLeaveEvent, (_, _) => ClearDropHighlight());
        target.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || ViewModel is not { } vm || e.KeyModifiers is not (KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift)))
        {
            return;
        }

        // Ctrl+Shift+V — всегда фото из буфера; Ctrl+V — вырезанные вещи, а если их нет — тоже фото.
        e.Handled = true;
        if (vm.HasCut && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            await vm.PasteCommand.ExecuteAsync(null);
        }
        else if (vm.PhotoTargetId is { } target && await PickedFiles.FromClipboardAsync(TopLevel.GetTopLevel(this)) is { Count: > 0 } files)
        {
            await vm.AddPhotosAsync(target, files);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.Contains(DataFormat.File);
        var (target, container) = DropTarget(e, files, sender);
        e.DragEffects = target is null || ViewModel is not { CanEdit: true } ? DragDropEffects.None
            : files ? DragDropEffects.Copy
            : DraggedIds(e) is { Count: > 0 } ids && !ids.Contains(target.Value.Id) ? DragDropEffects.Move
            : DragDropEffects.None;
        SetDropHighlight(e.DragEffects != DragDropEffects.None ? container : null);
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        ClearDropHighlight();
        var files = e.DataTransfer.Contains(DataFormat.File);
        var (target, _) = DropTarget(e, files, sender);
        if (target is not { } t || ViewModel is not { } vm)
        {
            return;
        }

        e.Handled = true;
        if (files)
        {
            await vm.AddPhotosAsync(t.Id, PickedFiles.From(e.DataTransfer.TryGetFiles()));
        }
        else if (DraggedIds(e) is { Count: > 0 } ids)
        {
            await vm.MoveAsync(ids, t.Id, t.Name);
        }
    }

    /// <summary>
    /// Куда бросают: вещи — в контейнер списка или узел дерева; файлы — на любую вещь, в карточку
    /// или на пустое место списка (текущее место).
    /// </summary>
    private ((Guid Id, string Name)? Target, Control? Container) DropTarget(DragEventArgs e, bool files, object? sender)
    {
        var visual = e.Source as Visual;
        if (visual?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: ItemRowViewModel row } item && (files || row.IsContainer))
        {
            return ((row.Id, row.Name), item);
        }

        if (files && ReferenceEquals(sender, CardPane) && ViewModel?.Details is { } card)
        {
            return ((card.Id, card.Name), CardPane);
        }

        if (files && sender is ListBox list && ViewModel is { CurrentId: { } currentId } vm)
        {
            return ((currentId, vm.Breadcrumbs[^1].Name), list);
        }

        if (visual?.FindAncestorOfType<TreeViewItem>(includeSelf: true) is { DataContext: PlaceNodeViewModel { IsPlaceholder: false } node } treeItem)
        {
            return ((node.Id, node.Name), treeItem);
        }

        return (null, null);
    }

    private static List<Guid>? DraggedIds(DragEventArgs e) =>
        e.DataTransfer.TryGetValue(ItemsFormat) is { Length: > 0 } text
            ? text.Split(',').Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList()
            : null;

    private void SetDropHighlight(Control? control)
    {
        if (ReferenceEquals(control, _dropHighlight))
        {
            return;
        }

        ClearDropHighlight();
        _dropHighlight = control;
        _dropHighlight?.Classes.Add("drop-target");
    }

    private void ClearDropHighlight()
    {
        _dropHighlight?.Classes.Remove("drop-target");
        _dropHighlight = null;
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Invy.Client.Core.Session;

namespace Invy.Client.Core.Explorer;

/// <summary>Выбор места или контейнера: «Где лежит?», «Переместить в…».</summary>
public sealed partial class LocationPickerViewModel : ObservableObject
{
    private readonly ClientSession _session;

    public LocationPickerViewModel(ClientSession session, string title)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        Title = title;
    }

    public string Title { get; }

    public ObservableCollection<PlaceNodeViewModel> Tree { get; } = [];

    [ObservableProperty]
    public partial PlaceNodeViewModel? SelectedNode { get; set; }

    /// <summary>Выбран вариант «Без места»: верхний уровень для мест, «Неразобранное» для вещей.</summary>
    [ObservableProperty]
    public partial bool IsNoPlaceSelected { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    public bool HasSelection => SelectedNode is not null || IsNoPlaceSelected;

    public MoveTarget? Result =>
        IsNoPlaceSelected ? new MoveTarget(null, "Без места")
        : SelectedNode is { IsPlaceholder: false } node ? new MoveTarget(node.Id, node.PathText)
        : null;

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            await _session.TryAsync(async () =>
                PlaceNodeViewModel.Merge(Tree, await _session.Api.GetTreeAsync(null), null, id => _session.Api.GetTreeAsync(id)));
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedNodeChanged(PlaceNodeViewModel? value)
    {
        if (value is not null)
        {
            IsNoPlaceSelected = false;
        }

        OnPropertyChanged(nameof(HasSelection));
    }

    partial void OnIsNoPlaceSelectedChanged(bool value)
    {
        if (value)
        {
            SelectedNode = null;
        }

        OnPropertyChanged(nameof(HasSelection));
    }
}

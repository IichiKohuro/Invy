using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Invy.Contracts.Catalog;
using Invy.Contracts.Items;

namespace Invy.Client.Core.Explorer;

/// <summary>
/// Узел дерева мест и контейнеров. Дети загружаются при первом раскрытии; до этого — заглушка,
/// чтобы у узла была стрелка. При обновлении дети сливаются по id: раскрытые ветки и выбор сохраняются.
/// </summary>
public sealed partial class PlaceNodeViewModel : ObservableObject
{
    private readonly Func<Guid?, Task<IReadOnlyList<TreeNodeDto>>>? _load;
    private Task? _loading;

    internal PlaceNodeViewModel(TreeNodeDto node, PlaceNodeViewModel? parent, Func<Guid?, Task<IReadOnlyList<TreeNodeDto>>> load)
    {
        Id = node.Id;
        Parent = parent;
        _load = load;
        Update(node);
    }

    private PlaceNodeViewModel(PlaceNodeViewModel parent)
    {
        Parent = parent;
        IsPlaceholder = true;
        Name = "Загрузка…";
    }

    public Guid Id { get; }

    public PlaceNodeViewModel? Parent { get; }

    public bool IsPlaceholder { get; }

    public bool IsLoaded { get; private set; }

    public ObservableCollection<PlaceNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph), nameof(Symbol))]
    public partial IconDto? Icon { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Symbol))]
    public partial TypeRole Role { get; private set; }

    [ObservableProperty]
    public partial string? Color { get; private set; }

    [ObservableProperty]
    public partial bool HasChildren { get; private set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string? Glyph => ItemVisuals.Glyph(Icon);

    public string Symbol => ItemVisuals.Symbol(Icon, Role);

    /// <summary>«Дом › Чердак › Серая коробка».</summary>
    public string PathText => Parent is null ? Name : $"{Parent.PathText} › {Name}";

    public IEnumerable<PlaceNodeViewModel> SelfAndDescendants() =>
        new[] { this }.Concat(Children.Where(c => !c.IsPlaceholder).SelectMany(c => c.SelfAndDescendants()));

    /// <summary>Загрузить детей, если ещё не загружены (повторные вызовы ждут ту же загрузку).</summary>
    public Task EnsureLoadedAsync() => IsLoaded || IsPlaceholder ? Task.CompletedTask : _loading ??= ReloadAsync();

    public async Task ReloadAsync()
    {
        if (_load is null)
        {
            return;
        }

        try
        {
            var nodes = await _load(Id);
            Merge(Children, nodes, this, _load);
            IsLoaded = true;
            HasChildren = Children.Count > 0;
        }
        finally
        {
            _loading = null;
        }
    }

    internal void Update(TreeNodeDto node)
    {
        Name = node.Name;
        Icon = node.Icon;
        Role = node.Role;
        Color = node.Color;
        HasChildren = node.HasChildren;
        if (!IsLoaded && node.HasChildren && Children.Count == 0)
        {
            Children.Add(new PlaceNodeViewModel(this));
        }
        else if (!node.HasChildren && !IsLoaded)
        {
            Children.Clear();
        }
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            _ = EnsureLoadedAsync();
        }
    }

    /// <summary>Привести <paramref name="target"/> к <paramref name="nodes"/>, переиспользуя узлы с теми же id.</summary>
    internal static void Merge(ObservableCollection<PlaceNodeViewModel> target, IReadOnlyList<TreeNodeDto> nodes, PlaceNodeViewModel? parent, Func<Guid?, Task<IReadOnlyList<TreeNodeDto>>> load)
    {
        var existing = target.Where(n => !n.IsPlaceholder).ToDictionary(n => n.Id);
        for (var i = 0; i < nodes.Count; i++)
        {
            var dto = nodes[i];
            if (existing.Remove(dto.Id, out var node))
            {
                node.Update(dto);
                var index = target.IndexOf(node);
                if (index != i)
                {
                    target.Move(index, i);
                }
            }
            else
            {
                target.Insert(i, new PlaceNodeViewModel(dto, parent, load));
            }
        }

        while (target.Count > nodes.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }
}

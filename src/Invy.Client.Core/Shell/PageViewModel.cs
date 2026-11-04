using CommunityToolkit.Mvvm.ComponentModel;

namespace Invy.Client.Core.Shell;

/// <summary>Базовая модель страницы, которую показывает оболочка.</summary>
public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }
}

/// <summary>Заглушка для разделов, которые появятся в следующих фазах.</summary>
public sealed class PlaceholderPageViewModel(string title, string description) : PageViewModel
{
    public override string Title { get; } = title;

    public string Description { get; } = description;
}

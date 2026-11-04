using CommunityToolkit.Mvvm.ComponentModel;

namespace Invy.Client.Core.Shell;

/// <summary>Базовая модель страницы, которую показывает оболочка.</summary>
public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }

    /// <summary>Страница показана: загрузить данные (первый раз) или обновить.</summary>
    public virtual Task ActivateAsync() => Task.CompletedTask;
}

/// <summary>Заглушка для разделов, которые появятся в следующих фазах.</summary>
public sealed class PlaceholderPageViewModel(string title, string description) : PageViewModel
{
    public override string Title { get; } = title;

    public string Description { get; } = description;
}

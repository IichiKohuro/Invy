using CommunityToolkit.Mvvm.ComponentModel;
using Invy.Client.Core.Shell;

namespace Invy.Client.Core.Pages;

/// <summary>Главная: поиск и быстрые разделы. В фазе 0 — макет без данных.</summary>
public sealed partial class HomePageViewModel : PageViewModel
{
    public override string Title => "Главная";

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    public IReadOnlyList<HomeTile> Tiles { get; } =
    [
        new("Недавние", "Последние изменённые вещи", "Clock"),
        new("Неразобранное", "Вещи без места", "Import"),
        new("Сохранённые поиски", "Например, «Все кабели Type-C»", "Bookmarks"),
    ];
}

public sealed record HomeTile(string Title, string Subtitle, string Icon);

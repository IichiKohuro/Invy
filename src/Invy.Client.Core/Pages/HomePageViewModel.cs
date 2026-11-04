using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Invy.Client.Core.Shell;

namespace Invy.Client.Core.Pages;

/// <summary>Главная: быстрый переход к разделам. Поиск по всем вещам — фаза 3.</summary>
public sealed partial class HomePageViewModel(Action<string>? navigate = null) : PageViewModel
{
    public override string Title => "Главная";

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    public IReadOnlyList<HomeTile> Tiles { get; } =
    [
        new("Места", "Дом → чердак → коробка → пакет: где что лежит", "MapPin", "places"),
        new("Неразобранное", "Вещи, у которых ещё нет места", "Import", "unsorted"),
        new("Теги", "«зарядка», «в ремонт», «отдать» — вещи из разных мест", "Tag", "tags"),
        new("Типы и характеристики", "Кабель: длина и разъёмы; коробка: материал и цвет", "Library", "types"),
    ];

    [RelayCommand]
    private void Open(HomeTile? tile)
    {
        if (tile is not null)
        {
            navigate?.Invoke(tile.Key);
        }
    }
}

/// <param name="Key">Раздел навигации, который открывает плитка.</param>
public sealed record HomeTile(string Title, string Subtitle, string Icon, string Key);

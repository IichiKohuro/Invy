using Invy.Client.Core.Shell;

namespace Invy.Client.Core.Pages;

/// <summary>«Проводник» по местам. В фазе 0 — демонстрационное дерево из README.</summary>
public sealed class PlacesPageViewModel : PageViewModel
{
    public override string Title => "Места";

    public IReadOnlyList<PlaceNode> Roots { get; } =
    [
        new("Дом", "Место",
        [
            new("Чердак", "Место",
            [
                new("Серая коробка", "Коробка",
                [
                    new("Прозрачный пакет", "Пакет",
                    [
                        new("Type-A → Type-C, 100 см", "Кабель", []),
                        new("HDMI → HDMI, 200 см", "Кабель", []),
                    ]),
                ]),
            ]),
            new("Сарай", "Место",
            [
                new("Ящик с инструментами", "Контейнер", [new("Отвёртка крестовая PH2", "Инструмент", [])]),
            ]),
        ]),
    ];
}

public sealed record PlaceNode(string Name, string TypeName, IReadOnlyList<PlaceNode> Children)
{
    public bool IsContainer => Children.Count > 0 || TypeName is "Место" or "Коробка" or "Пакет" or "Контейнер";
}

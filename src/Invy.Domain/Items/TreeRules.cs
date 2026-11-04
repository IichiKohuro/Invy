namespace Invy.Domain.Items;

/// <summary>Правила вложенности: что во что можно положить.</summary>
public static class TreeRules
{
    /// <summary>Защитный предел глубины дерева (рекурсивные запросы, отображение пути).</summary>
    public const int MaxDepth = 64;

    /// <summary>
    /// Место содержит что угодно (в т. ч. другие места: Дом → Чердак); контейнер — вещи и контейнеры,
    /// но не места; обычная вещь ничего не содержит.
    /// </summary>
    public static bool CanContain(TypeRole parent, TypeRole child) => parent switch
    {
        TypeRole.Place => true,
        TypeRole.Container => child != TypeRole.Place,
        _ => false,
    };

    /// <summary>Текст ошибки для пользователя или <c>null</c>, если положить можно.</summary>
    public static string? ExplainCannotContain(TypeRole parent, string parentName, TypeRole child, string childName) =>
        CanContain(parent, child)
            ? null
            : parent == TypeRole.Item
                ? $"«{parentName}» — не контейнер: внутрь неё нельзя положить другие вещи"
                : $"Место «{childName}» нельзя положить в контейнер «{parentName}»";
}

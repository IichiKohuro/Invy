namespace Invy.Domain.Items;

/// <summary>Роль типа вещи: определяет, может ли вещь содержать другие вещи.</summary>
public enum TypeRole
{
    /// <summary>Обычная вещь: кабель, отвёртка.</summary>
    Item = 0,

    /// <summary>Контейнер: коробка, пакет, сумка.</summary>
    Container = 1,

    /// <summary>Место: дом, чердак, сарай.</summary>
    Place = 2,
}

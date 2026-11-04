using Invy.Domain.Items;

namespace Invy.Domain.Tests.Items;

public class TreeRulesTests
{
    [Theory]
    [InlineData(TypeRole.Place, TypeRole.Place, true)]          // Дом → Чердак
    [InlineData(TypeRole.Place, TypeRole.Container, true)]      // Чердак → Коробка
    [InlineData(TypeRole.Place, TypeRole.Item, true)]
    [InlineData(TypeRole.Container, TypeRole.Container, true)]  // Коробка → Пакет
    [InlineData(TypeRole.Container, TypeRole.Item, true)]       // Пакет → Кабель
    [InlineData(TypeRole.Container, TypeRole.Place, false)]     // Чердак в коробку — нельзя
    [InlineData(TypeRole.Item, TypeRole.Item, false)]           // в кабель ничего не кладут
    [InlineData(TypeRole.Item, TypeRole.Container, false)]
    public void Defines_what_can_contain_what(TypeRole parent, TypeRole child, bool expected) =>
        TreeRules.CanContain(parent, child).ShouldBe(expected);

    [Fact]
    public void Explains_why_item_cannot_be_put_inside()
    {
        TreeRules.ExplainCannotContain(TypeRole.Item, "Кабель", TypeRole.Item, "Переходник").ShouldBe("«Кабель» — не контейнер: внутрь неё нельзя положить другие вещи");
        TreeRules.ExplainCannotContain(TypeRole.Container, "Коробка", TypeRole.Place, "Чердак").ShouldBe("Место «Чердак» нельзя положить в контейнер «Коробка»");
        TreeRules.ExplainCannotContain(TypeRole.Place, "Сарай", TypeRole.Container, "Ящик").ShouldBeNull();
    }

    [Fact]
    public void Item_name_is_trimmed_and_normalized()
    {
        var item = new Item();

        item.Rename("  Ёлочная гирлянда ");

        item.Name.ShouldBe("Ёлочная гирлянда");
        item.NameNorm.ShouldBe("елочная гирлянда");
        item.IsNameAuto.ShouldBeFalse();
    }

    [Fact]
    public void Deletion_and_restore_track_operation()
    {
        var item = new Item();
        var deletion = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;

        item.MarkDeleted(deletion, at);
        item.IsDeleted.ShouldBeTrue();
        item.DeletionId.ShouldBe(deletion);
        item.DeletedAt.ShouldBe(at);

        item.Restore();
        item.IsDeleted.ShouldBeFalse();
        item.DeletionId.ShouldBeNull();
        item.DeletedAt.ShouldBeNull();
    }
}

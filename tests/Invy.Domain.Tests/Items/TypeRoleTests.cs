using Invy.Domain.Items;

namespace Invy.Domain.Tests.Items;

public class TypeRoleTests
{
    // Значения хранятся в БД как INT — менять их нельзя.
    [Theory]
    [InlineData(TypeRole.Item, 0)]
    [InlineData(TypeRole.Container, 1)]
    [InlineData(TypeRole.Place, 2)]
    public void Persisted_values_are_stable(TypeRole role, int expected) => ((int)role).ShouldBe(expected);
}

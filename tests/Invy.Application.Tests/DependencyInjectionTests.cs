using Microsoft.Extensions.DependencyInjection;

namespace Invy.Application.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddInvyApplication_returns_same_collection_for_chaining()
    {
        var services = new ServiceCollection();

        services.AddInvyApplication().ShouldBeSameAs(services);
    }
}

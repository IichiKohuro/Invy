using Microsoft.Extensions.DependencyInjection;

namespace Invy.Application;

public static class DependencyInjection
{
    /// <summary>Регистрирует сервисы сценариев. Наполняется в фазе 1.</summary>
    public static IServiceCollection AddInvyApplication(this IServiceCollection services) => services;
}

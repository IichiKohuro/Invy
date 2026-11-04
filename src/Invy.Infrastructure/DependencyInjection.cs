using Microsoft.Extensions.DependencyInjection;

namespace Invy.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Регистрирует инфраструктуру (SQLite, медиа, бэкапы). Наполняется в фазе 1.</summary>
    public static IServiceCollection AddInvyInfrastructure(this IServiceCollection services) => services;
}

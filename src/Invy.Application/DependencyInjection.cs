using Invy.Application.Catalog;
using Invy.Application.History;
using Invy.Application.Items;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Invy.Application;

public static class DependencyInjection
{
    /// <summary>Сервисы сценариев: по одному экземпляру на запрос.</summary>
    public static IServiceCollection AddInvyApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ItemTypeService>();
        services.AddScoped<ValueListService>();
        services.AddScoped<TagService>();
        services.AddScoped<ItemService>();
        services.AddScoped<HistoryService>();
        return services;
    }
}

using Invy.Api.Auth;
using Invy.Application.Abstractions;
using Invy.Domain.Devices;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Serilog;
using Serilog.Events;

namespace Invy.Api.Hosting;

internal static class ServiceRegistration
{
    /// <summary>Авторизация: по умолчанию всё требует аутентификации; изменения — роль Editor, администрирование — Admin.</summary>
    public static IServiceCollection AddInvyAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<InvyAuthOptions>(configuration.GetSection(InvyAuthOptions.SectionName));
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentActor, HttpCurrentActor>();
        services.AddAuthentication(InvyAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, InvyAuthenticationHandler>(InvyAuthenticationHandler.SchemeName, null);
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.Viewer, p => p.RequireAuthenticatedUser())
            .AddPolicy(Policies.Editor, p => p.RequireRole(nameof(DeviceRole.Editor), nameof(DeviceRole.Admin)))
            .AddPolicy(Policies.Admin, p => p.RequireRole(nameof(DeviceRole.Admin)));
        return services;
    }

    /// <summary>Логи в файлы <c>{DataPath}/logs/server-YYYYMMDD.log</c> (14 дней) — вдобавок к консоли и журналу событий Windows.</summary>
    public static ILoggingBuilder AddInvyFileLogging(this ILoggingBuilder logging, string dataPath)
    {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .WriteTo.File(
                Path.Combine(dataPath, "logs", "server-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture)
            .CreateLogger();
        return logging.AddSerilog(logger, dispose: true);
    }
}

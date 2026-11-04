using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Invy.Api.Auth;
using Invy.Application.Devices;
using Invy.Contracts;
using Invy.Contracts.Devices;

namespace Invy.Api.Endpoints;

/// <summary>Сопряжение и управление устройствами — <c>/api/v1/pairing/requests</c>, <c>/api/v1/devices</c>.</summary>
internal static class DeviceEndpoints
{
    public const string PairingRateLimit = "pairing";

    /// <summary>Не больше 10 запросов подключения в минуту с одного адреса.</summary>
    public static IServiceCollection AddPairingRateLimit(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PairingRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
        });

    public static IEndpointRouteBuilder MapDevices(this IEndpointRouteBuilder app)
    {
        var pairing = app.MapGroup($"{ApiRoutes.Prefix}/pairing/requests").WithTags("Устройства");
        pairing.MapPost("/", (PairingService s, PairingRequestCreate request, HttpContext http, CancellationToken ct) =>
                s.RequestAsync(request, IsSameComputer(http), ct))
            .AllowAnonymous()
            .RequireRateLimiting(PairingRateLimit);
        pairing.MapGet("/{id:guid}", (PairingService s, Guid id) => s.GetStatus(id)).AllowAnonymous();
        pairing.MapGet("/", (PairingService s) => s.ListPending()).RequireAuthorization(Policies.Admin);
        pairing.MapPost("/{id:guid}/approve", (PairingService s, Guid id, ApprovePairingRequest request, CancellationToken ct) => s.ApproveAsync(id, request, ct))
            .RequireAuthorization(Policies.Admin);
        pairing.MapPost("/{id:guid}/deny", (PairingService s, Guid id) =>
        {
            s.Deny(id);
            return TypedResults.NoContent();
        }).RequireAuthorization(Policies.Admin);

        var devices = app.MapGroup($"{ApiRoutes.Prefix}/devices").WithTags("Устройства");
        devices.MapGet("/", (DeviceService s, CancellationToken ct) => s.ListAsync(ct)).RequireAuthorization(Policies.Admin);
        devices.MapGet("/me", (ClaimsPrincipal user) => new CurrentDeviceDto(
            Guid.TryParse(user.FindFirstValue(InvyClaims.DeviceId), out var id) ? id : null,
            user.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            Enum.TryParse<DeviceRole>(user.FindFirstValue(ClaimTypes.Role), out var role) ? role : DeviceRole.Viewer));
        devices.MapPatch("/{id:guid}", (DeviceService s, Guid id, UpdateDeviceRequest request, CancellationToken ct) => s.UpdateAsync(id, request, ct))
            .RequireAuthorization(Policies.Admin);
        devices.MapDelete("/{id:guid}", async (DeviceService s, Guid id, CancellationToken ct) =>
        {
            await s.RevokeAsync(id, ct);
            return TypedResults.NoContent();
        }).RequireAuthorization(Policies.Admin);

        return app;
    }

    /// <summary>Запрос с этого же компьютера (первое устройство становится администратором без подтверждения).</summary>
    private static bool IsSameComputer(HttpContext http) => http.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);
}

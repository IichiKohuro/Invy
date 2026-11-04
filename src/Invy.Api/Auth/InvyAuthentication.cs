using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Invy.Application.Abstractions;
using Invy.Domain.Devices;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Invy.Api.Auth;

public enum AuthMode
{
    /// <summary>Каждое устройство подключается по запросу, который одобряет администратор.</summary>
    Pairing = 0,

    /// <summary>Без авторизации: все запросы — администратор. Только для доверенной сети и разработки.</summary>
    TrustedLan = 1,
}

/// <summary>Секция <c>Invy:Auth</c>.</summary>
public sealed class InvyAuthOptions
{
    public const string SectionName = "Invy:Auth";

    public AuthMode Mode { get; set; } = AuthMode.Pairing;
}

public static class Policies
{
    public const string Viewer = "viewer";
    public const string Editor = "editor";
    public const string Admin = "admin";
}

internal static class InvyClaims
{
    public const string DeviceId = "invy:device";
}

/// <summary>Токен устройства: 256 бит случайных данных; на сервере хранится только SHA-256.</summary>
internal static class DeviceTokens
{
    public static string Create() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Аутентификация по токену устройства: <c>Authorization: Bearer …</c>, а для SignalR (WebSocket) —
/// параметр <c>access_token</c>. В режиме «Доверенная сеть» любой запрос — администратор без устройства.
/// </summary>
internal sealed class InvyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<InvyAuthOptions> auth)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Invy";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (auth.Value.Mode == AuthMode.TrustedLan)
        {
            return Success(null, "Доверенная сеть", DeviceRole.Admin);
        }

        var token = ReadToken();
        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }

        var hash = DeviceTokens.Hash(token);
        var db = Context.RequestServices.GetRequiredService<IInventoryDbContext>();
        var device = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.TokenHash == hash && d.RevokedAt == null, Context.RequestAborted);
        return device is null
            ? AuthenticateResult.Fail("Устройство не найдено или доступ отозван")
            : Success(device.Id, device.Name, device.Role);
    }

    private string? ReadToken()
    {
        var header = Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return header["Bearer ".Length..].Trim();
        }

        // Браузерные и WebSocket-клиенты SignalR передают токен в строке запроса.
        return Request.Path.StartsWithSegments("/hubs") ? Request.Query["access_token"].ToString() : null;
    }

    private static AuthenticateResult Success(Guid? deviceId, string name, DeviceRole role)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name), new(ClaimTypes.Role, role.ToString()) };
        if (deviceId is { } id)
        {
            claims.Add(new Claim(InvyClaims.DeviceId, id.ToString()));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}

/// <summary>Текущее устройство из запроса — для истории изменений.</summary>
internal sealed class HttpCurrentActor(IHttpContextAccessor accessor) : ICurrentActor
{
    public Guid? DeviceId => Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(InvyClaims.DeviceId), out var id) ? id : null;

    public DeviceRole Role => Enum.TryParse<DeviceRole>(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role), out var role) ? role : DeviceRole.Viewer;
}

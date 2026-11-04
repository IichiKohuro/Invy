namespace Invy.Contracts.Server;

/// <summary>Ответ <c>GET /api/v1/info</c>: кто этот сервер и с какими клиентами он совместим.</summary>
public sealed record ServerInfoDto(
    Guid ServerId,
    string Name,
    string ServerVersion,
    int ApiVersion,
    string MinClientVersion);

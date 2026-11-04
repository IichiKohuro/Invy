namespace Invy.Contracts;

/// <summary>Маршруты и порты, общие для сервера и клиентов.</summary>
public static class ApiRoutes
{
    public const int ApiVersion = 1;
    public const string Prefix = "/api/v1";

    public const string Health = "/health";
    public const string Info = Prefix + "/info";

    /// <summary>Корень раздачи обновлений: <c>/updates/win</c> — фид Velopack, <c>/updates/android</c> — APK.</summary>
    public const string Updates = "/updates";

    public const int DefaultHttpPort = 5470;
    public const int DefaultDiscoveryPort = 5471;
}

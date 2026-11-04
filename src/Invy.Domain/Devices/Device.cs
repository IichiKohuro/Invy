namespace Invy.Domain.Devices;

/// <summary>Сопряжённое устройство. Токен хранится только в виде SHA-256.</summary>
public sealed class Device
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Name { get; set; } = string.Empty;

    public DevicePlatform Platform { get; set; }

    public DeviceRole Role { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public string? AppVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive => RevokedAt is null;
}

public enum DeviceRole
{
    Viewer = 0,
    Editor = 1,
    Admin = 2,
}

public enum DevicePlatform
{
    Other = 0,
    Windows = 1,
    Android = 2,
}

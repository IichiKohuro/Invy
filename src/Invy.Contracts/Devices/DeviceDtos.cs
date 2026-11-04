using System.Text.Json.Serialization;
using Invy.Contracts.Sync;

namespace Invy.Contracts.Devices;

[JsonConverter(typeof(JsonStringEnumConverter<DeviceRole>))]
public enum DeviceRole
{
    /// <summary>Только просмотр (например, детский планшет).</summary>
    Viewer = 0,

    /// <summary>Просмотр и изменение данных.</summary>
    Editor = 1,

    /// <summary>Всё, включая устройства, бэкапы и восстановление.</summary>
    Admin = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter<DevicePlatform>))]
public enum DevicePlatform
{
    Other = 0,
    Windows = 1,
    Android = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter<PairingStatus>))]
public enum PairingStatus
{
    Pending = 0,
    Approved = 1,
    Denied = 2,
    Expired = 3,
}

/// <summary>Запрос подключения нового устройства.</summary>
public sealed record PairingRequestCreate(string DeviceName, DevicePlatform Platform, string? AppVersion = null);

/// <summary>
/// Состояние запроса. <see cref="RequestId"/> знает только запросивший — по нему он ждёт решения.
/// <see cref="Code"/> показывается на обоих экранах для сверки. <see cref="Token"/> выдаётся один раз — после одобрения.
/// </summary>
public sealed record PairingRequestResponse(
    Guid RequestId,
    string Code,
    PairingStatus Status,
    string? Token = null,
    Guid? DeviceId = null,
    DeviceRole? Role = null);

/// <summary>Запрос, ожидающий решения администратора.</summary>
public sealed record PendingPairingDto(Guid RequestId, string DeviceName, DevicePlatform Platform, string? AppVersion, string Code, DateTimeOffset CreatedAt);

public sealed record ApprovePairingRequest(DeviceRole Role = DeviceRole.Editor, string? Name = null);

public sealed record DeviceDto(
    Guid Id,
    string Name,
    DevicePlatform Platform,
    DeviceRole Role,
    string? AppVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt,
    bool IsRevoked);

public sealed record UpdateDeviceRequest(string? Name = null, DeviceRole? Role = null);

/// <summary>Кто я: устройство текущего запроса (в режиме «Доверенная сеть» — без id).</summary>
public sealed record CurrentDeviceDto(Guid? DeviceId, string Name, DeviceRole Role);

/// <summary>События SignalR-хаба <see cref="HubRoutes.Inventory"/> (сервер → клиент).</summary>
public interface IInventoryClient
{
    /// <summary>Изменения одной операции после коммита.</summary>
    Task Changed(ChangeBatch batch);

    /// <summary>Новое устройство просит подключения — только администраторам.</summary>
    Task PairingRequested(PendingPairingDto request);
}

public static class HubRoutes
{
    public const string Inventory = "/hubs/inventory";
}

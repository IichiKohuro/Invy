using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Invy.Application.Abstractions;
using Invy.Application.Errors;
using Invy.Contracts.Devices;
using Invy.Domain.Devices;
using Microsoft.EntityFrameworkCore;
using ContractPlatform = Invy.Contracts.Devices.DevicePlatform;
using ContractRole = Invy.Contracts.Devices.DeviceRole;
using DomainPlatform = Invy.Domain.Devices.DevicePlatform;
using DomainRole = Invy.Domain.Devices.DeviceRole;

namespace Invy.Application.Devices;

/// <summary>Токен устройства: 256 бит случайных данных; на сервере хранится только SHA-256.</summary>
public static class DeviceTokens
{
    public static string Create() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>Уведомления администраторов (реализация — SignalR).</summary>
public interface IAdminNotifier
{
    Task PairingRequestedAsync(PendingPairingDto request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Ожидающие запросы сопряжения — в памяти: живут 10 минут и не переживают перезапуск сервера
/// (устройство просто запросит подключение снова).
/// </summary>
public sealed class PairingStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<Guid, PairingRequestState> _requests = new();

    internal PairingRequestState Add(PairingRequestState state)
    {
        _requests[state.Id] = state;
        return state;
    }

    internal PairingRequestState? Find(Guid id, DateTimeOffset now)
    {
        Cleanup(now);
        return _requests.GetValueOrDefault(id);
    }

    internal IReadOnlyList<PairingRequestState> Pending(DateTimeOffset now)
    {
        Cleanup(now);
        return _requests.Values.Where(r => r.Status == PairingStatus.Pending).OrderBy(r => r.CreatedAt).ToList();
    }

    private void Cleanup(DateTimeOffset now)
    {
        foreach (var expired in _requests.Values.Where(r => r.ExpiresAt < now))
        {
            _requests.TryRemove(expired.Id, out _);
        }
    }
}

internal sealed class PairingRequestState
{
    public Guid Id { get; init; }

    public required string DeviceName { get; init; }

    public ContractPlatform Platform { get; init; }

    public string? AppVersion { get; init; }

    public required string Code { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public PairingStatus Status { get; set; }

    /// <summary>Выдаётся запросившему один раз, затем стирается.</summary>
    public string? Token { get; set; }

    public Guid? DeviceId { get; set; }

    public ContractRole? Role { get; set; }
}

/// <summary>
/// Сопряжение: устройство просит подключения и показывает код; администратор сверяет код и одобряет.
/// Первое устройство на новом сервере, подключающееся с того же ПК, становится администратором без подтверждения.
/// </summary>
public sealed class PairingService(IInventoryDbContext db, IUnitOfWork unitOfWork, PairingStore store, IAdminNotifier notifier, TimeProvider clock)
{
    public async Task<PairingRequestResponse> RequestAsync(PairingRequestCreate request, bool fromSameComputer, CancellationToken cancellationToken)
    {
        var name = request.DeviceName?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100)
        {
            throw new ValidationException("deviceName", "Имя устройства — от 1 до 100 символов");
        }

        var now = clock.GetUtcNow();
        var state = store.Add(new PairingRequestState
        {
            Id = Guid.NewGuid(),   // случайный v4: id запроса — секрет запросившего
            DeviceName = name,
            Platform = request.Platform,
            AppVersion = request.AppVersion?.Trim(),
            Code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture),
            CreatedAt = now,
            ExpiresAt = now + PairingStore.Lifetime,
        });

        if (fromSameComputer && !await db.Devices.AnyAsync(d => d.RevokedAt == null, cancellationToken))
        {
            await ApproveAsync(state, ContractRole.Admin, null, cancellationToken);
            return TakeStatus(state);
        }

        await notifier.PairingRequestedAsync(ToPending(state), cancellationToken);
        return new PairingRequestResponse(state.Id, state.Code, state.Status);
    }

    /// <summary>Состояние запроса для запросившего; токен отдаётся один раз.</summary>
    public PairingRequestResponse GetStatus(Guid requestId)
    {
        var state = store.Find(requestId, clock.GetUtcNow()) ?? throw new NotFoundException("Запрос не найден или истёк — запросите подключение снова");
        return TakeStatus(state);
    }

    public IReadOnlyList<PendingPairingDto> ListPending() => store.Pending(clock.GetUtcNow()).Select(ToPending).ToList();

    public async Task<DeviceDto> ApproveAsync(Guid requestId, ApprovePairingRequest request, CancellationToken cancellationToken)
    {
        var state = store.Find(requestId, clock.GetUtcNow()) ?? throw new NotFoundException("Запрос не найден или истёк");
        if (state.Status != PairingStatus.Pending)
        {
            throw new ValidationException("requestId", "Запрос уже обработан");
        }

        var device = await ApproveAsync(state, request.Role, request.Name, cancellationToken);
        return DeviceService.ToDto(device);
    }

    public void Deny(Guid requestId)
    {
        var state = store.Find(requestId, clock.GetUtcNow()) ?? throw new NotFoundException("Запрос не найден или истёк");
        if (state.Status == PairingStatus.Pending)
        {
            state.Status = PairingStatus.Denied;
        }
    }

    private async Task<Device> ApproveAsync(PairingRequestState state, ContractRole role, string? name, CancellationToken cancellationToken)
    {
        var token = DeviceTokens.Create();
        var device = new Device
        {
            Name = string.IsNullOrWhiteSpace(name) ? state.DeviceName : name.Trim(),
            Platform = (DomainPlatform)state.Platform,
            Role = (DomainRole)role,
            TokenHash = DeviceTokens.Hash(token),
            AppVersion = state.AppVersion,
        };
        db.Devices.Add(device);
        await unitOfWork.SaveAsync(cancellationToken: cancellationToken);

        state.Status = PairingStatus.Approved;
        state.Token = token;
        state.DeviceId = device.Id;
        state.Role = role;
        return device;
    }

    private static PairingRequestResponse TakeStatus(PairingRequestState state)
    {
        var response = new PairingRequestResponse(state.Id, state.Code, state.Status, state.Token, state.DeviceId, state.Role);
        state.Token = null;
        return response;
    }

    private static PendingPairingDto ToPending(PairingRequestState s) => new(s.Id, s.DeviceName, s.Platform, s.AppVersion, s.Code, s.CreatedAt);
}

/// <summary>Управление устройствами: список, роль и имя, отзыв доступа.</summary>
public sealed class DeviceService(IInventoryDbContext db, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<IReadOnlyList<DeviceDto>> ListAsync(CancellationToken cancellationToken) =>
        (await db.Devices.AsNoTracking().ToListAsync(cancellationToken)).OrderBy(d => d.RevokedAt is not null).ThenBy(d => d.Name, StringComparer.CurrentCulture).Select(ToDto).ToList();

    public async Task<DeviceDto> UpdateAsync(Guid id, UpdateDeviceRequest request, CancellationToken cancellationToken)
    {
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id, cancellationToken) ?? throw NotFoundException.For("Устройство", id);
        if (request.Name is { } name)
        {
            device.Name = name.Trim() is { Length: > 0 and <= 100 } trimmed ? trimmed : throw new ValidationException("name", "Имя устройства — от 1 до 100 символов");
        }

        if (request.Role is { } role && (DomainRole)role != device.Role)
        {
            if (device.Role == DomainRole.Admin && await IsLastAdminAsync(device.Id, cancellationToken))
            {
                throw new ValidationException("role", "Это единственный администратор — сначала назначьте другого");
            }

            device.Role = (DomainRole)role;
        }

        await unitOfWork.SaveAsync(cancellationToken: cancellationToken);
        return ToDto(device);
    }

    public async Task RevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id, cancellationToken) ?? throw NotFoundException.For("Устройство", id);
        if (device.RevokedAt is not null)
        {
            return;
        }

        if (device.Role == DomainRole.Admin && await IsLastAdminAsync(device.Id, cancellationToken))
        {
            throw new ValidationException("id", "Нельзя отозвать доступ у единственного администратора");
        }

        device.RevokedAt = clock.GetUtcNow();
        await unitOfWork.SaveAsync(cancellationToken: cancellationToken);
    }

    internal static DeviceDto ToDto(Device d) =>
        new(d.Id, d.Name, (ContractPlatform)d.Platform, (ContractRole)d.Role, d.AppVersion, d.CreatedAt, d.LastSeenAt, d.RevokedAt is not null);

    private Task<bool> IsLastAdminAsync(Guid deviceId, CancellationToken cancellationToken) =>
        db.Devices.AllAsync(d => d.Id == deviceId || d.Role != DomainRole.Admin || d.RevokedAt != null, cancellationToken);
}

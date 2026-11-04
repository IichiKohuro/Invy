using Invy.Api.Auth;
using Invy.Application.Abstractions;
using Invy.Application.Devices;
using Invy.Contracts;
using Invy.Contracts.Devices;
using Invy.Contracts.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Invy.Api.Realtime;

/// <summary>
/// Хаб реального времени <c>/hubs/inventory</c>. Клиенты только слушают: изменения приходят событием
/// <see cref="IInventoryClient.Changed"/> после коммита; администраторы получают запросы подключения устройств.
/// </summary>
[Authorize(Policy = Policies.Viewer)]
internal sealed class InventoryHub : Hub<IInventoryClient>
{
    public const string AdminsGroup = "admins";

    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsInRole(nameof(Domain.Devices.DeviceRole.Admin)) == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);
        }

        await base.OnConnectedAsync();
    }
}

internal sealed class SignalRChangeNotifier(IHubContext<InventoryHub, IInventoryClient> hub) : IChangeNotifier
{
    public Task PublishAsync(ChangeBatch batch, CancellationToken cancellationToken = default) => hub.Clients.All.Changed(batch);
}

internal sealed class SignalRAdminNotifier(IHubContext<InventoryHub, IInventoryClient> hub) : IAdminNotifier
{
    public Task PairingRequestedAsync(PendingPairingDto request, CancellationToken cancellationToken = default) =>
        hub.Clients.Group(InventoryHub.AdminsGroup).PairingRequested(request);
}

internal static class RealtimeRegistration
{
    /// <summary>Регистрировать до AddInvyApplication/AddInvyInfrastructure: там — заглушки через TryAdd.</summary>
    public static IServiceCollection AddInvyRealtime(this IServiceCollection services)
    {
        services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, InvyJsonContext.Default));
        services.AddSingleton<IChangeNotifier, SignalRChangeNotifier>();
        services.AddSingleton<IAdminNotifier, SignalRAdminNotifier>();
        return services;
    }
}

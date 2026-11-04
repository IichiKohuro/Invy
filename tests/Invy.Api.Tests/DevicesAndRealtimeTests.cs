using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Invy.Contracts;
using Invy.Contracts.Catalog;
using Invy.Contracts.Devices;
using Invy.Contracts.Sync;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Invy.Api.Tests;

/// <summary>Сервер в режиме сопряжения. Заголовок <c>X-Test-Loopback</c> имитирует запрос с того же компьютера.</summary>
public sealed class PairingApiFactory : InvyApiFactory
{
    protected override string AuthMode => "Pairing";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, LoopbackFilter>());
    }

    private sealed class LoopbackFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = context.Request.Headers.ContainsKey("X-Test-Loopback") ? IPAddress.Loopback : IPAddress.Parse("192.168.1.50");
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}

public sealed class DevicesAndRealtimeTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private PairingApiFactory _factory = null!;

    public ValueTask InitializeAsync()
    {
        _factory = new PairingApiFactory();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Pairing_flow_with_roles_and_revocation()
    {
        var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/types", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Первое устройство с этого же компьютера — администратор без подтверждения.
        var pc = await RequestPairingAsync("Домашний ПК", DevicePlatform.Windows, sameComputer: true);
        pc.Status.ShouldBe(PairingStatus.Approved);
        pc.Role.ShouldBe(DeviceRole.Admin);
        var admin = Client(pc.Token!);
        (await admin.GetJsonAsync<CurrentDeviceDto>("/api/v1/devices/me")).ShouldBe(new CurrentDeviceDto(pc.DeviceId, "Домашний ПК", DeviceRole.Admin));

        // Администратор слушает хаб и получает запрос телефона.
        await using var adminHub = Hub(pc.Token);
        var requested = new TaskCompletionSource<PendingPairingDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        adminHub.On<PendingPairingDto>(nameof(IInventoryClient.PairingRequested), r => requested.TrySetResult(r));
        await adminHub.StartAsync(Ct);

        var phone = await RequestPairingAsync("Pixel 7", DevicePlatform.Android, sameComputer: false);
        phone.Status.ShouldBe(PairingStatus.Pending);
        phone.Code.Length.ShouldBe(6);
        phone.Token.ShouldBeNull();
        (await requested.Task.WaitAsync(Timeout, Ct)).Code.ShouldBe(phone.Code);
        (await admin.GetJsonAsync<IReadOnlyList<PendingPairingDto>>("/api/v1/pairing/requests")).ShouldContain(r => r.RequestId == phone.RequestId);

        var device = await admin.PostJsonAsync<ApprovePairingRequest, DeviceDto>($"/api/v1/pairing/requests/{phone.RequestId}/approve", new ApprovePairingRequest(DeviceRole.Viewer, "Телефон"));
        device.Role.ShouldBe(DeviceRole.Viewer);

        var approved = await anonymous.GetJsonAsync<PairingRequestResponse>($"/api/v1/pairing/requests/{phone.RequestId}");
        approved.Status.ShouldBe(PairingStatus.Approved);
        approved.Token.ShouldNotBeNullOrEmpty();
        (await anonymous.GetJsonAsync<PairingRequestResponse>($"/api/v1/pairing/requests/{phone.RequestId}")).Token.ShouldBeNull();   // выдаётся один раз

        // Просмотр — можно, изменения — нет.
        var viewer = Client(approved.Token!);
        (await viewer.GetAsync("/api/v1/types", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.PostAsJsonAsync("/api/v1/types", new SaveItemTypeRequest("Коробка", TypeRole.Container, []), HttpJson.Info<SaveItemTypeRequest>(), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Отзыв доступа.
        (await admin.DeleteAsync($"/api/v1/devices/{device.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await viewer.GetAsync("/api/v1/types", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await admin.SendExpectingErrorAsync(HttpMethod.Delete, $"/api/v1/devices/{pc.DeviceId}")).Status.ShouldBe(422);   // единственный администратор
    }

    [Fact]
    public async Task Denied_or_second_local_request_gets_no_token()
    {
        var pc = await RequestPairingAsync("ПК", DevicePlatform.Windows, sameComputer: true);
        var admin = Client(pc.Token!);

        // Устройства уже есть — даже с того же компьютера нужно подтверждение.
        var second = await RequestPairingAsync("Ноутбук", DevicePlatform.Windows, sameComputer: true);
        second.Status.ShouldBe(PairingStatus.Pending);

        (await admin.PostAsync($"/api/v1/pairing/requests/{second.RequestId}/deny", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var denied = await _factory.CreateClient().GetJsonAsync<PairingRequestResponse>($"/api/v1/pairing/requests/{second.RequestId}");
        denied.Status.ShouldBe(PairingStatus.Denied);
        denied.Token.ShouldBeNull();
    }

    [Fact]
    public async Task Hub_requires_device_token()
    {
        await using var hub = Hub(token: null);

        await Should.ThrowAsync<HttpRequestException>(() => hub.StartAsync(Ct));
    }

    [Fact]
    public async Task Connected_clients_receive_changes_after_commit()
    {
        var pc = await RequestPairingAsync("ПК", DevicePlatform.Windows, sameComputer: true);
        await using var first = Hub(pc.Token);
        await using var second = Hub(pc.Token);
        var firstBatch = new TaskCompletionSource<ChangeBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondBatch = new TaskCompletionSource<ChangeBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.On<ChangeBatch>(nameof(IInventoryClient.Changed), b => firstBatch.TrySetResult(b));
        second.On<ChangeBatch>(nameof(IInventoryClient.Changed), b => secondBatch.TrySetResult(b));
        await first.StartAsync(Ct);
        await second.StartAsync(Ct);

        var type = await Client(pc.Token!).PostJsonAsync<SaveItemTypeRequest, ItemTypeDto>("/api/v1/types", new SaveItemTypeRequest("Коробка", TypeRole.Container, []));

        var a = await firstBatch.Task.WaitAsync(Timeout, Ct);
        var b = await secondBatch.Task.WaitAsync(Timeout, Ct);
        a.ShouldBeEquivalentTo(b);
        a.DeviceId.ShouldBe(pc.DeviceId);
        var change = a.Changes.ShouldHaveSingleItem();
        (change.EntityType, change.EntityId, change.Action).ShouldBe((ChangeEntityType.ItemType, type.Id, ChangeAction.Created));

        // Пропущенное догружается по seq.
        var feed = await Client(pc.Token!).GetJsonAsync<SyncChangesResponse>($"/api/v1/sync/changes?since={a.FirstSeq - 1}");
        feed.Changes.ShouldContain(c => c.Seq == change.Seq && c.EntityId == type.Id);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<PairingRequestResponse> RequestPairingAsync(string name, DevicePlatform platform, bool sameComputer)
    {
        var client = _factory.CreateClient();
        if (sameComputer)
        {
            client.DefaultRequestHeaders.Add("X-Test-Loopback", "1");
        }

        return await client.PostJsonAsync<PairingRequestCreate, PairingRequestResponse>("/api/v1/pairing/requests", new PairingRequestCreate(name, platform, "0.1.0"));
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HubConnection Hub(string? token) => new HubConnectionBuilder()
        .WithUrl(new Uri(_factory.Server.BaseAddress, HubRoutes.Inventory), options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            options.AccessTokenProvider = () => Task.FromResult(token);
        })
        .AddJsonProtocol(o => o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, InvyJsonContext.Default))
        .Build();
}

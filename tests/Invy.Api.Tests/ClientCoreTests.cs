using System.Net.Http.Headers;
using Invy.Client.Core.Api;
using Invy.Client.Core.Catalog;
using Invy.Client.Core.Connection;
using Invy.Contracts;
using Invy.Contracts.Catalog;
using Invy.Contracts.Devices;
using Invy.Contracts.Items;
using Invy.Contracts.Sync;
using SkiaSharp;

namespace Invy.Api.Tests;

/// <summary>Клиентское ядро (Invy.Client.Core) против настоящего сервера в памяти.</summary>
public sealed class ClientCoreTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private InvyApiFactory _trusted = null!;
    private PairingApiFactory _pairing = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _trusted = new InvyApiFactory();
        _pairing = new PairingApiFactory();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _trusted.DisposeAsync();
        await _pairing.DisposeAsync();
    }

    [Fact]
    public async Task Trusted_lan_connects_without_pairing_and_maps_api_errors()
    {
        var connection = Connection(_trusted, new InMemorySettingsStore());

        (await connection.ConnectAsync(_trusted.Server.BaseAddress, Ct)).ShouldBeTrue();
        connection.Status.ShouldBe(ConnectionStatus.Connected);
        connection.Me!.Role.ShouldBe(DeviceRole.Admin);
        connection.CanEdit.ShouldBeTrue();

        var api = connection.Api!;
        var place = await api.CreateTypeAsync(new SaveItemTypeRequest("Место", TypeRole.Place, []), Ct);
        var attic = await api.CreateItemAsync(new SaveItemRequest(place.Id, Name: "Чердак"), Ct);
        (await api.GetRootsAsync(ct: Ct)).Items.ShouldContain(i => i.Id == attic.Id);

        var invalid = await Should.ThrowAsync<ApiException>(() => api.CreateItemAsync(new SaveItemRequest(place.Id), Ct));
        invalid.IsValidation.ShouldBeTrue();
        invalid.Errors["name"].ShouldContain("Укажите название");

        await api.UpdateItemAsync(attic.Id, new SaveItemRequest(place.Id, Name: "Мансарда", Version: attic.Version), Ct);
        var conflict = await Should.ThrowAsync<ApiException>(() => api.UpdateItemAsync(attic.Id, new SaveItemRequest(place.Id, Name: "Чердак", Version: attic.Version), Ct));
        conflict.IsConflict.ShouldBeTrue();
        conflict.CurrentAs(InvyJsonContext.Default.ItemDetailsDto)!.Name.ShouldBe("Мансарда");
    }

    [Fact]
    public async Task Pairing_waits_for_admin_and_remembers_token()
    {
        var admin = await AdminClientAsync();
        var settings = new InMemorySettingsStore();
        var connection = Connection(_pairing, settings);

        var connecting = connection.ConnectAsync(_pairing.Server.BaseAddress, Ct);
        await WaitUntilAsync(() => connection.Status == ConnectionStatus.WaitingForApproval);
        connection.PairingCode!.Length.ShouldBe(6);

        var pending = (await admin.GetPendingPairingsAsync(Ct)).Single(p => p.Code == connection.PairingCode);
        await admin.ApprovePairingAsync(pending.RequestId, new ApprovePairingRequest(DeviceRole.Editor), Ct);

        (await connecting.WaitAsync(Timeout, Ct)).ShouldBeTrue();
        connection.Me!.Role.ShouldBe(DeviceRole.Editor);
        settings.Current.ProtectedToken.ShouldNotBeNullOrEmpty();
        settings.Current.ServerId.ShouldBe(connection.Server!.ServerId);

        // Повторный запуск приложения: токен из настроек, без нового запроса подключения.
        var again = Connection(_pairing, settings);
        (await again.ConnectAsync(_pairing.Server.BaseAddress, Ct)).ShouldBeTrue();
        again.Me!.DeviceId.ShouldBe(connection.Me.DeviceId);
        (await admin.GetPendingPairingsAsync(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Realtime_delivers_changes_in_order_and_catches_up()
    {
        var connection = Connection(_trusted, new InMemorySettingsStore());
        await connection.ConnectAsync(_trusted.Server.BaseAddress, Ct);
        await using var first = new RealtimeService(ImmediateDispatcher.Instance) { LongPollingOnly = true };
        await using var second = new RealtimeService(ImmediateDispatcher.Instance) { LongPollingOnly = true };
        var firstBatches = new List<ChangeBatch>();
        var secondBatches = new List<ChangeBatch>();
        first.ChangesReceived += b => { lock (firstBatches) { firstBatches.Add(b); } };
        second.ChangesReceived += b => { lock (secondBatches) { secondBatches.Add(b); } };
        await first.StartAsync(connection, Ct);
        await second.StartAsync(connection, Ct);

        var type = await connection.Api!.CreateTypeAsync(new SaveItemTypeRequest("Коробка", TypeRole.Container, []), Ct);
        await WaitUntilAsync(() => firstBatches.Count == 1 && secondBatches.Count == 1);
        firstBatches[0].Changes.ShouldHaveSingleItem().EntityId.ShouldBe(type.Id);
        first.LastSeq.ShouldBe(firstBatches[0].LastSeq);

        // Повторная догрузка без пропусков ничего не дублирует…
        await first.CatchUpAsync(Ct);
        firstBatches.Count.ShouldBe(1);

        // …а после пропуска приносит недостающее по порядку.
        first.LastSeq -= 1;
        await first.CatchUpAsync(Ct);
        firstBatches.Count.ShouldBe(2);
        firstBatches[1].Changes.ShouldHaveSingleItem().EntityId.ShouldBe(type.Id);
    }

    [Fact]
    public async Task Catalog_reloads_on_changes_and_photos_round_trip()
    {
        var connection = Connection(_trusted, new InMemorySettingsStore());
        await connection.ConnectAsync(_trusted.Server.BaseAddress, Ct);
        var api = connection.Api!;
        var catalog = new CatalogStore();
        await catalog.LoadAsync(api, Ct);
        var before = catalog.Types.Count;

        (await api.ApplyStarterKitAsync(Ct)).TypesCreated.ShouldBe(10);
        var refreshed = await catalog.HandleAsync(new ChangeBatch(1, 2, null, null, [new ChangeDto(1, ChangeEntityType.ItemType, Guid.Empty, ChangeAction.Created), new ChangeDto(2, ChangeEntityType.ValueList, Guid.Empty, ChangeAction.Created)]), Ct);

        refreshed.ShouldBeTrue();
        catalog.Types.Count.ShouldBe(before + 10);
        var cable = catalog.ActiveTypes.Single(t => t.Name == "Кабель");
        CatalogStore.EffectiveFields(cable).Select(f => f.Name).ShouldBe(["Длина", "Разъём 1", "Разъём 2", "Версия USB", "Цвет"]);
        catalog.List(CatalogStore.EffectiveFields(cable)[1].ValueListId)!.Name.ShouldBe("Разъёмы");

        var box = await api.CreateItemAsync(new SaveItemRequest(catalog.ActiveTypes.Single(t => t.Name == "Коробка").Id, Name: "Серая коробка"), Ct);
        using var bitmap = new SKBitmap(64, 32);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var photo = (await api.UploadPhotosAsync(box.Id, [("box.png", new MemoryStream(png.ToArray()))], Ct)).Single();
        (await api.GetItemAsync(box.Id, Ct)).CoverMediaId.ShouldBe(photo.Id);
        (await api.GetPhotoAsync(photo.Id, PhotoSize.Original, Ct)).ShouldBe(png.ToArray());
    }

    [Theory]
    [InlineData("192.168.1.10", "http://192.168.1.10:5470/")]
    [InlineData("home-pc:8080", "http://home-pc:8080/")]
    [InlineData("https://invy.local/api", "https://invy.local/")]   // HTTPS без порта — стандартный 443
    [InlineData("http://home-pc", "http://home-pc:5470/")]
    [InlineData("", null)]
    [InlineData("ftp://x", null)]
    public void Server_address_is_normalized(string input, string? expected) =>
        ServerConnection.ParseServerAddress(input)?.ToString().ShouldBe(expected);

    private static ServerConnection Connection(InvyApiFactory factory, ISettingsStore settings) =>
        new(settings, new PlainTokenProtector(), () => factory.Server.CreateHandler(), "Тестовый ПК", DevicePlatform.Windows, "0.2.0")
        {
            PairingPollInterval = TimeSpan.FromMilliseconds(50),
        };

    private async Task<InvyApiClient> AdminClientAsync()
    {
        var http = _pairing.CreateClient();
        http.DefaultRequestHeaders.Add("X-Test-Loopback", "1");
        var response = await http.PostJsonAsync<PairingRequestCreate, PairingRequestResponse>("/api/v1/pairing/requests", new PairingRequestCreate("Сервер", DevicePlatform.Windows));
        var adminHttp = _pairing.CreateClient();
        adminHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", response.Token);
        return new InvyApiClient(adminHttp);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Условие не выполнилось вовремя");
            }

            await Task.Delay(20, Ct);
        }
    }
}

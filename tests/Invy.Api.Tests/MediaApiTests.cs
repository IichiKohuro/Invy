using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Invy.Contracts.Catalog;
using Invy.Contracts.Items;
using SkiaSharp;

namespace Invy.Api.Tests;

/// <summary>Фото: загрузка, превью с поворотом по EXIF, обложка, удаление; стартовый набор типов.</summary>
public sealed class MediaApiTests : IClassFixture<InvyApiFactory>
{
    private readonly HttpClient _http;

    public MediaApiTests(InvyApiFactory factory) => _http = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Photos_are_uploaded_previewed_and_set_as_cover()
    {
        var item = await CreateItemAsync();
        var wide = Image(3000, 1500, SKEncodedImageFormat.Png);
        var small = Image(640, 480, SKEncodedImageFormat.Jpeg);

        var uploaded = await UploadAsync(item.Id, ("wide.png", wide), ("small.jpg", small));

        uploaded.Select(p => (p.FileName, p.Width, p.Height, p.ContentType)).ShouldBe([("wide.png", 3000, 1500, "image/png"), ("small.jpg", 640, 480, "image/jpeg")]);
        var details = await _http.GetJsonAsync<ItemDetailsDto>($"/api/v1/items/{item.Id}");
        details.Photos.Count.ShouldBe(2);
        details.CoverMediaId.ShouldBe(uploaded[0].Id);   // первое фото — обложка

        using var thumb = await _http.GetAsync($"/api/v1/media/{uploaded[0].Id}?size=Thumb", Ct);
        thumb.StatusCode.ShouldBe(HttpStatusCode.OK);
        thumb.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
        thumb.Headers.CacheControl!.ToString().ShouldContain("immutable");
        thumb.Headers.ETag.ShouldNotBeNull();
        using (var preview = SKBitmap.Decode(await thumb.Content.ReadAsByteArrayAsync(Ct)))
        {
            (preview.Width, preview.Height).ShouldBe((256, 128));
        }

        (await _http.GetByteArrayAsync($"/api/v1/media/{uploaded[1].Id}?size=Original", Ct)).ShouldBe(small);

        (await _http.PutAsJsonAsync($"/api/v1/items/{item.Id}/cover", new SetCoverRequest(uploaded[1].Id), HttpJson.Info<SetCoverRequest>(), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _http.GetJsonAsync<ItemDetailsDto>($"/api/v1/items/{item.Id}")).CoverMediaId.ShouldBe(uploaded[1].Id);

        (await _http.DeleteAsync($"/api/v1/media/{uploaded[1].Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var after = await _http.GetJsonAsync<ItemDetailsDto>($"/api/v1/items/{item.Id}");
        after.Photos.ShouldHaveSingleItem().Id.ShouldBe(uploaded[0].Id);
        after.CoverMediaId.ShouldBe(uploaded[0].Id);   // обложка переходит на оставшееся фото

        var history = await _http.GetJsonAsync<PagedResult<AuditEntryDto>>($"/api/v1/items/{item.Id}/history");
        history.Items.ShouldContain(h => h.Changes.Any(c => c.Label == "Фото" && c.New == "wide.png"));
    }

    [Fact]
    public async Task Preview_follows_exif_orientation()
    {
        var item = await CreateItemAsync();
        var rotated = WithExifOrientation(Image(400, 200, SKEncodedImageFormat.Jpeg), orientation: 6);   // повернуть на 90°

        var photo = (await UploadAsync(item.Id, ("phone.jpg", rotated))).Single();

        (photo.Width, photo.Height).ShouldBe((200, 400));
        using var thumb = SKBitmap.Decode(await _http.GetByteArrayAsync($"/api/v1/media/{photo.Id}?size=Thumb", Ct));
        (thumb.Width, thumb.Height).ShouldBe((128, 256));
    }

    [Fact]
    public async Task Non_image_files_are_rejected()
    {
        var item = await CreateItemAsync();
        using var form = new MultipartFormDataContent { { new ByteArrayContent("не картинка"u8.ToArray()), "files", "notes.txt" } };

        using var response = await _http.PostAsync($"/api/v1/items/{item.Id}/media", form, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("JPEG, PNG");
    }

    [Fact]
    public async Task Starter_kit_creates_types_once()
    {
        var first = await _http.PostJsonAsync<object?, StarterKitResult>("/api/v1/setup/starter-kit", null);
        var second = await _http.PostJsonAsync<object?, StarterKitResult>("/api/v1/setup/starter-kit", null);

        first.ShouldBe(new StarterKitResult(10, 5));
        second.ShouldBe(new StarterKitResult(0, 0));
        var cable = (await _http.GetJsonAsync<IReadOnlyList<ItemTypeDto>>("/api/v1/types")).Single(t => t.Name == "Кабель");
        cable.TitleTemplate.ShouldBe("{Разъём 1} → {Разъём 2}, {Длина} см");
        cable.Icon.ShouldBe(new IconDto(IconKind.Emoji, "🔌"));
        cable.Fields.Select(f => f.Name).ShouldBe(["Длина", "Разъём 1", "Разъём 2", "Версия USB", "Цвет"]);
    }

    private async Task<ItemDetailsDto> CreateItemAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var type = await _http.PostJsonAsync<SaveItemTypeRequest, ItemTypeDto>("/api/v1/types", new SaveItemTypeRequest($"Коробка {suffix}", TypeRole.Container, []));
        return await _http.PostJsonAsync<SaveItemRequest, ItemDetailsDto>("/api/v1/items", new SaveItemRequest(type.Id, Name: $"Синяя коробка {suffix}"));
    }

    private async Task<IReadOnlyList<MediaDto>> UploadAsync(Guid itemId, params (string Name, byte[] Bytes)[] files)
    {
        using var form = new MultipartFormDataContent();
        foreach (var (name, bytes) in files)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(content, "files", name);
        }

        using var response = await _http.PostAsync($"/api/v1/items/{itemId}/media", form, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync(HttpJson.Info<IReadOnlyList<MediaDto>>(), Ct))!;
    }

    private static byte[] Image(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.SteelBlue);
            using var paint = new SKPaint { Color = SKColors.Orange };
            canvas.DrawRect(0, 0, width / 2f, height / 2f, paint);
        }

        using var data = bitmap.Encode(format, 90);
        return data.ToArray();
    }

    /// <summary>Вставляет в JPEG сегмент APP1/Exif с тегом Orientation — как у фото с телефона.</summary>
    private static byte[] WithExifOrientation(byte[] jpeg, ushort orientation)
    {
        byte[] tiff =
        [
            .."MM"u8, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,   // big-endian TIFF, IFD0 по смещению 8
            0x00, 0x01,                                      // одна запись
            0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01,  // Orientation, SHORT, count 1
            (byte)(orientation >> 8), (byte)orientation, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,                          // следующего IFD нет
        ];
        byte[] payload = [.."Exif"u8, 0x00, 0x00, .. tiff];
        var length = payload.Length + 2;
        byte[] segment = [0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. payload];
        return [.. jpeg[..2], .. segment, .. jpeg[2..]];
    }
}

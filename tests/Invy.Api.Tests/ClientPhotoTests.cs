using Invy.Client.Core.Api;
using Invy.Client.Core.Connection;
using Invy.Client.Core.Editor;
using Invy.Client.Core.Explorer;
using Invy.Client.Core.Photos;
using Invy.Client.Core.Session;
using Invy.Contracts.Catalog;
using Invy.Contracts.Devices;
using Invy.Contracts.Items;
using SkiaSharp;

namespace Invy.Api.Tests;

/// <summary>Фото, обложка, кэш превью, своя иконка и цвет — клиентское ядро против сервера в памяти.</summary>
public sealed class ClientPhotoTests : IAsyncLifetime
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "invy-photo-cache", Guid.NewGuid().ToString("N"));
    private InvyApiFactory _server = null!;
    private ClientSession _session = null!;
    private FakeDialogs _dialogs = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _server = new InvyApiFactory();
        var settings = new InMemorySettingsStore();
        var connection = new ServerConnection(settings, new PlainTokenProtector(), () => _server.Server.CreateHandler(), "Тест", DevicePlatform.Windows, "0.2.0");
        (await connection.ConnectAsync(_server.Server.BaseAddress, Ct)).ShouldBeTrue();
        await connection.Api!.ApplyStarterKitAsync(Ct);
        _dialogs = new FakeDialogs();
        _session = new ClientSession(connection, settings, _dialogs, ImmediateDispatcher.Instance, photoCacheDirectory: _cacheDir);
        await _session.Catalog.LoadAsync(_session.Api, Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _session.DisposeAsync();
        await _server.DisposeAsync();
        if (Directory.Exists(_cacheDir))
        {
            Directory.Delete(_cacheDir, recursive: true);
        }
    }

    [Fact]
    public async Task New_item_uploads_photos_after_save_with_chosen_cover()
    {
        var editor = ItemEditorViewModel.ForNew(_session, null, Type("Инструмент").Id);
        editor.Name = "Шуруповёрт";
        (await editor.Photos.AddFilesAsync([Png("front.png", SKColors.Red), Png("side.png", SKColors.Blue)])).ShouldBeTrue();
        editor.Photos.Photos.ShouldAllBe(p => p.IsPending && p.Thumbnail != null);
        editor.Photos.Photos[0].IsCover.ShouldBeTrue();

        // Обложкой выбрано второе фото — после сохранения так и на сервере.
        await editor.Photos.SetCoverCommand.ExecuteAsync(editor.Photos.Photos[1]);
        var saved = await editor.TrySaveAsync();

        saved!.Photos.Select(p => p.FileName).ShouldBe(["front.png", "side.png"]);
        saved.CoverMediaId.ShouldBe(saved.Photos[1].Id);
        editor.Result!.CoverMediaId.ShouldBe(saved.Photos[1].Id);
        editor.Photos.Photos.ShouldAllBe(p => !p.IsPending);
    }

    [Fact]
    public async Task Existing_item_photos_change_cover_and_delete_immediately()
    {
        var item = await _session.Api.CreateItemAsync(new SaveItemRequest(Type("Коробка").Id, Name: "Коробка с фото"), Ct);
        var strip = new PhotoStripViewModel(_session, item.Id);
        var changes = 0;
        strip.Changed += () => changes++;

        await strip.AddFilesAsync([Png("a.png", SKColors.Green)]);
        await strip.AddFilesAsync([Png("b.png", SKColors.Yellow)]);
        strip.Photos.Select(p => p.IsCover).ShouldBe([true, false]);
        (await _session.Api.GetItemAsync(item.Id, Ct)).CoverMediaId.ShouldBe(strip.Photos[0].MediaId);
        await WaitUntilAsync(() => strip.Photos.All(p => p.Thumbnail is not null));

        await strip.SetCoverCommand.ExecuteAsync(strip.Photos[1]);
        (await _session.Api.GetItemAsync(item.Id, Ct)).CoverMediaId.ShouldBe(strip.Photos[1].MediaId);

        // Удалить обложку: обложкой становится оставшееся фото — и в ленте, и на сервере.
        await strip.RemoveCommand.ExecuteAsync(strip.Photos[1]);
        _dialogs.Questions.ShouldContain("Удалить фото?");
        strip.Photos.ShouldHaveSingleItem().IsCover.ShouldBeTrue();
        var current = await _session.Api.GetItemAsync(item.Id, Ct);
        current.Photos.ShouldHaveSingleItem().FileName.ShouldBe("a.png");
        current.CoverMediaId.ShouldBe(current.Photos[0].Id);
        changes.ShouldBe(4);
    }

    [Fact]
    public async Task Explorer_adds_photos_and_shows_cover_thumbnails()
    {
        var place = await _session.Api.CreateItemAsync(new SaveItemRequest(Type("Место").Id, Name: "Кладовка"), Ct);
        var box = await _session.Api.CreateItemAsync(new SaveItemRequest(Type("Коробка").Id, place.Id, "Коробка"), Ct);
        using var explorer = new ExplorerViewModel(_session);
        await explorer.ActivateAsync();
        await explorer.NavigateAsync(place.Id);
        explorer.Items.Single().HasThumbnail.ShouldBeFalse();

        (await explorer.AddPhotosAsync(box.Id, [Png("box.png", SKColors.Orange)])).ShouldBeTrue();

        _dialogs.Notices.ShouldContain(("1 фото добавлено — «Коробка»", NotifyKind.Success));
        await WaitUntilAsync(() => explorer.Items.Single().HasThumbnail);
        explorer.SelectedItem = explorer.Items.Single();
        await WaitUntilAsync(() => explorer.Details?.Id == box.Id);
        explorer.Details!.Photos.Photos.ShouldHaveSingleItem().IsCover.ShouldBeTrue();
        explorer.PhotoTargetId.ShouldBe(box.Id);
    }

    [Fact]
    public async Task Photo_cache_serves_from_disk_without_server()
    {
        var item = await _session.Api.CreateItemAsync(new SaveItemRequest(Type("Инструмент").Id, Name: "Ключ"), Ct);
        var media = (await _session.Api.UploadPhotosAsync(item.Id, [("key.png", new MemoryStream(PngBytes(SKColors.Purple)))], Ct)).Single();

        var thumb = await _session.Photos.GetAsync(media.Id, PhotoSize.Thumb);
        thumb.ShouldNotBeNull();
        Directory.GetFiles(_cacheDir).ShouldHaveSingleItem();

        // Новый запуск без связи с сервером: превью берётся с диска.
        var offline = new PhotoCache(() => throw new InvalidOperationException("Нет подключения"), _cacheDir);
        (await offline.GetAsync(media.Id, PhotoSize.Thumb)).ShouldBe(thumb);
        (await offline.GetAsync(media.Id, PhotoSize.Preview)).ShouldBeNull();
    }

    [Fact]
    public async Task Own_icon_and_color_override_type_and_reset()
    {
        var cableType = Type("Кабель");
        var editor = ItemEditorViewModel.ForNew(_session, null, cableType.Id);
        editor.Name = "Удлинитель";
        editor.EffectiveIcon.ShouldBe(cableType.Icon);
        editor.SetEmojiCommand.Execute("🔋");
        editor.SetColorCommand.Execute("#0078D4");
        editor.HasOwnLook.ShouldBeTrue();
        var saved = await editor.TrySaveAsync();
        saved!.Icon.ShouldBe(new IconDto(IconKind.Emoji, "🔋"));
        saved.Color.ShouldBe("#0078D4");

        // Повторное открытие: своя иконка — своя; «Как у типа» — снова иконка типа.
        var again = ItemEditorViewModel.ForExisting(_session, saved);
        again.Icon.ShouldBe(new IconDto(IconKind.Emoji, "🔋"));
        again.ResetLookCommand.Execute(null);
        again.HasOwnLook.ShouldBeFalse();
        (await again.TrySaveAsync())!.Icon.ShouldBe(cableType.Icon);

        // Иконка из фото вещи.
        var withPhoto = ItemEditorViewModel.ForExisting(_session, await _session.Api.GetItemAsync(saved.Id, Ct));
        await withPhoto.Photos.AddFilesAsync([Png("cable.png", SKColors.Black)]);
        withPhoto.UsePhotoAsIconCommand.Execute(withPhoto.Photos.Photos.Single());
        (await withPhoto.TrySaveAsync())!.Icon.ShouldBe(new IconDto(IconKind.Custom, withPhoto.Photos.Photos.Single().MediaId!.Value.ToString()));
    }

    private ItemTypeDto Type(string name) => _session.Catalog.ActiveTypes.Single(t => t.Name == name);

    private static PickedFile Png(string name, SKColor color)
    {
        var bytes = PngBytes(color);
        return new PickedFile(name, () => Task.FromResult<Stream>(new MemoryStream(bytes)));
    }

    private static byte[] PngBytes(SKColor color)
    {
        using var bitmap = new SKBitmap(64, 48);
        bitmap.Erase(color);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Условие не выполнилось вовремя");
            }

            await Task.Delay(20, Ct);
        }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public List<string> Questions { get; } = [];

        public List<(string Message, NotifyKind Kind)> Notices { get; } = [];

        public Task<DialogChoice> AskAsync(string title, string message, string primary, string? secondary = null, string close = "Отмена")
        {
            Questions.Add(title);
            return Task.FromResult(DialogChoice.Primary);
        }

        public Task<ItemDetailsDto?> EditItemAsync(ItemEditorViewModel editor) => editor.TrySaveAsync();

        public Task<MoveTarget?> PickLocationAsync(string title, ClientSession session) => Task.FromResult<MoveTarget?>(null);

        public Task<IReadOnlyList<PickedFile>> PickImagesAsync() => Task.FromResult<IReadOnlyList<PickedFile>>([]);

        public void Notify(string message, NotifyKind kind = NotifyKind.Info) => Notices.Add((message, kind));
    }
}

#:project ../src/Invy.Client.Core/Invy.Client.Core.csproj
#:property TargetFramework=net10.0
#:property TreatWarningsAsErrors=false

// Сквозная проверка фазы 2 против настоящего сервера (Kestrel, SQLite, SignalR по WebSocket).
// Запуск: dotnet run tools/e2e-phase2.cs -- http://127.0.0.1:5470/ chain <каталог с box.png и cable.png>
// chain   — завести цепочку Чердак → Серая коробка → Прозрачный пакет → Кабель с фото, как пользователь в редакторе;
// latency — второе устройство с «Проводником» на пакете: через сколько видит новую вещь;
// add     — добавить ещё один кабель (проверка живого обновления Desktop).
using System.Diagnostics;
using Invy.Client.Core.Connection;
using Invy.Client.Core.Editor;
using Invy.Client.Core.Explorer;
using Invy.Client.Core.Session;
using Invy.Contracts.Catalog;
using Invy.Contracts.Devices;
using Invy.Contracts.Items;

var server = new Uri(args[0]);
var mode = args[1];
var photos = args.Length > 2 ? args[2] : ".";

switch (mode)
{
    case "chain":
    {
        var session = await ConnectAsync("Скрипт A", realtime: false);
        var kit = await session.Api.ApplyStarterKitAsync();
        await session.Catalog.LoadAsync(session.Api);
        Console.WriteLine($"Стартовый набор: типов {kit.TypesCreated}, справочников {kit.ListsCreated}");

        var total = Stopwatch.StartNew();
        var attic = await SaveAsync(session, null, "Место", e => e.Name = "Чердак");
        var box = await SaveAsync(session, new MoveTarget(attic.Id, "Чердак"), "Коробка", e =>
        {
            e.Name = "Серая коробка";
            Field(e, "Цвет").Text = "серый";
        }, Path.Combine(photos, "box.png"));
        var bag = await SaveAsync(session, new MoveTarget(box.Id, "Чердак › Серая коробка"), "Пакет", e => e.Name = "Прозрачный пакет");
        var cable = await SaveAsync(session, new MoveTarget(bag.Id, "Чердак › Серая коробка › Прозрачный пакет"), "Кабель", e =>
        {
            Field(e, "Длина").Number = 100;
            Choose(Field(e, "Разъём 1"), "Type-A");
            Choose(Field(e, "Разъём 2"), "Type-C");
            Console.WriteLine($"  авто-название в форме: {e.AutoName}");
        }, Path.Combine(photos, "cable.png"));
        Console.WriteLine($"Цепочка заведена за {total.ElapsedMilliseconds} мс (без учёта ввода человеком)");
        Console.WriteLine($"Кабель: «{cable.Name}», путь: {string.Join(" › ", cable.Path.Select(p => p.Name))}, фото: {cable.Photos.Count}, обложка: {cable.CoverMediaId is not null}");
        Console.WriteLine($"BAG={bag.Id}");
        await session.DisposeAsync();
        break;
    }

    case "latency":
    {
        var writer = await ConnectAsync("Скрипт A", realtime: false);
        var reader = await ConnectAsync("Скрипт B", realtime: true);
        Console.WriteLine($"Канал реального времени B: {reader.Realtime.State}");
        var bagId = (await writer.Api.GetRootsAsync()).Items.Single(i => i.Name == "Чердак") is { } atticRow
            ? await FindAsync(writer, atticRow.Id, "Серая коробка", "Прозрачный пакет")
            : throw new InvalidOperationException("Нет чердака");
        using var explorer = new ExplorerViewModel(reader);
        await explorer.ActivateAsync();
        await explorer.NavigateAsync(bagId);

        var cableType = writer.Catalog.ActiveTypes.Single(t => t.Name == "Переходник");
        var samples = new List<double>();
        for (var i = 0; i < 10; i++)
        {
            var created = await writer.Api.CreateItemAsync(new SaveItemRequest(cableType.Id, bagId, $"Переходник №{i + 1}"));
            var watch = Stopwatch.StartNew();
            while (!Contains(explorer, created.Id))
            {
                if (watch.Elapsed > TimeSpan.FromSeconds(5))
                {
                    throw new TimeoutException($"Второе устройство не увидело вещь №{i + 1} за 5 с");
                }

                await Task.Delay(2);
            }

            samples.Add(watch.Elapsed.TotalMilliseconds);
            await writer.Api.DeleteItemAsync(created.Id);
            await Task.Delay(150);
        }

        samples.Sort();
        Console.WriteLine($"Задержка до второго устройства (10 изменений): медиана {samples[samples.Count / 2]:0} мс, максимум {samples[^1]:0} мс");
        await writer.DisposeAsync();
        await reader.DisposeAsync();
        break;
    }

    case "add":
    {
        var session = await ConnectAsync("Скрипт A", realtime: false);
        var bagId = Guid.Parse(args[3]);
        var cable = await SaveAsync(session, new MoveTarget(bagId, "Прозрачный пакет"), "Кабель", e =>
        {
            Field(e, "Длина").Number = 200;
            Choose(Field(e, "Разъём 1"), "HDMI");
            Choose(Field(e, "Разъём 2"), "HDMI");
        });
        Console.WriteLine($"Добавлен «{cable.Name}» в {DateTime.Now:HH:mm:ss.fff}");
        await session.DisposeAsync();
        break;
    }
}

async Task<ClientSession> ConnectAsync(string name, bool realtime)
{
    var settings = new InMemorySettingsStore();
    var connection = new ServerConnection(settings, new PlainTokenProtector(), null, name, DevicePlatform.Other, "e2e");
    if (!await connection.ConnectAsync(server))
    {
        throw new InvalidOperationException(connection.Error);
    }

    var session = new ClientSession(connection, settings, new ConsoleDialogs(), ImmediateDispatcher.Instance);
    if (realtime)
    {
        await session.StartAsync();
    }
    else
    {
        await session.Catalog.LoadAsync(session.Api);
    }

    return session;
}

static async Task<ItemDetailsDto> SaveAsync(ClientSession session, MoveTarget? location, string type, Action<ItemEditorViewModel> fill, string? photo = null)
{
    var watch = Stopwatch.StartNew();
    var editor = ItemEditorViewModel.ForNew(session, location, session.Catalog.ActiveTypes.Single(t => t.Name == type).Id);
    fill(editor);
    if (photo is not null)
    {
        await editor.Photos.AddFilesAsync([new PickedFile(Path.GetFileName(photo), () => Task.FromResult<Stream>(File.OpenRead(photo)))]);
    }

    var saved = await editor.TrySaveAsync() ?? throw new InvalidOperationException(editor.Error ?? editor.NameError ?? editor.LocationError);
    Console.WriteLine($"  + {type} «{saved.Name}» — {watch.ElapsedMilliseconds} мс{(photo is null ? string.Empty : ", с фото")}");
    return saved;
}

static FieldEditorViewModel Field(ItemEditorViewModel editor, string name) => editor.Fields.Single(f => f.Definition.Name == name);

static void Choose(FieldEditorViewModel field, string value) => field.SelectedOption = field.Options.Single(o => o.Value == value);

static async Task<Guid> FindAsync(ClientSession session, Guid parentId, params string[] names)
{
    foreach (var name in names)
    {
        parentId = (await session.Api.GetChildrenAsync(parentId)).Items.Single(i => i.Name == name).Id;
    }

    return parentId;
}

static bool Contains(ExplorerViewModel explorer, Guid id)
{
    try
    {
        return explorer.Items.Any(i => i.Id == id);
    }
    catch (InvalidOperationException)
    {
        return false;   // список обновляется в потоке SignalR
    }
}

sealed class ConsoleDialogs : IDialogService
{
    public Task<DialogChoice> AskAsync(string title, string message, string primary, string? secondary = null, string close = "Отмена") => Task.FromResult(DialogChoice.Primary);

    public Task<ItemDetailsDto?> EditItemAsync(ItemEditorViewModel editor) => editor.TrySaveAsync();

    public Task<ItemTypeDto?> EditTypeAsync(Invy.Client.Core.Catalog.TypeEditorViewModel editor) => editor.TrySaveAsync();

    public Task<MoveTarget?> PickLocationAsync(string title, ClientSession session) => Task.FromResult<MoveTarget?>(null);

    public Task<IReadOnlyList<PickedFile>> PickImagesAsync() => Task.FromResult<IReadOnlyList<PickedFile>>([]);

    public void Notify(string message, NotifyKind kind = NotifyKind.Info) => Console.WriteLine($"  [{kind}] {message}");
}

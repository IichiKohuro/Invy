using Invy.Client.Core.Connection;
using Invy.Client.Core.Editor;
using Invy.Client.Core.Explorer;
using Invy.Client.Core.Session;
using Invy.Contracts.Catalog;
using Invy.Contracts.Devices;
using Invy.Contracts.Items;

namespace Invy.Api.Tests;

/// <summary>ViewModel «Проводника» и редактора вещи против настоящего сервера в памяти.</summary>
public sealed class ClientViewModelTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly List<ClientSession> _sessions = [];
    private InvyApiFactory _server = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _server = new InvyApiFactory();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions)
        {
            await session.DisposeAsync();
        }

        await _server.DisposeAsync();
    }

    [Fact]
    public async Task Editor_builds_chain_with_auto_name_and_remembered_values()
    {
        var (session, _) = await SessionAsync(starterKit: true);

        var attic = await SaveNewAsync(session, null, "Место", e => e.Name = "Чердак");
        var box = await SaveNewAsync(session, new MoveTarget(attic.Id, "Чердак"), "Коробка", e =>
        {
            e.Name = "Серая коробка";
            Field(e, "Цвет").Text = "серый";
        });
        var bag = await SaveNewAsync(session, new MoveTarget(box.Id, "Чердак › Серая коробка"), "Пакет", e => e.Name = "Прозрачный пакет");

        var cable = ItemEditorViewModel.ForNew(session, new MoveTarget(bag.Id, "Пакет"), Type(session, "Кабель").Id);
        cable.Unit.ShouldBe("шт");
        cable.Fields.Select(f => f.TemplateKey).ShouldBe(["Integer", "Choice", "Choice", "Choice", "Text"]);
        Field(cable, "Длина").Number = 100;
        Field(cable, "Разъём 1").SelectedOption = Option(Field(cable, "Разъём 1"), "Type-A");
        Field(cable, "Разъём 2").SelectedOption = Option(Field(cable, "Разъём 2"), "Type-C");
        Field(cable, "Цвет").Text = "чёрный";
        cable.AutoName.ShouldBe("Type-A → Type-C, 100 см");
        cable.NamePlaceholder.ShouldBe("Type-A → Type-C, 100 см");

        await cable.SaveAndAddAnotherCommand.ExecuteAsync(null);

        cable.SavedMessage.ShouldBe("Добавлено: «Type-A → Type-C, 100 см». Можно заполнять следующую.");
        var saved = cable.Result!;
        saved.IsNameAuto.ShouldBeTrue();
        saved.Path.Select(p => p.Name).ShouldBe(["Чердак", "Серая коробка", "Прозрачный пакет"]);
        saved.Values.Single(v => v.Display == "100 см").ShouldNotBeNull();

        // Следующая вещь: тип и место те же, «Цвет» (запоминать последнее) подставлен, остальное пусто.
        cable.IsNew.ShouldBeTrue();
        cable.ParentId.ShouldBe(bag.Id);
        Field(cable, "Цвет").Text.ShouldBe("чёрный");
        Field(cable, "Длина").Number.ShouldBeNull();
        cable.AutoName.ShouldBeNull();
        session.Settings.Current.RecentTypeIds[0].ShouldBe(Type(session, "Кабель").Id);

        // Новый вариант справочника прямо из формы.
        var connector = Field(cable, "Разъём 1");
        connector.NewOptionText = "USB-C магнитный";
        await connector.AddOptionCommand.ExecuteAsync(null);
        connector.SelectedOption!.Value.ShouldBe("USB-C магнитный");
        (await session.Api.GetListsAsync(Ct)).Single(l => l.Name == "Разъёмы").Options.ShouldContain(o => o.Value == "USB-C магнитный");
    }

    [Fact]
    public async Task Editor_shows_errors_next_to_fields()
    {
        var (session, _) = await SessionAsync(starterKit: true);
        var shed = await SaveNewAsync(session, null, "Место", e => e.Name = "Сарай");
        var box = await SaveNewAsync(session, new MoveTarget(shed.Id, "Сарай"), "Коробка", e => e.Name = "Ящик");

        // Проверка на клиенте: без названия и без шаблона.
        var empty = ItemEditorViewModel.ForNew(session, null, Type(session, "Место").Id);
        (await empty.TrySaveAsync()).ShouldBeNull();
        empty.NameError.ShouldBe("Укажите название");
        empty.Name = "Гараж";
        empty.NameError.ShouldBeNull();

        // Ошибка сервера: место нельзя положить в коробку — сообщение у поля «Где лежит».
        var place = ItemEditorViewModel.ForNew(session, new MoveTarget(box.Id, "Ящик"), Type(session, "Место").Id);
        place.Name = "Кладовка";
        (await place.TrySaveAsync()).ShouldBeNull();
        place.LocationError.ShouldBe("Место «Кладовка» нельзя положить в контейнер «Ящик»");
        place.Error.ShouldBeNull();

        // Ошибка значения характеристики — у самой характеристики.
        var typeWithLimit = await session.Api.CreateTypeAsync(
            new SaveItemTypeRequest("Батарейка", TypeRole.Item, [new FieldDefinitionInput(null, "Ёмкость", FieldDataType.Integer, Settings: new FieldSettingsDto(Min: 0, Max: 10000))]), Ct);
        await session.Catalog.LoadAsync(session.Api, Ct);
        var battery = ItemEditorViewModel.ForNew(session, new MoveTarget(box.Id, "Ящик"), typeWithLimit.Id);
        battery.Name = "AA";
        Field(battery, "Ёмкость").Number = 20000;
        (await battery.TrySaveAsync()).ShouldBeNull();
        Field(battery, "Ёмкость").Error.ShouldNotBeNullOrEmpty();
        battery.Error.ShouldBeNull();
    }

    [Fact]
    public async Task Editor_resolves_version_conflict()
    {
        var (session, dialogs) = await SessionAsync(starterKit: true);
        var item = await SaveNewAsync(session, null, "Инструмент", e => e.Name = "Отвёртка");

        // Пока редактор открыт, вещь меняют на другом устройстве.
        var editor = ItemEditorViewModel.ForExisting(session, await session.Api.GetItemAsync(item.Id, Ct));
        await session.Api.UpdateItemAsync(item.Id, new SaveItemRequest(item.TypeId, Name: "Отвёртка PH2", Version: item.Version), Ct);

        // «Загрузить новую» — в форме значения с другого устройства, сохранения нет.
        editor.Description = "крестовая";
        dialogs.Answers.Enqueue(DialogChoice.Secondary);
        (await editor.TrySaveAsync()).ShouldBeNull();
        dialogs.Questions.ShouldContain("Вещь изменили на другом устройстве");
        editor.Name.ShouldBe("Отвёртка PH2");
        editor.Description.ShouldBeNull();

        // Ещё одно изменение на другом устройстве, затем «Сохранить мои» — поверх.
        var current = await session.Api.GetItemAsync(item.Id, Ct);
        await session.Api.UpdateItemAsync(item.Id, new SaveItemRequest(item.TypeId, Name: "Отвёртка PH1", Version: current.Version), Ct);
        editor.Description = "крестовая";
        dialogs.Answers.Enqueue(DialogChoice.Primary);
        var saved = await editor.TrySaveAsync();
        saved!.Name.ShouldBe("Отвёртка PH2");
        saved.Description.ShouldBe("крестовая");
    }

    [Fact]
    public async Task Explorer_navigates_and_keeps_tree_in_sync()
    {
        var (session, _) = await SessionAsync(starterKit: true);
        var home = await SaveNewAsync(session, null, "Место", e => e.Name = "Дом");
        var attic = await SaveNewAsync(session, new MoveTarget(home.Id, "Дом"), "Место", e => e.Name = "Чердак");
        var box = await SaveNewAsync(session, new MoveTarget(attic.Id, "Чердак"), "Коробка", e => e.Name = "Серая коробка");
        await SaveNewAsync(session, new MoveTarget(box.Id, "Серая коробка"), "Инструмент", e =>
        {
            e.Name = "Рулетка";
            e.Quantity = 2;
        });

        using var explorer = new ExplorerViewModel(session);
        await explorer.ActivateAsync();
        explorer.Items.Select(i => i.Name).ShouldBe(["Дом"]);
        explorer.Tree.Select(n => n.Name).ShouldBe(["Дом"]);
        explorer.Tree[0].Children.ShouldHaveSingleItem().IsPlaceholder.ShouldBeTrue();
        explorer.AddText.ShouldBe("Добавить место");

        // Открыть коробку по ссылке: крошки, содержимое и раскрытая ветка дерева.
        await explorer.NavigateAsync(box.Id);
        explorer.Breadcrumbs.Select(b => b.Name).ShouldBe(["Места", "Дом", "Чердак", "Серая коробка"]);
        explorer.CurrentPathText.ShouldBe("Дом › Чердак › Серая коробка");
        var tape = explorer.Items.ShouldHaveSingleItem();
        tape.Subtitle.ShouldBe("Инструмент · 2 шт");
        explorer.Details!.Name.ShouldBe("Серая коробка");
        explorer.Details.ContentsText.ShouldBe("Внутри 1 вещь");
        explorer.SelectedNode!.PathText.ShouldBe("Дом › Чердак › Серая коробка");
        explorer.Tree[0].IsExpanded.ShouldBeTrue();

        // Выбор вещи — её карточка; «Наверх» — на уровень выше.
        explorer.SelectedItem = tape;
        await WaitUntilAsync(() => explorer.Details?.Name == "Рулетка");
        explorer.Details!.PathText.ShouldBe("Дом › Чердак › Серая коробка");
        await explorer.UpCommand.ExecuteAsync(null);
        explorer.CurrentId.ShouldBe(attic.Id);
        explorer.Items.Single().Subtitle.ShouldBe("Коробка · 1 вещь");

        // Выбор узла в дереве открывает место.
        explorer.SelectedNode = explorer.Tree[0];
        await WaitUntilAsync(() => explorer.CurrentId == home.Id);
        explorer.Items.Select(i => i.Name).ShouldBe(["Чердак"]);
    }

    [Fact]
    public async Task Explorer_cuts_pastes_moves_duplicates_and_deletes()
    {
        var (session, dialogs) = await SessionAsync(starterKit: true);
        var attic = await SaveNewAsync(session, null, "Место", e => e.Name = "Чердак");
        var shed = await SaveNewAsync(session, null, "Место", e => e.Name = "Сарай");
        var box = await SaveNewAsync(session, new MoveTarget(attic.Id, "Чердак"), "Коробка", e => e.Name = "Коробка с кабелями");
        await SaveNewAsync(session, new MoveTarget(box.Id, "Коробка"), "Инструмент", e => e.Name = "Паяльник");

        using var explorer = new ExplorerViewModel(session);
        await explorer.ActivateAsync();
        await explorer.NavigateAsync(attic.Id);

        // Вырезать → открыть «Сарай» → вставить.
        explorer.CutCommand.Execute(explorer.Items.Single());
        explorer.Items.Single().IsCut.ShouldBeTrue();
        explorer.CutText.ShouldBe("Вырезано: «Коробка с кабелями»");
        await explorer.NavigateAsync(shed.Id);
        await explorer.PasteCommand.ExecuteAsync(null);
        explorer.HasCut.ShouldBeFalse();
        explorer.Items.Single().Name.ShouldBe("Коробка с кабелями");
        dialogs.Notices.ShouldContain(("«Коробка с кабелями» → Сарай", NotifyKind.Success));

        // «Переместить в…» обратно на чердак.
        dialogs.Location = new MoveTarget(attic.Id, "Чердак");
        await explorer.MoveToCommand.ExecuteAsync(explorer.Items.Single());
        explorer.Items.ShouldBeEmpty();
        explorer.IsEmpty.ShouldBeTrue();
        explorer.EmptyTitle.ShouldBe("Здесь пусто");

        // Копия и удаление: контейнер с содержимым — выбор «переложить выше».
        await explorer.NavigateAsync(attic.Id);
        await explorer.DuplicateCommand.ExecuteAsync(explorer.Items.Single());
        explorer.Items.Count.ShouldBe(2);
        explorer.SelectedItem!.Id.ShouldNotBe(box.Id);

        var original = explorer.Items.Single(i => i.Id == box.Id);
        dialogs.Answers.Enqueue(DialogChoice.Secondary);
        await explorer.DeleteCommand.ExecuteAsync(original);
        dialogs.Questions[^1].ShouldBe("Удалить «Коробка с кабелями»?");
        explorer.Items.Select(i => i.Name).ShouldBe(["Коробка с кабелями (копия)", "Паяльник"]);
        (await session.Api.GetTrashAsync(ct: Ct)).Items.ShouldContain(t => t.Id == box.Id);

        // Перетаскивание нескольких вещей в контейнер.
        var copy = explorer.Items.Single(i => i.IsContainer);
        var tool = explorer.Items.Single(i => !i.IsContainer);
        (await explorer.MoveAsync([tool.Id], copy.Id, copy.Name)).ShouldBeTrue();
        explorer.Items.ShouldHaveSingleItem().Item.ChildCount.ShouldBe(1);
    }

    [Fact]
    public async Task Explorer_refreshes_on_changes_from_another_device()
    {
        var (session, _) = await SessionAsync(starterKit: true, realtime: true);
        using var explorer = new ExplorerViewModel(session);
        await explorer.ActivateAsync();

        // События приходят из потока SignalR: ждём, пока своё изменение дойдёт, и только потом переходим.
        var attic = await SaveNewAsync(session, null, "Место", e => e.Name = "Чердак");
        await WaitUntilAsync(() => explorer.Tree.Any(n => n.Id == attic.Id) && explorer.Items.Any(i => i.Id == attic.Id));
        await explorer.NavigateAsync(attic.Id);

        // Второй клиент кладёт коробку на чердак и создаёт новое место.
        var (other, _) = await SessionAsync(starterKit: false);
        var box = await SaveNewAsync(other, new MoveTarget(attic.Id, "Чердак"), "Коробка", e => e.Name = "Новая коробка");
        await WaitUntilAsync(() => explorer.Items.Any(i => i.Id == box.Id));
        await WaitUntilAsync(() => explorer.Tree.Single(n => n.Id == attic.Id).HasChildren);

        await SaveNewAsync(other, null, "Место", e => e.Name = "Балкон");
        await WaitUntilAsync(() => explorer.Tree.Any(n => n.Name == "Балкон"));
        explorer.Tree.Select(n => n.Name).ShouldBe(["Балкон", "Чердак"]);

        // Переименование текущего места — крошки обновляются.
        var current = await other.Api.GetItemAsync(attic.Id, Ct);
        await other.Api.UpdateItemAsync(attic.Id, new SaveItemRequest(current.TypeId, Name: "Мансарда", Version: current.Version), Ct);
        await WaitUntilAsync(() => explorer.Breadcrumbs[^1].Name == "Мансарда");
        await WaitUntilAsync(() => explorer.Tree.Any(n => n.Name == "Мансарда"));
    }

    private async Task<(ClientSession Session, FakeDialogs Dialogs)> SessionAsync(bool starterKit, bool realtime = false)
    {
        var settings = new InMemorySettingsStore();
        var connection = new ServerConnection(settings, new PlainTokenProtector(), () => _server.Server.CreateHandler(), "Тест", DevicePlatform.Windows, "0.2.0");
        (await connection.ConnectAsync(_server.Server.BaseAddress, Ct)).ShouldBeTrue();
        if (starterKit)
        {
            await connection.Api!.ApplyStarterKitAsync(Ct);
        }

        var dialogs = new FakeDialogs();
        var session = new ClientSession(connection, settings, dialogs, ImmediateDispatcher.Instance, new RealtimeService(ImmediateDispatcher.Instance) { LongPollingOnly = true });
        _sessions.Add(session);
        if (realtime)
        {
            await session.StartAsync(Ct);
        }
        else
        {
            await session.Catalog.LoadAsync(session.Api, Ct);
        }

        return (session, dialogs);
    }

    private static async Task<ItemDetailsDto> SaveNewAsync(ClientSession session, MoveTarget? location, string typeName, Action<ItemEditorViewModel> fill)
    {
        var editor = ItemEditorViewModel.ForNew(session, location, Type(session, typeName).Id);
        fill(editor);
        var saved = await editor.TrySaveAsync();
        saved.ShouldNotBeNull(editor.Error ?? editor.NameError ?? editor.LocationError);
        return saved;
    }

    private static ItemTypeDto Type(ClientSession session, string name) => session.Catalog.ActiveTypes.Single(t => t.Name == name);

    private static FieldEditorViewModel Field(ItemEditorViewModel editor, string name) => editor.Fields.Single(f => f.Definition.Name == name);

    private static ValueListOptionDto Option(FieldEditorViewModel field, string value) => field.Options.Single(o => o.Value == value);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!Check(condition))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Условие не выполнилось вовремя");
            }

            await Task.Delay(20, Ct);
        }
    }

    /// <summary>Коллекции меняются из потока SignalR — читаем их, пока не получится.</summary>
    private static bool Check(Func<bool> condition)
    {
        try
        {
            return condition();
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public Queue<DialogChoice> Answers { get; } = new();

        public List<string> Questions { get; } = [];

        public List<(string Message, NotifyKind Kind)> Notices { get; } = [];

        public MoveTarget? Location { get; set; }

        public Task<DialogChoice> AskAsync(string title, string message, string primary, string? secondary = null, string close = "Отмена")
        {
            Questions.Add(title);
            return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : DialogChoice.Primary);
        }

        public Task<ItemDetailsDto?> EditItemAsync(ItemEditorViewModel editor) => editor.TrySaveAsync();

        public Task<ItemTypeDto?> EditTypeAsync(Invy.Client.Core.Catalog.TypeEditorViewModel editor) => editor.TrySaveAsync();

        public Task<MoveTarget?> PickLocationAsync(string title, ClientSession session) => Task.FromResult(Location);

        public Task<IReadOnlyList<PickedFile>> PickImagesAsync() => Task.FromResult<IReadOnlyList<PickedFile>>([]);

        public void Notify(string message, NotifyKind kind = NotifyKind.Info)
        {
            lock (Notices)
            {
                Notices.Add((message, kind));
            }
        }
    }
}

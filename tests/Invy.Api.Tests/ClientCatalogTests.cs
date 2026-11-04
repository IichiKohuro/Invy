using Invy.Client.Core.Catalog;
using Invy.Client.Core.Connection;
using Invy.Client.Core.Editor;
using Invy.Client.Core.Session;
using Invy.Contracts.Catalog;
using Invy.Contracts.Devices;
using Invy.Contracts.Items;

namespace Invy.Api.Tests;

/// <summary>Подключение с кодом, конструктор типов, справочники и теги — ViewModel против сервера в памяти.</summary>
public sealed class ClientCatalogTests : IAsyncLifetime
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
    public async Task Connect_screen_validates_address_and_starts_session()
    {
        var settings = new InMemorySettingsStore();
        var connection = new ServerConnection(settings, new PlainTokenProtector(), () => _server.Server.CreateHandler(), "Тест", DevicePlatform.Windows, "0.2.0");
        ServerConnection? connected = null;
        var connect = new ConnectViewModel(connection, "  ", c =>
        {
            connected = c;
            return Task.CompletedTask;
        });

        await connect.ConnectCommand.ExecuteAsync(null);
        connect.Error.ShouldBe("Укажите адрес сервера, например 192.168.1.10 или home-pc:5470");
        connected.ShouldBeNull();

        connect.Address = _server.Server.BaseAddress.ToString();
        await connect.ConnectCommand.ExecuteAsync(null);
        connect.Error.ShouldBeNull();
        connected.ShouldBeSameAs(connection);
        connect.StatusText.ShouldStartWith("Подключено к «");
        settings.Current.ServerUrl.ShouldBe("http://localhost:5470/");   // без порта — порт Invy по умолчанию
    }

    [Fact]
    public async Task New_device_waits_with_code_until_admin_approves_it()
    {
        await using var pairing = new PairingApiFactory();

        // Администратор — первое устройство на том же компьютере, что и сервер.
        var adminSettings = new InMemorySettingsStore();
        var adminConnection = new ServerConnection(adminSettings, new PlainTokenProtector(), () => new LoopbackHandler(pairing.Server.CreateHandler()), "Сервер", DevicePlatform.Windows);
        (await adminConnection.ConnectAsync(pairing.Server.BaseAddress, Ct)).ShouldBeTrue();
        adminConnection.Me!.Role.ShouldBe(DeviceRole.Admin);
        var adminDialogs = new FakeDialogs();
        var admin = new ClientSession(adminConnection, adminSettings, adminDialogs, ImmediateDispatcher.Instance, new RealtimeService(ImmediateDispatcher.Instance) { LongPollingOnly = true });
        _sessions.Add(admin);
        await admin.StartAsync(Ct);
        PendingPairingDto? requested = null;
        admin.Realtime.PairingRequested += r => requested = r;

        // Ноутбук в той же сети: экран подключения показывает код и ждёт.
        var laptop = new ServerConnection(new InMemorySettingsStore(), new PlainTokenProtector(), () => pairing.Server.CreateHandler(), "Ноутбук", DevicePlatform.Windows)
        {
            PairingPollInterval = TimeSpan.FromMilliseconds(50),
        };
        var started = false;
        var connect = new ConnectViewModel(laptop, pairing.Server.BaseAddress.ToString(), _ =>
        {
            started = true;
            return Task.CompletedTask;
        });
        var connecting = connect.ConnectCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => connect.IsWaitingForApproval && requested is not null);
        connect.PairingCode.ShouldBe(requested!.Code);
        connect.IsBusy.ShouldBeTrue();

        // Администратор видит запрос с тем же кодом и разрешает изменять.
        adminDialogs.Answers.Enqueue(DialogChoice.Primary);
        await PairingPrompt.AskAsync(admin, requested);
        adminDialogs.Questions.ShouldContain("Новое устройство просит доступ");

        await connecting.WaitAsync(Timeout, Ct);
        started.ShouldBeTrue();
        laptop.Me!.Role.ShouldBe(DeviceRole.Editor);
        adminDialogs.Notices.ShouldContain(("«Ноутбук» подключено", NotifyKind.Success));
    }

    [Fact]
    public async Task Type_constructor_builds_fields_with_new_list_and_live_preview()
    {
        var (session, _) = await SessionAsync();
        var editor = TypeEditorViewModel.ForNew(session, TypeRole.Item);
        editor.PreviewDelay = TimeSpan.Zero;
        editor.Name = "Батарейка";
        editor.DefaultUnit = "шт";
        editor.SetEmojiCommand.Execute("🔋");

        editor.AddFieldCommand.Execute(null);
        var size = editor.Fields[^1];
        size.Name = "Типоразмер";
        size.DataType = FieldDraftViewModel.DataTypes.Single(d => d.Type == FieldDataType.Choice);
        size.Summary.ShouldBe("Выбор из списка — справочник не выбран");
        size.NewListOptions = "AA, AAA, aa, CR2032";
        await editor.CreateListCommand.ExecuteAsync(size);
        size.List!.Name.ShouldBe("Типоразмер");
        size.List.Options.Select(o => o.Value).ShouldBe(["AA", "AAA", "CR2032"]);

        editor.AddFieldCommand.Execute(null);
        var capacity = editor.Fields[^1];
        capacity.Name = "Ёмкость";
        capacity.DataType = Kind(FieldDataType.Integer);
        capacity.Unit = "мАч";
        capacity.Min = 0;

        editor.AddFieldCommand.Execute(null);
        editor.Fields[^1].Name = "Перезаряжаемая";
        editor.Fields[^1].DataType = Kind(FieldDataType.Boolean);
        editor.MoveFieldUpCommand.Execute(editor.Fields[^1]);
        editor.Fields.Select(f => f.Name).ShouldBe(["Типоразмер", "Перезаряжаемая", "Ёмкость"]);

        // Шаблон: неизвестное поле подсвечивается сразу.
        editor.TitleTemplate = "{Типоразмер} {Напряжение}";
        editor.TitleTemplateError.ShouldBe("Нет характеристики «Напряжение»");
        editor.TitleTemplate = null;
        editor.InsertIntoTemplateCommand.Execute(size);
        editor.InsertIntoTemplateCommand.Execute(capacity);
        editor.TitleTemplate.ShouldBe("{Типоразмер} {Ёмкость}");
        editor.TitleTemplateError.ShouldBeNull();

        // Предпросмотр — настоящая форма вещи: поля по типам данных и авто-название.
        var preview = editor.Preview!;
        preview.Fields.Select(f => f.TemplateKey).ShouldBe(["Choice", "Boolean", "Integer"]);
        preview.Fields[0].SelectedOption = preview.Fields[0].Options.Single(o => o.Value == "AA");
        preview.Fields[2].Number = 2500;
        preview.AutoName.ShouldBe("AA 2500");
        editor.DefaultUnit = "уп.";
        editor.Preview.ShouldBeSameAs(preview);
        preview.Fields[2].Number.ShouldBe(2500);   // значения в предпросмотре не теряются при правке типа
        (await preview.TrySaveAsync()).ShouldBeNull();

        var saved = await editor.TrySaveAsync();
        saved!.Fields.Select(f => (f.Name, f.DataType)).ShouldBe([("Типоразмер", FieldDataType.Choice), ("Перезаряжаемая", FieldDataType.Boolean), ("Ёмкость", FieldDataType.Integer)]);
        saved.Icon.ShouldBe(new IconDto(IconKind.Emoji, "🔋"));
        session.Catalog.Type(saved.Id).ShouldNotBeNull();

        // Правка: переименовать, убрать поле — на сервере оно уходит в архив.
        var edit = TypeEditorViewModel.ForExisting(session, saved);
        edit.PreviewDelay = TimeSpan.Zero;
        edit.Fields.Single(f => f.Name == "Ёмкость").Name = "Ёмкость, мАч";
        edit.RemoveFieldCommand.Execute(edit.Fields.Single(f => f.Name == "Перезаряжаемая"));
        edit.TitleTemplate = "{Типоразмер} {Ёмкость, мАч}";
        var updated = await edit.TrySaveAsync();
        updated!.Fields.Where(f => !f.IsArchived).Select(f => f.Name).ShouldBe(["Типоразмер", "Ёмкость, мАч"]);
        updated.Fields.Single(f => f.Name == "Перезаряжаемая").IsArchived.ShouldBeTrue();
    }

    [Fact]
    public async Task Type_constructor_shows_errors_next_to_fields()
    {
        var (session, _) = await SessionAsync();
        var editor = TypeEditorViewModel.ForNew(session, TypeRole.Container);
        editor.PreviewDelay = TimeSpan.Zero;

        editor.AddFieldCommand.Execute(null);
        editor.AddFieldCommand.Execute(null);
        editor.Fields[0].Name = "Цвет";
        editor.Fields[1].Name = "цвет";
        editor.AddFieldCommand.Execute(null);
        editor.Fields[2].Name = "Материал";
        editor.Fields[2].DataType = Kind(FieldDataType.Choice);
        (await editor.TrySaveAsync()).ShouldBeNull();
        editor.NameError.ShouldBe("Укажите название типа");
        editor.Fields[1].Error.ShouldBe("Характеристика «цвет» повторяется");
        editor.Fields[2].Error.ShouldBe("Выберите справочник или создайте новый");

        // Ошибка сервера — у своей характеристики по номеру.
        editor.Name = "Ящик";
        editor.Fields[1].Name = "Вес";
        editor.Fields[1].DataType = Kind(FieldDataType.Decimal);
        editor.Fields[1].Min = 10;
        editor.Fields[1].Max = 1;
        editor.RemoveFieldCommand.Execute(editor.Fields[2]);
        (await editor.TrySaveAsync()).ShouldBeNull();
        editor.Fields[1].Error.ShouldBe("«Вес»: минимум больше максимума");
        editor.Fields[0].Error.ShouldBeNull();

        // Такой тип уже есть.
        editor.Fields[1].Max = null;
        editor.Name = "Коробка";
        (await editor.TrySaveAsync()).ShouldBeNull();
        editor.NameError.ShouldBe("Тип «Коробка» уже есть");
    }

    [Fact]
    public async Task Types_page_groups_archives_and_deletes()
    {
        var (session, dialogs) = await SessionAsync(starterKit: false);
        using var page = new TypesViewModel(session);
        page.IsEmpty.ShouldBeTrue();
        await page.ApplyStarterKitCommand.ExecuteAsync(null);
        dialogs.Notices.ShouldContain(("Добавлено: 10 типов, 5 справочников", NotifyKind.Success));
        page.Groups.Select(g => g.Title).ShouldBe(["Места", "Контейнеры", "Вещи"]);
        var cable = page.Groups[2].Types.Single(t => t.Name == "Кабель");
        cable.Subtitle.ShouldBe("5 характеристик: Длина, Разъём 1, Разъём 2, Версия USB…");

        // Тип с вещами удалить нельзя — только в архив.
        await session.Api.CreateItemAsync(new SaveItemRequest(cable.Type.Id, Name: "USB"), Ct);
        await page.DeleteCommand.ExecuteAsync(cable);
        dialogs.Notices[^1].Kind.ShouldBe(NotifyKind.Error);
        dialogs.Notices[^1].Message.ShouldContain("используется");

        await page.ToggleArchiveCommand.ExecuteAsync(cable);
        page.Groups[2].Types.ShouldNotContain(t => t.Name == "Кабель");
        page.ShowArchived = true;
        var archived = page.Groups[2].Types.Single(t => t.Name == "Кабель");
        archived.IsArchived.ShouldBeTrue();
        archived.Subtitle.ShouldStartWith("В архиве · ");

        // Неиспользуемый тип удаляется.
        var tool = page.Groups[2].Types.Single(t => t.Name == "Расходники");
        await page.DeleteCommand.ExecuteAsync(tool);
        page.Groups[2].Types.ShouldNotContain(t => t.Name == "Расходники");

        // Новый тип через диалог конструктора.
        dialogs.OnEditType = editor =>
        {
            editor.Name = "Книга";
            editor.AddFieldCommand.Execute(null);
            editor.Fields[0].Name = "Автор";
        };
        await page.NewTypeCommand.ExecuteAsync(TypeRole.Item);
        page.Selected!.Name.ShouldBe("Книга");
    }

    [Fact]
    public async Task Value_lists_are_edited_reordered_and_archived()
    {
        var (session, _) = await SessionAsync();
        using var lists = new ValueListsViewModel(session);
        lists.NewListCommand.Execute(null);
        var editor = lists.Editor!;
        editor.Name = "Цвета";
        editor.NewOptionText = "Красный, Синий; Зелёный";
        editor.AddOptionCommand.Execute(null);
        editor.MoveOptionUpCommand.Execute(editor.Options[2]);
        editor.Options.Select(o => o.Value).ShouldBe(["Красный", "Зелёный", "Синий"]);
        editor.IsDirty.ShouldBeTrue();
        var saved = await editor.TrySaveAsync();
        saved!.Options.Select(o => o.Value).ShouldBe(["Красный", "Зелёный", "Синий"]);
        lists.Selected!.Id.ShouldBe(saved.Id);
        editor.IsDirty.ShouldBeFalse();
        editor.UsageText.ShouldBe("Пока не используется");

        // Сохранённый вариант не удаляется, а уходит в архив.
        editor.RemoveOptionCommand.Execute(editor.Options[0]);
        editor.Options[0].IsArchived.ShouldBeTrue();
        editor.Options.Count.ShouldBe(3);
        (await editor.TrySaveAsync())!.Options.Single(o => o.Value == "Красный").IsArchived.ShouldBeTrue();

        // «Разъёмы» используются кабелем — это видно.
        lists.Selected = lists.Lists.Single(l => l.Name == "Разъёмы");
        lists.Editor!.UsageText.ShouldBe("Используется: Кабель (Разъём 1, Разъём 2), Переходник (Вход, Выход), Зарядное устройство (Разъём)");

        // Удалить можно только неиспользуемый.
        lists.Selected = lists.Lists.Single(l => l.Name == "Цвета");
        await lists.DeleteCommand.ExecuteAsync(null);
        lists.Lists.ShouldNotContain(l => l.Name == "Цвета");
    }

    [Fact]
    public async Task Tags_are_created_renamed_colored_merged_and_deleted()
    {
        var (session, dialogs) = await SessionAsync();
        using var page = new TagsViewModel(session);
        page.NewTagName = "#зарядка";
        await page.CreateCommand.ExecuteAsync(null);
        page.NewTagName = "кабели";
        await page.CreateCommand.ExecuteAsync(null);
        page.NewTagName = "Кабели";
        await page.CreateCommand.ExecuteAsync(null);
        page.Error.ShouldBe("Тег #Кабели уже есть");
        page.Tags.Select(t => t.Name).ShouldBe(["зарядка", "кабели"]);

        var charging = page.Tags[0];
        page.BeginRenameCommand.Execute(charging);
        charging.EditName = "Зарядки";
        await page.RenameCommand.ExecuteAsync(charging);
        page.Tags.Select(t => t.Name).ShouldContain("Зарядки");

        page.ColorTarget = page.Tags.Single(t => t.Name == "кабели");
        await page.SetColorCommand.ExecuteAsync("#0078D4");
        page.Tags.Single(t => t.Name == "кабели").Color.ShouldBe("#0078D4");

        // Объединение: вещи получают целевой тег.
        var type = session.Catalog.ActiveTypes.Single(t => t.Name == "Кабель");
        var tagged = await session.Api.CreateItemAsync(new SaveItemRequest(type.Id, Name: "Lightning", TagIds: [page.Tags.Single(t => t.Name == "Зарядки").Tag.Id]), Ct);
        await session.Catalog.LoadAsync(session.Api, Ct);
        var source = page.Tags.Single(t => t.Name == "Зарядки");
        source.CountText.ShouldBe("1 вещь");
        await page.MergeCommand.ExecuteAsync(source.MergeOptions.Single());
        page.Tags.Select(t => t.Name).ShouldBe(["кабели"]);
        (await session.Api.GetItemAsync(tagged.Id, Ct)).TagIds.ShouldBe([page.Tags[0].Tag.Id]);

        await page.DeleteCommand.ExecuteAsync(page.Tags[0]);
        page.IsEmpty.ShouldBeTrue();
        dialogs.Questions.ShouldContain("Удалить тег #кабели?");
    }

    private static DataTypeOption Kind(FieldDataType type) => FieldDraftViewModel.DataTypes.Single(d => d.Type == type);

    private async Task<(ClientSession Session, FakeDialogs Dialogs)> SessionAsync(bool starterKit = true)
    {
        var settings = new InMemorySettingsStore();
        var connection = new ServerConnection(settings, new PlainTokenProtector(), () => _server.Server.CreateHandler(), "Тест", DevicePlatform.Windows, "0.2.0");
        (await connection.ConnectAsync(_server.Server.BaseAddress, Ct)).ShouldBeTrue();
        if (starterKit)
        {
            await connection.Api!.ApplyStarterKitAsync(Ct);
        }

        var dialogs = new FakeDialogs();
        var session = new ClientSession(connection, settings, dialogs, ImmediateDispatcher.Instance);
        _sessions.Add(session);
        await session.Catalog.LoadAsync(session.Api, Ct);
        return (session, dialogs);
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

    /// <summary>Запросы «с этого же компьютера» (как сервер видит клиента на том же ПК).</summary>
    private sealed class LoopbackHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.TryAddWithoutValidation("X-Test-Loopback", "1");
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public Queue<DialogChoice> Answers { get; } = new();

        public List<string> Questions { get; } = [];

        public List<(string Message, NotifyKind Kind)> Notices { get; } = [];

        public Action<TypeEditorViewModel>? OnEditType { get; set; }

        public Task<DialogChoice> AskAsync(string title, string message, string primary, string? secondary = null, string close = "Отмена")
        {
            Questions.Add(title);
            return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : DialogChoice.Primary);
        }

        public Task<ItemDetailsDto?> EditItemAsync(ItemEditorViewModel editor) => editor.TrySaveAsync();

        public Task<ItemTypeDto?> EditTypeAsync(TypeEditorViewModel editor)
        {
            OnEditType?.Invoke(editor);
            return editor.TrySaveAsync();
        }

        public Task<MoveTarget?> PickLocationAsync(string title, ClientSession session) => Task.FromResult<MoveTarget?>(null);

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

using System.Net;
using System.Net.Http.Json;
using Invy.Contracts.Catalog;
using Invy.Contracts.Items;
using Invy.Contracts.Sync;

namespace Invy.Api.Tests;

/// <summary>Сценарии фазы 1 через HTTP: справочники, типы, вещи, дерево, корзина, история, синхронизация.</summary>
public sealed class InventoryApiTests : IClassFixture<InvyApiFactory>
{
    private readonly HttpClient _http;

    public InventoryApiTests(InvyApiFactory factory) => _http = factory.CreateClient();

    [Fact]
    public async Task Readme_example_cable_in_bag_in_box_on_attic()
    {
        var world = await World.CreateAsync(_http);

        var cable = await _http.PostJsonAsync<SaveItemRequest, ItemDetailsDto>("/api/v1/items", new SaveItemRequest(
            world.Cable.Id,
            ParentId: world.Bag.Id,
            Values:
            [
                new FieldValueDto(world.Length.Id, Number: 100),
                new FieldValueDto(world.Connector1.Id, OptionIds: [world.TypeA.Id]),
                new FieldValueDto(world.Connector2.Id, OptionIds: [world.TypeC.Id]),
                new FieldValueDto(world.Color.Id, Text: "чёрный"),
            ]));

        cable.Name.ShouldBe("Type-A → Type-C, 100 см");
        cable.IsNameAuto.ShouldBeTrue();
        cable.Number.ShouldBeGreaterThan(0);
        cable.Path.Select(p => p.Name).ShouldBe([world.Attic.Name, world.GreyBox.Name, world.Bag.Name]);
        cable.Values.Single(v => v.FieldId == world.Length.Id).Display.ShouldBe("100 см");
        cable.Values.Single(v => v.FieldId == world.Connector2.Id).Display.ShouldBe("Type-C");

        var bag = await _http.GetJsonAsync<ItemDetailsDto>($"/api/v1/items/{world.Bag.Id}");
        bag.Counts.ShouldBe(new ItemCountsDto(1, 1));
        var atticContents = await _http.GetJsonAsync<PagedResult<ItemSummaryDto>>($"/api/v1/items/{world.Attic.Id}/children");
        atticContents.Items.ShouldHaveSingleItem().Name.ShouldBe(world.GreyBox.Name);
        (await _http.GetJsonAsync<IReadOnlyList<TreeNodeDto>>($"/api/v1/tree?parentId={world.GreyBox.Id}")).ShouldHaveSingleItem().Name.ShouldBe(world.Bag.Name);
        (await _http.GetJsonAsync<IReadOnlyList<string>>($"/api/v1/fields/{world.Color.Id}/suggest?q=ЧЕР")).ShouldContain("чёрный");
    }

    [Fact]
    public async Task Tree_rules_cycles_and_values_are_validated()
    {
        var world = await World.CreateAsync(_http);
        var cable = await world.CreateCableAsync(_http, world.Bag.Id, 50);

        var (status, problem) = await _http.SendExpectingErrorAsync(HttpMethod.Post, "/api/v1/items", new SaveItemRequest(world.Cable.Id, ParentId: cable.Id, Name: "Внутрь кабеля"));
        status.ShouldBe(422);
        problem.GetProperty("errors").GetProperty("parentId")[0].GetString()!.ShouldContain("не контейнер");

        (status, problem) = await _http.SendExpectingErrorAsync(HttpMethod.Post, "/api/v1/items/move", new MoveItemsRequest([world.Attic.Id], world.Bag.Id));
        status.ShouldBe(422);
        problem.GetRawText().ShouldContain("Место");

        (status, problem) = await _http.SendExpectingErrorAsync(HttpMethod.Post, "/api/v1/items/move", new MoveItemsRequest([world.GreyBox.Id], world.Bag.Id));
        status.ShouldBe(422);
        problem.GetRawText().ShouldContain("внутрь неё самой");

        (status, problem) = await _http.SendExpectingErrorAsync(HttpMethod.Post, "/api/v1/items", new SaveItemRequest(
            world.Cable.Id, Values: [new FieldValueDto(world.Length.Id, Number: 1.5), new FieldValueDto(world.Connector1.Id, OptionIds: [Guid.NewGuid()])]));
        status.ShouldBe(422);
        var errors = problem.GetProperty("errors").GetRawText();
        errors.ShouldContain("ожидается целое число");
        errors.ShouldContain("вариант не из справочника");
    }

    [Fact]
    public async Task Stale_version_is_rejected_with_current_state()
    {
        var world = await World.CreateAsync(_http);
        var cable = await world.CreateCableAsync(_http, world.Bag.Id, 50);
        var request = new SaveItemRequest(world.Cable.Id, ParentId: world.Bag.Id, Name: "Новое имя", Values: [new FieldValueDto(world.Length.Id, Number: 50)], Version: cable.Version);

        var updated = await _http.PutJsonAsync<SaveItemRequest, ItemDetailsDto>($"/api/v1/items/{cable.Id}", request);
        updated.Version.ShouldBe(cable.Version + 1);
        updated.IsNameAuto.ShouldBeFalse();

        var (status, problem) = await _http.SendExpectingErrorAsync(HttpMethod.Put, $"/api/v1/items/{cable.Id}", request with { Name = "Ещё раз" });
        status.ShouldBe(409);
        problem.GetProperty("current").GetProperty("name").GetString().ShouldBe("Новое имя");
    }

    [Fact]
    public async Task Move_is_recorded_in_item_history()
    {
        var world = await World.CreateAsync(_http);
        var cable = await world.CreateCableAsync(_http, world.Bag.Id, 50);

        using var response = await _http.PostAsJsonAsync("/api/v1/items/move", new MoveItemsRequest([cable.Id], world.Attic.Id), HttpJson.Info<MoveItemsRequest>(), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var history = await _http.GetJsonAsync<PagedResult<AuditEntryDto>>($"/api/v1/items/{cable.Id}/history");
        history.Items[0].Action.ShouldBe(ChangeAction.Moved);
        history.Items[0].Summary.ShouldBe($"Перемещено: {world.Bag.Name} → {world.Attic.Name}");
        history.Items[^1].Action.ShouldBe(ChangeAction.Created);
    }

    [Fact]
    public async Task Container_goes_to_trash_with_contents_and_comes_back()
    {
        var world = await World.CreateAsync(_http);
        var cable = await world.CreateCableAsync(_http, world.Bag.Id, 50);

        var (status, _) = await _http.SendExpectingErrorAsync(HttpMethod.Delete, $"/api/v1/items/{world.GreyBox.Id}");
        status.ShouldBe(422);   // внутри есть вещи — нужно выбрать, что с ними делать

        using (var deleted = await _http.DeleteAsync($"/api/v1/items/{world.GreyBox.Id}?contents=Delete", TestContext.Current.CancellationToken))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await _http.GetJsonAsync<ItemDetailsDto>($"/api/v1/items/{cable.Id}")).IsDeleted.ShouldBeTrue();
        (await _http.GetJsonAsync<PagedResult<ItemSummaryDto>>($"/api/v1/items/{world.Attic.Id}/children")).Total.ShouldBe(0);
        var trash = await _http.GetJsonAsync<PagedResult<TrashEntryDto>>("/api/v1/trash");
        var entry = trash.Items.Single(t => t.Id == world.GreyBox.Id);
        entry.ContainedCount.ShouldBe(2);   // пакет и кабель

        var restored = await _http.PostJsonAsync<object?, ItemDetailsDto>($"/api/v1/trash/{world.GreyBox.Id}/restore", null);
        restored.IsDeleted.ShouldBeFalse();
        restored.Counts.ShouldBe(new ItemCountsDto(1, 2));
        (await _http.GetJsonAsync<ItemDetailsDto>($"/api/v1/items/{cable.Id}")).Path.Select(p => p.Name).ShouldBe([world.Attic.Name, world.GreyBox.Name, world.Bag.Name]);
    }

    [Fact]
    public async Task Deleting_with_move_up_keeps_contents_and_empty_trash_purges()
    {
        var world = await World.CreateAsync(_http);
        var cable = await world.CreateCableAsync(_http, world.Bag.Id, 50);

        using (var deleted = await _http.DeleteAsync($"/api/v1/items/{world.Bag.Id}?contents=MoveUp", TestContext.Current.CancellationToken))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await _http.GetJsonAsync<ItemDetailsDto>($"/api/v1/items/{cable.Id}")).ParentId.ShouldBe(world.GreyBox.Id);

        using (var purge = await _http.DeleteAsync("/api/v1/trash", TestContext.Current.CancellationToken))
        {
            purge.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await _http.SendExpectingErrorAsync(HttpMethod.Get, $"/api/v1/items/{world.Bag.Id}")).Status.ShouldBe(404);
    }

    [Fact]
    public async Task Tags_are_assigned_counted_and_merged()
    {
        var world = await World.CreateAsync(_http);
        var charging = await _http.PostJsonAsync<SaveTagRequest, TagDto>("/api/v1/tags", new SaveTagRequest($"#зарядка-{world.Suffix}"));
        var power = await _http.PostJsonAsync<SaveTagRequest, TagDto>("/api/v1/tags", new SaveTagRequest($"питание-{world.Suffix}", "#FFAA00"));
        charging.Name.ShouldBe($"зарядка-{world.Suffix}");

        var cable = await world.CreateCableAsync(_http, world.Bag.Id, 50, [charging.Id]);
        cable.TagIds.ShouldBe([charging.Id]);

        var merged = await _http.PostJsonAsync<MergeTagRequest, TagDto>($"/api/v1/tags/{charging.Id}/merge", new MergeTagRequest(power.Id));
        merged.ItemCount.ShouldBe(1);
        (await _http.GetJsonAsync<ItemDetailsDto>($"/api/v1/items/{cable.Id}")).TagIds.ShouldBe([power.Id]);
        (await _http.GetJsonAsync<IReadOnlyList<TagDto>>("/api/v1/tags")).ShouldNotContain(t => t.Id == charging.Id);
    }

    [Fact]
    public async Task Type_names_are_unique_and_versions_checked()
    {
        var world = await World.CreateAsync(_http);

        var (status, problem) = await _http.SendExpectingErrorAsync(HttpMethod.Post, "/api/v1/types", new SaveItemTypeRequest(world.Cable.Name.ToUpperInvariant(), TypeRole.Item, []));
        status.ShouldBe(422);
        problem.GetRawText().ShouldContain("уже есть");

        var renamed = await _http.PutJsonAsync<SaveItemTypeRequest, ItemTypeDto>($"/api/v1/types/{world.Box.Id}", new SaveItemTypeRequest(
            world.Box.Name, TypeRole.Container, [new FieldDefinitionInput(null, "Материал", FieldDataType.Text)], Version: world.Box.Version));
        renamed.Fields.ShouldHaveSingleItem().Name.ShouldBe("Материал");

        (status, _) = await _http.SendExpectingErrorAsync(HttpMethod.Put, $"/api/v1/types/{world.Box.Id}", new SaveItemTypeRequest(world.Box.Name, TypeRole.Container, [], Version: world.Box.Version));
        status.ShouldBe(409);

        (status, problem) = await _http.SendExpectingErrorAsync(HttpMethod.Delete, $"/api/v1/types/{world.Box.Id}");
        status.ShouldBe(422);
        problem.GetRawText().ShouldContain("используется");
    }

    [Fact]
    public async Task Option_can_be_added_from_item_form_once()
    {
        var world = await World.CreateAsync(_http);

        var lightning = await _http.PostJsonAsync<AddOptionRequest, ValueListOptionDto>($"/api/v1/lists/{world.Connectors.Id}/options", new AddOptionRequest("Lightning"));
        var again = await _http.PostJsonAsync<AddOptionRequest, ValueListOptionDto>($"/api/v1/lists/{world.Connectors.Id}/options", new AddOptionRequest(" lightning "));

        again.Id.ShouldBe(lightning.Id);
        (await _http.GetJsonAsync<ValueListDto>($"/api/v1/lists/{world.Connectors.Id}")).Options.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Sync_feed_returns_changes_after_given_sequence()
    {
        var before = await _http.GetJsonAsync<SyncChangesResponse>("/api/v1/sync/changes?since=0&take=1");
        var world = await World.CreateAsync(_http);

        var changes = await _http.GetJsonAsync<SyncChangesResponse>($"/api/v1/sync/changes?since={before.LastSeq}");

        changes.HasMore.ShouldBeFalse();
        changes.Changes.Select(c => c.Seq).ShouldBeInOrder();
        changes.Changes.ShouldContain(c => c.EntityId == world.Bag.Id && c.Action == ChangeAction.Created && c.ParentId == world.GreyBox.Id);
        changes.LastSeq.ShouldBe(changes.Changes[^1].Seq);
    }

    /// <summary>Справочник «Разъёмы», типы «Место»/«Коробка»/«Кабель» и цепочка Чердак → Серая коробка → Пакет.</summary>
    private sealed record World(
        string Suffix,
        ValueListDto Connectors,
        ValueListOptionDto TypeA,
        ValueListOptionDto TypeC,
        ItemTypeDto Place,
        ItemTypeDto Box,
        ItemTypeDto Cable,
        FieldDefinitionDto Length,
        FieldDefinitionDto Connector1,
        FieldDefinitionDto Connector2,
        FieldDefinitionDto Color,
        ItemDetailsDto Attic,
        ItemDetailsDto GreyBox,
        ItemDetailsDto Bag)
    {
        public static async Task<World> CreateAsync(HttpClient http)
        {
            var suffix = Guid.NewGuid().ToString("N")[..6];
            var connectors = await http.PostJsonAsync<SaveValueListRequest, ValueListDto>("/api/v1/lists", new SaveValueListRequest(
                $"Разъёмы {suffix}", [new ValueListOptionInput(null, "Type-A"), new ValueListOptionInput(null, "Type-C")]));
            var place = await http.PostJsonAsync<SaveItemTypeRequest, ItemTypeDto>("/api/v1/types", new SaveItemTypeRequest($"Место {suffix}", TypeRole.Place, []));
            var box = await http.PostJsonAsync<SaveItemTypeRequest, ItemTypeDto>("/api/v1/types", new SaveItemTypeRequest($"Коробка {suffix}", TypeRole.Container, []));
            var cable = await http.PostJsonAsync<SaveItemTypeRequest, ItemTypeDto>("/api/v1/types", new SaveItemTypeRequest(
                $"Кабель {suffix}",
                TypeRole.Item,
                [
                    new FieldDefinitionInput(null, "Длина", FieldDataType.Integer, Unit: "см", IsRequired: true, ShowInList: true),
                    new FieldDefinitionInput(null, "Разъём 1", FieldDataType.Choice, ValueListId: connectors.Id),
                    new FieldDefinitionInput(null, "Разъём 2", FieldDataType.Choice, ValueListId: connectors.Id),
                    new FieldDefinitionInput(null, "Цвет", FieldDataType.Text, RememberLast: true),
                ],
                TitleTemplate: "{Разъём 1} → {Разъём 2}, {Длина} см"));

            var attic = await http.PostJsonAsync<SaveItemRequest, ItemDetailsDto>("/api/v1/items", new SaveItemRequest(place.Id, Name: $"Чердак {suffix}"));
            var greyBox = await http.PostJsonAsync<SaveItemRequest, ItemDetailsDto>("/api/v1/items", new SaveItemRequest(box.Id, attic.Id, $"Серая коробка {suffix}"));
            var bag = await http.PostJsonAsync<SaveItemRequest, ItemDetailsDto>("/api/v1/items", new SaveItemRequest(box.Id, greyBox.Id, $"Прозрачный пакет {suffix}"));

            return new World(
                suffix, connectors, connectors.Options[0], connectors.Options[1], place, box, cable,
                cable.Fields[0], cable.Fields[1], cable.Fields[2], cable.Fields[3], attic, greyBox, bag);
        }

        public Task<ItemDetailsDto> CreateCableAsync(HttpClient http, Guid parentId, int length, IReadOnlyList<Guid>? tags = null) =>
            http.PostJsonAsync<SaveItemRequest, ItemDetailsDto>("/api/v1/items", new SaveItemRequest(
                Cable.Id, parentId, Values: [new FieldValueDto(Length.Id, Number: length)], TagIds: tags, Name: $"Кабель {length} см"));
    }
}

using Dapper;
using Invy.Application.Abstractions;
using Invy.Application.Errors;
using Invy.Contracts.Sync;
using Invy.Domain.Audit;
using Invy.Domain.Items;
using Invy.Domain.Tags;
using Invy.Domain.Types;
using Microsoft.EntityFrameworkCore;

namespace Invy.Infrastructure.Tests.Persistence;

public sealed class PersistenceTests : IAsyncLifetime
{
    private TestDatabase _database = null!;

    public async ValueTask InitializeAsync() => _database = await TestDatabase.CreateAsync();

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Migration_creates_schema_with_fts_table()
    {
        await using var scope = _database.OpenScope();
        var connection = scope.Db.Database.GetDbConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var tables = (await connection.QueryAsync<string>("SELECT name FROM sqlite_master WHERE type = 'table'")).ToHashSet();

        string[] expected = ["Items", "ItemValues", "ItemTags", "ItemTypes", "FieldDefinitions", "ValueLists", "ValueListOptions", "Tags", "Media", "Devices", "AuditLog", "ItemsFts"];
        expected.Where(t => !tables.Contains(t)).ShouldBeEmpty();
        (await scope.Db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task New_items_get_numbers_versions_and_creation_history()
    {
        await using var scope = _database.OpenScope();
        var place = Seed.Type("Место", TypeRole.Place);
        var attic = Seed.Item(place, "Чердак");
        var shed = Seed.Item(place, "Сарай");
        scope.Db.AddRange(place, attic, shed);

        await scope.UnitOfWork.SaveAsync(cancellationToken: TestContext.Current.CancellationToken);

        new[] { attic.Number, shed.Number }.ShouldBe([1, 2], ignoreOrder: true);
        attic.Version.ShouldBe(1);
        attic.CreatedAt.ShouldNotBe(default);

        var history = await scope.Db.AuditLog.OrderBy(a => a.Id).ToListAsync(TestContext.Current.CancellationToken);
        history.Select(h => (h.EntityType, h.Action)).ShouldBe(
            [(AuditEntityType.Item, AuditAction.Created), (AuditEntityType.Item, AuditAction.Created), (AuditEntityType.ItemType, AuditAction.Created)],
            ignoreOrder: true);
        history.ShouldContain(h => h.Summary == "Создано: Чердак");
    }

    [Fact]
    public async Task Changing_a_value_records_readable_history_and_bumps_version()
    {
        var length = new FieldDefinition { Name = "Длина", DataType = FieldDataType.Integer, Unit = "см" };
        Guid cableId;
        await using (var scope = _database.OpenScope())
        {
            var cable = Seed.Type("Кабель", TypeRole.Item, length);
            var item = Seed.Item(cable, "Кабель USB");
            item.Values.AddRange(new FieldValue(length.Id, Number: 100).ToRows(item.Id, length));
            scope.Db.AddRange(cable, item);
            await scope.UnitOfWork.SaveAsync(cancellationToken: TestContext.Current.CancellationToken);
            cableId = item.Id;
        }

        await using (var scope = _database.OpenScope())
        {
            var item = await scope.Db.Items.Include(i => i.Values).SingleAsync(i => i.Id == cableId, TestContext.Current.CancellationToken);
            item.Values.Single().NumberValue = 150;
            await scope.UnitOfWork.SaveAsync(cancellationToken: TestContext.Current.CancellationToken);

            item.Version.ShouldBe(2);
            var entry = await scope.Db.AuditLog.OrderBy(a => a.Id).LastAsync(TestContext.Current.CancellationToken);
            entry.Action.ShouldBe(AuditAction.Updated);
            entry.Changes.ShouldBe([new AuditChange($"field:{length.Id}", "Длина", "100 см", "150 см")]);
            entry.Summary.ShouldBe("Изменено: Кабель USB — Длина");
        }
    }

    [Fact]
    public async Task Move_delete_restore_and_tags_are_recorded()
    {
        await using var scope = _database.OpenScope();
        var place = Seed.Type("Место", TypeRole.Place);
        var box = Seed.Type("Коробка", TypeRole.Container);
        var attic = Seed.Item(place, "Чердак");
        var greyBox = Seed.Item(box, "Серая коробка", attic);
        var shed = Seed.Item(place, "Сарай");
        var tag = new Tag();
        tag.Rename("зарядка");
        scope.Db.AddRange(place, box, attic, greyBox, shed, tag);
        await scope.UnitOfWork.SaveAsync(cancellationToken: TestContext.Current.CancellationToken);

        greyBox.ParentId = shed.Id;
        await scope.UnitOfWork.SaveAsync(cancellationToken: TestContext.Current.CancellationToken);
        var moved = await LastAsync(scope);
        moved.Action.ShouldBe(AuditAction.Moved);
        moved.Summary.ShouldBe("Перемещено: Чердак → Сарай");
        (moved.OldParentId, moved.ParentId).ShouldBe((attic.Id, shed.Id));

        greyBox.Tags.Add(new ItemTag { ItemId = greyBox.Id, TagId = tag.Id });
        await scope.UnitOfWork.SaveAsync(cancellationToken: TestContext.Current.CancellationToken);
        (await LastAsync(scope)).Changes.ShouldBe([new AuditChange("tags", "Теги", null, "#зарядка")]);

        greyBox.MarkDeleted(Guid.NewGuid(), DateTimeOffset.UtcNow);
        await scope.UnitOfWork.SaveAsync(cancellationToken: TestContext.Current.CancellationToken);
        (await LastAsync(scope)).Summary.ShouldBe("В корзину: Серая коробка");
        (await scope.Db.Items.AnyAsync(i => i.Id == greyBox.Id, TestContext.Current.CancellationToken)).ShouldBeFalse();

        greyBox.Restore();
        await scope.UnitOfWork.SaveAsync(cancellationToken: TestContext.Current.CancellationToken);
        (await LastAsync(scope)).Action.ShouldBe(AuditAction.Restored);
    }

    [Fact]
    public async Task Tree_queries_return_path_counts_children_and_descendants()
    {
        await using var scope = _database.OpenScope();
        var place = Seed.Type("Место", TypeRole.Place);
        var box = Seed.Type("Коробка", TypeRole.Container);
        var cable = Seed.Type("Кабель", TypeRole.Item);
        var house = Seed.Item(place, "Дом");
        var attic = Seed.Item(place, "Чердак", house);
        var greyBox = Seed.Item(box, "Серая коробка", attic);
        var bag = Seed.Item(box, "Пакет", greyBox);
        var usb10 = Seed.Item(cable, "Кабель 10", bag);
        var usb2 = Seed.Item(cable, "кабель 2", bag);
        var loose = Seed.Item(cable, "Без места");
        scope.Db.AddRange(place, box, cable, house, attic, greyBox, bag, usb10, usb2, loose);
        await scope.UnitOfWork.SaveAsync(cancellationToken: TestContext.Current.CancellationToken);
        var queries = scope.Queries;
        var ct = TestContext.Current.CancellationToken;

        (await queries.GetPathAsync(usb2.Id, ct)).Select(p => p.Name).ShouldBe(["Дом", "Чердак", "Серая коробка", "Пакет"]);
        (await queries.GetCountsAsync(attic.Id, ct)).ShouldBe(new ItemCounts(1, 4));

        var children = await queries.GetChildrenAsync(bag.Id, ChildrenScope.Container, ItemSort.Name, false, 0, 50, ct);
        children.Items.Select(c => c.Name).ShouldBe(["кабель 2", "Кабель 10"]);   // естественная сортировка без учёта регистра
        children.Total.ShouldBe(2);
        children.Items[0].TypeName.ShouldBe("Кабель");

        (await queries.GetChildrenAsync(null, ChildrenScope.RootPlaces, ItemSort.Name, false, 0, 50, ct)).Items.Select(c => c.Name).ShouldBe(["Дом"]);
        (await queries.GetChildrenAsync(null, ChildrenScope.Unsorted, ItemSort.Name, false, 0, 50, ct)).Items.Select(c => c.Name).ShouldBe(["Без места"]);

        var tree = await queries.GetTreeAsync(attic.Id, ct);
        tree.ShouldHaveSingleItem().Name.ShouldBe("Серая коробка");
        tree[0].HasChildren.ShouldBeTrue();
        (await queries.GetTreeAsync(bag.Id, ct)).ShouldBeEmpty();   // в пакете только вещи

        (await queries.GetDescendantIdsAsync(greyBox.Id, false, ct)).ShouldBe([bag.Id, usb10.Id, usb2.Id], ignoreOrder: true);
        (await queries.IsSelfOrDescendantAsync(usb2.Id, attic.Id, ct)).ShouldBeTrue();
        (await queries.IsSelfOrDescendantAsync(attic.Id, usb2.Id, ct)).ShouldBeFalse();
        (await queries.IsSelfOrDescendantAsync(bag.Id, bag.Id, ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task Text_value_suggestions_are_case_and_yo_insensitive()
    {
        var color = new FieldDefinition { Name = "Цвет", DataType = FieldDataType.Text };
        await using var scope = _database.OpenScope();
        var cable = Seed.Type("Кабель", TypeRole.Item, color);
        scope.Db.Add(cable);
        foreach (var (name, value) in new[] { ("A", "Чёрный"), ("B", "чёрный"), ("C", "Черничный"), ("D", "Белый") })
        {
            var item = Seed.Item(cable, name);
            item.Values.AddRange(new FieldValue(color.Id, Text: value).ToRows(item.Id, color));
            scope.Db.Add(item);
        }

        await scope.UnitOfWork.SaveAsync(cancellationToken: TestContext.Current.CancellationToken);

        var suggestions = await scope.Queries.SuggestTextValuesAsync(color.Id, "ЧЕР", 10, TestContext.Current.CancellationToken);

        suggestions.Count.ShouldBe(2);   // «Чёрный» и «чёрный» — одно значение
        suggestions[0].ShouldBe("Чёрный", StringCompareShould.IgnoreCase);
    }

    [Fact]
    public async Task Concurrent_edit_is_rejected_with_conflict()
    {
        var place = Seed.Type("Место", TypeRole.Place);
        var attic = Seed.Item(place, "Чердак");
        await using (var setup = _database.OpenScope())
        {
            setup.Db.AddRange(place, attic);
            await setup.UnitOfWork.SaveAsync(cancellationToken: TestContext.Current.CancellationToken);
        }

        await using var first = _database.OpenScope();
        await using var second = _database.OpenScope();
        var a = await first.Db.Items.SingleAsync(i => i.Id == attic.Id, TestContext.Current.CancellationToken);
        var b = await second.Db.Items.SingleAsync(i => i.Id == attic.Id, TestContext.Current.CancellationToken);

        a.Rename("Чердак над гаражом");
        await first.UnitOfWork.SaveAsync(cancellationToken: TestContext.Current.CancellationToken);

        b.Rename("Мансарда");
        await Should.ThrowAsync<ConflictException>(() => second.UnitOfWork.SaveAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Clients_are_notified_after_commit_with_sequence_numbers()
    {
        await using var scope = _database.OpenScope();
        var place = Seed.Type("Место", TypeRole.Place);
        var correlation = Guid.NewGuid();
        scope.Db.AddRange(place, Seed.Item(place, "Чердак"));

        await scope.UnitOfWork.SaveAsync(correlation, TestContext.Current.CancellationToken);

        var batch = _database.Notifier.Batches.ShouldHaveSingleItem();
        batch.CorrelationId.ShouldBe(correlation);
        batch.Changes.Count.ShouldBe(2);
        batch.Changes.Select(c => c.Seq).ShouldBe([batch.FirstSeq, batch.LastSeq]);
        batch.Changes.ShouldContain(c => c.EntityType == ChangeEntityType.Item && c.Action == ChangeAction.Created);
        (await scope.Db.AuditLog.MaxAsync(a => a.Id, TestContext.Current.CancellationToken)).ShouldBe(batch.LastSeq);
    }

    private static Task<AuditEntry> LastAsync(TestDatabase.Scope scope) =>
        scope.Db.AuditLog.OrderBy(a => a.Id).LastAsync(TestContext.Current.CancellationToken);
}

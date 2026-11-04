using Invy.Api.Auth;
using Invy.Application.Abstractions;
using Invy.Application.History;
using Invy.Application.Items;
using Invy.Contracts;
using Invy.Contracts.Items;
using Invy.Contracts.Sync;

namespace Invy.Api.Endpoints;

/// <summary>Вещи, дерево, корзина, история и синхронизация.</summary>
internal static class ItemEndpoints
{
    public static IEndpointRouteBuilder MapItems(this IEndpointRouteBuilder app)
    {
        var items = app.MapGroup($"{ApiRoutes.Prefix}/items").WithTags("Вещи");
        items.MapGet("/{id:guid}", (ItemService s, Guid id, CancellationToken ct) => s.GetAsync(id, ct));
        items.MapGet("/{id:guid}/children", (ItemService s, Guid id, ItemSort? sort, bool? desc, int? skip, int? take, CancellationToken ct) =>
            s.GetChildrenAsync(id, ChildrenScope.Container, sort ?? ItemSort.Name, desc ?? false, skip ?? 0, take ?? 100, ct));
        items.MapGet("/roots", (ItemService s, ItemSort? sort, bool? desc, int? skip, int? take, CancellationToken ct) =>
            s.GetChildrenAsync(null, ChildrenScope.RootPlaces, sort ?? ItemSort.Name, desc ?? false, skip ?? 0, take ?? 100, ct));
        items.MapGet("/unsorted", (ItemService s, ItemSort? sort, bool? desc, int? skip, int? take, CancellationToken ct) =>
            s.GetChildrenAsync(null, ChildrenScope.Unsorted, sort ?? ItemSort.Updated, desc ?? true, skip ?? 0, take ?? 100, ct));
        items.MapPost("/batch-get", (ItemService s, BatchGetRequest request, CancellationToken ct) => s.BatchGetAsync(request.Ids, ct));

        items.MapPost("/", async (ItemService s, SaveItemRequest request, CancellationToken ct) =>
        {
            var created = await s.CreateAsync(request, ct);
            return TypedResults.Created($"{ApiRoutes.Prefix}/items/{created.Id}", created);
        }).RequireAuthorization(Policies.Editor);
        items.MapPut("/{id:guid}", (ItemService s, Guid id, SaveItemRequest request, CancellationToken ct) => s.UpdateAsync(id, request, ct))
            .RequireAuthorization(Policies.Editor);
        items.MapPost("/move", async (ItemService s, MoveItemsRequest request, CancellationToken ct) =>
        {
            await s.MoveAsync(request.ItemIds, request.TargetId, ct);
            return TypedResults.NoContent();
        }).RequireAuthorization(Policies.Editor);
        items.MapPost("/{id:guid}/duplicate", async (ItemService s, Guid id, CancellationToken ct) =>
        {
            var copy = await s.DuplicateAsync(id, ct);
            return TypedResults.Created($"{ApiRoutes.Prefix}/items/{copy.Id}", copy);
        }).RequireAuthorization(Policies.Editor);
        items.MapDelete("/{id:guid}", async (ItemService s, Guid id, DeleteContentsMode? contents, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, contents, ct);
            return TypedResults.NoContent();
        }).RequireAuthorization(Policies.Editor);
        items.MapGet("/{id:guid}/history", (HistoryService s, Guid id, int? skip, int? take, CancellationToken ct) =>
            s.QueryAsync(ChangeEntityType.Item, id, null, null, null, skip ?? 0, take ?? 50, ct));

        app.MapGet($"{ApiRoutes.Prefix}/tree", (ItemService s, Guid? parentId, CancellationToken ct) => s.GetTreeAsync(parentId, ct))
            .WithTags("Вещи");

        var trash = app.MapGroup($"{ApiRoutes.Prefix}/trash").WithTags("Корзина");
        trash.MapGet("/", (ItemService s, int? skip, int? take, CancellationToken ct) => s.GetTrashAsync(skip ?? 0, take ?? 100, ct));
        trash.MapPost("/{id:guid}/restore", (ItemService s, Guid id, CancellationToken ct) => s.RestoreAsync(id, ct))
            .RequireAuthorization(Policies.Editor);
        trash.MapDelete("/", async (ItemService s, CancellationToken ct) =>
        {
            await s.EmptyTrashAsync(ct);
            return TypedResults.NoContent();
        }).RequireAuthorization(Policies.Editor);

        app.MapGet($"{ApiRoutes.Prefix}/audit", (HistoryService s, ChangeEntityType? entity, Guid? id, Guid? device, DateTimeOffset? from, DateTimeOffset? to, int? skip, int? take, CancellationToken ct) =>
                s.QueryAsync(entity, id, device, from, to, skip ?? 0, take ?? 50, ct))
            .WithTags("История");
        app.MapGet($"{ApiRoutes.Prefix}/sync/changes", (HistoryService s, long? since, int? take, CancellationToken ct) => s.GetChangesAsync(since ?? 0, take ?? 500, ct))
            .WithTags("История");

        return app;
    }
}

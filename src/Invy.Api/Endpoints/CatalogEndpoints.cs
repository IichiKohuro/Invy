using Invy.Api.Auth;
using Invy.Application.Catalog;
using Invy.Application.Items;
using Invy.Contracts;
using Invy.Contracts.Catalog;

namespace Invy.Api.Endpoints;

/// <summary>Типы, справочники, теги — <c>/api/v1/types|lists|tags</c>.</summary>
internal static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalog(this IEndpointRouteBuilder app)
    {
        var types = app.MapGroup($"{ApiRoutes.Prefix}/types").WithTags("Типы");
        types.MapGet("/", (ItemTypeService s, bool? includeArchived, CancellationToken ct) => s.ListAsync(includeArchived ?? false, ct));
        types.MapGet("/{id:guid}", (ItemTypeService s, Guid id, CancellationToken ct) => s.GetAsync(id, ct));
        types.MapPost("/", async (ItemTypeService s, SaveItemTypeRequest request, CancellationToken ct) =>
        {
            var created = await s.CreateAsync(request, ct);
            return TypedResults.Created($"{ApiRoutes.Prefix}/types/{created.Id}", created);
        }).RequireAuthorization(Policies.Editor);
        types.MapPut("/{id:guid}", (ItemTypeService s, Guid id, SaveItemTypeRequest request, CancellationToken ct) => s.UpdateAsync(id, request, ct))
            .RequireAuthorization(Policies.Editor);
        types.MapDelete("/{id:guid}", async (ItemTypeService s, Guid id, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return TypedResults.NoContent();
        }).RequireAuthorization(Policies.Editor);

        var lists = app.MapGroup($"{ApiRoutes.Prefix}/lists").WithTags("Справочники");
        lists.MapGet("/", (ValueListService s, CancellationToken ct) => s.ListAsync(ct));
        lists.MapGet("/{id:guid}", (ValueListService s, Guid id, CancellationToken ct) => s.GetAsync(id, ct));
        lists.MapPost("/", async (ValueListService s, SaveValueListRequest request, CancellationToken ct) =>
        {
            var created = await s.CreateAsync(request, ct);
            return TypedResults.Created($"{ApiRoutes.Prefix}/lists/{created.Id}", created);
        }).RequireAuthorization(Policies.Editor);
        lists.MapPut("/{id:guid}", (ValueListService s, Guid id, SaveValueListRequest request, CancellationToken ct) => s.UpdateAsync(id, request, ct))
            .RequireAuthorization(Policies.Editor);
        lists.MapDelete("/{id:guid}", async (ValueListService s, Guid id, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return TypedResults.NoContent();
        }).RequireAuthorization(Policies.Editor);
        lists.MapPost("/{id:guid}/options", (ValueListService s, Guid id, AddOptionRequest request, CancellationToken ct) => s.AddOptionAsync(id, request, ct))
            .RequireAuthorization(Policies.Editor);

        var tags = app.MapGroup($"{ApiRoutes.Prefix}/tags").WithTags("Теги");
        tags.MapGet("/", (TagService s, CancellationToken ct) => s.ListAsync(ct));
        tags.MapPost("/", async (TagService s, SaveTagRequest request, CancellationToken ct) =>
        {
            var created = await s.CreateAsync(request, ct);
            return TypedResults.Created($"{ApiRoutes.Prefix}/tags/{created.Id}", created);
        }).RequireAuthorization(Policies.Editor);
        tags.MapPut("/{id:guid}", (TagService s, Guid id, SaveTagRequest request, CancellationToken ct) => s.UpdateAsync(id, request, ct))
            .RequireAuthorization(Policies.Editor);
        tags.MapDelete("/{id:guid}", async (TagService s, Guid id, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return TypedResults.NoContent();
        }).RequireAuthorization(Policies.Editor);
        tags.MapPost("/{id:guid}/merge", (TagService s, Guid id, MergeTagRequest request, CancellationToken ct) => s.MergeAsync(id, request.TargetTagId, ct))
            .RequireAuthorization(Policies.Editor);

        app.MapGet($"{ApiRoutes.Prefix}/fields/{{id:guid}}/suggest", (ItemService s, Guid id, string? q, int? take, CancellationToken ct) => s.SuggestAsync(id, q, take ?? 10, ct))
            .WithTags("Типы");

        return app;
    }
}

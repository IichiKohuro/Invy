using Invy.Api.Auth;
using Invy.Application.Abstractions;
using Invy.Application.Media;
using Invy.Application.Setup;
using Invy.Contracts;
using Invy.Contracts.Items;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Invy.Api.Endpoints;

/// <summary>Фото вещей и стартовый набор.</summary>
internal static class MediaEndpoints
{
    private const long MaxUploadBytes = 200L * 1024 * 1024;

    public static IEndpointRouteBuilder MapMedia(this IEndpointRouteBuilder app)
    {
        app.MapPost($"{ApiRoutes.Prefix}/items/{{id:guid}}/media", async (MediaService s, Guid id, IFormFileCollection files, CancellationToken ct) =>
            {
                var uploads = files.Select(f => new UploadFile(f.FileName, f.Length, f.OpenReadStream)).ToList();
                var created = await s.UploadAsync(id, uploads, ct);
                return TypedResults.Created($"{ApiRoutes.Prefix}/items/{id}", created);
            })
            .WithTags("Фото")
            .DisableAntiforgery()   // авторизация — токен в заголовке, не cookie: CSRF невозможен
            .WithMetadata(new RequestSizeLimitAttribute(MaxUploadBytes), new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxUploadBytes })
            .RequireAuthorization(Policies.Editor);

        app.MapGet($"{ApiRoutes.Prefix}/media/{{id:guid}}", async (MediaService s, Guid id, MediaSize? size, HttpContext http, CancellationToken ct) =>
            {
                var content = await s.GetAsync(id, size ?? MediaSize.Preview, ct);
                // Файлы неизменяемы (имя = хэш содержимого); private — потому что нужен токен.
                http.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
                return TypedResults.PhysicalFile(content.Path, content.ContentType, entityTag: new EntityTagHeaderValue(content.ETag));
            })
            .WithTags("Фото");

        app.MapPut($"{ApiRoutes.Prefix}/items/{{id:guid}}/cover", async (MediaService s, Guid id, SetCoverRequest request, CancellationToken ct) =>
            {
                await s.SetCoverAsync(id, request.MediaId, ct);
                return TypedResults.NoContent();
            })
            .WithTags("Фото")
            .RequireAuthorization(Policies.Editor);

        app.MapDelete($"{ApiRoutes.Prefix}/media/{{id:guid}}", async (MediaService s, Guid id, CancellationToken ct) =>
            {
                await s.DeleteAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithTags("Фото")
            .RequireAuthorization(Policies.Editor);

        app.MapPost($"{ApiRoutes.Prefix}/setup/starter-kit", (StarterKitService s, CancellationToken ct) => s.ApplyAsync(ct))
            .WithTags("Типы")
            .RequireAuthorization(Policies.Editor);

        return app;
    }
}

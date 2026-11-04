using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Invy.Contracts;
using Invy.Contracts.Catalog;
using Invy.Contracts.Devices;
using Invy.Contracts.Items;
using Invy.Contracts.Server;
using Invy.Contracts.Sync;

namespace Invy.Client.Core.Api;

/// <summary>Размер фото при загрузке с сервера.</summary>
public enum PhotoSize
{
    Thumb,
    Preview,
    Original,
}

/// <summary>Ошибка API: код ответа, текст для пользователя, ошибки по полям (422) и актуальное состояние (409).</summary>
public sealed class ApiException(HttpStatusCode status, string title, string? detail, IReadOnlyDictionary<string, string[]> errors, JsonElement? current)
    : Exception(detail ?? title)
{
    public HttpStatusCode Status { get; } = status;

    public string Title { get; } = title;

    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    /// <summary>Актуальное состояние записи при конфликте версий (409).</summary>
    public JsonElement? Current { get; } = current;

    public bool IsConflict => Status == HttpStatusCode.Conflict;

    public bool IsValidation => Status == HttpStatusCode.UnprocessableEntity;

    /// <summary>Все сообщения одной строкой — для InfoBar.</summary>
    public string UserMessage => Errors.Count > 0 ? string.Join("\n", Errors.Values.SelectMany(e => e).Distinct()) : Message;

    public T? CurrentAs<T>(JsonTypeInfo<T> info) => Current is { } element ? element.Deserialize(info) : default;
}

/// <summary>
/// Клиент REST API Invy (<c>/api/v1</c>). Сериализация — source-generated <see cref="InvyJsonContext"/>,
/// без рефлексии (Android с триммингом). Ошибки сервера — <see cref="ApiException"/>.
/// </summary>
public sealed class InvyApiClient(HttpClient http)
{
    private static readonly InvyJsonContext Json = InvyJsonContext.Default;

    public HttpClient Http => http;

    // ---------- Сервер и устройства ----------
    public Task<ServerInfoDto> GetServerInfoAsync(CancellationToken ct = default) => GetAsync(ApiRoutes.Info, Json.ServerInfoDto, ct);

    public Task<CurrentDeviceDto> GetMeAsync(CancellationToken ct = default) => GetAsync("api/v1/devices/me", Json.CurrentDeviceDto, ct);

    public Task<PairingRequestResponse> RequestPairingAsync(PairingRequestCreate request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/v1/pairing/requests", request, Json.PairingRequestCreate, Json.PairingRequestResponse, ct);

    public Task<PairingRequestResponse> GetPairingStatusAsync(Guid requestId, CancellationToken ct = default) =>
        GetAsync($"api/v1/pairing/requests/{requestId}", Json.PairingRequestResponse, ct);

    public Task<IReadOnlyList<PendingPairingDto>> GetPendingPairingsAsync(CancellationToken ct = default) =>
        GetAsync("api/v1/pairing/requests", Json.IReadOnlyListPendingPairingDto, ct);

    public Task<DeviceDto> ApprovePairingAsync(Guid requestId, ApprovePairingRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"api/v1/pairing/requests/{requestId}/approve", request, Json.ApprovePairingRequest, Json.DeviceDto, ct);

    public Task DenyPairingAsync(Guid requestId, CancellationToken ct = default) => SendAsync(HttpMethod.Post, $"api/v1/pairing/requests/{requestId}/deny", ct);

    public Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(CancellationToken ct = default) => GetAsync("api/v1/devices", Json.IReadOnlyListDeviceDto, ct);

    // ---------- Типы, справочники, теги ----------
    public Task<IReadOnlyList<ItemTypeDto>> GetTypesAsync(bool includeArchived = false, CancellationToken ct = default) =>
        GetAsync($"api/v1/types?includeArchived={(includeArchived ? "true" : "false")}", Json.IReadOnlyListItemTypeDto, ct);

    public Task<ItemTypeDto> GetTypeAsync(Guid id, CancellationToken ct = default) => GetAsync($"api/v1/types/{id}", Json.ItemTypeDto, ct);

    public Task<ItemTypeDto> CreateTypeAsync(SaveItemTypeRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/v1/types", request, Json.SaveItemTypeRequest, Json.ItemTypeDto, ct);

    public Task<ItemTypeDto> UpdateTypeAsync(Guid id, SaveItemTypeRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"api/v1/types/{id}", request, Json.SaveItemTypeRequest, Json.ItemTypeDto, ct);

    public Task DeleteTypeAsync(Guid id, CancellationToken ct = default) => SendAsync(HttpMethod.Delete, $"api/v1/types/{id}", ct);

    public Task<IReadOnlyList<ValueListDto>> GetListsAsync(CancellationToken ct = default) => GetAsync("api/v1/lists", Json.IReadOnlyListValueListDto, ct);

    public Task<ValueListDto> CreateListAsync(SaveValueListRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/v1/lists", request, Json.SaveValueListRequest, Json.ValueListDto, ct);

    public Task<ValueListDto> UpdateListAsync(Guid id, SaveValueListRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"api/v1/lists/{id}", request, Json.SaveValueListRequest, Json.ValueListDto, ct);

    public Task DeleteListAsync(Guid id, CancellationToken ct = default) => SendAsync(HttpMethod.Delete, $"api/v1/lists/{id}", ct);

    public Task<ValueListOptionDto> AddOptionAsync(Guid listId, AddOptionRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"api/v1/lists/{listId}/options", request, Json.AddOptionRequest, Json.ValueListOptionDto, ct);

    public Task<IReadOnlyList<TagDto>> GetTagsAsync(CancellationToken ct = default) => GetAsync("api/v1/tags", Json.IReadOnlyListTagDto, ct);

    public Task<TagDto> CreateTagAsync(SaveTagRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/v1/tags", request, Json.SaveTagRequest, Json.TagDto, ct);

    public Task<TagDto> UpdateTagAsync(Guid id, SaveTagRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"api/v1/tags/{id}", request, Json.SaveTagRequest, Json.TagDto, ct);

    public Task DeleteTagAsync(Guid id, CancellationToken ct = default) => SendAsync(HttpMethod.Delete, $"api/v1/tags/{id}", ct);

    /// <summary>Вещи тега <paramref name="id"/> получают тег <paramref name="targetTagId"/>, исходный тег удаляется.</summary>
    public Task<TagDto> MergeTagAsync(Guid id, Guid targetTagId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"api/v1/tags/{id}/merge", new MergeTagRequest(targetTagId), Json.MergeTagRequest, Json.TagDto, ct);

    public Task<IReadOnlyList<string>> SuggestAsync(Guid fieldId, string prefix, int take = 10, CancellationToken ct = default) =>
        GetAsync($"api/v1/fields/{fieldId}/suggest?q={Uri.EscapeDataString(prefix)}&take={take}", Json.IReadOnlyListString, ct);

    public Task<StarterKitResult> ApplyStarterKitAsync(CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/v1/setup/starter-kit", Json.StarterKitResult, ct);

    // ---------- Вещи и дерево ----------
    public Task<ItemDetailsDto> GetItemAsync(Guid id, CancellationToken ct = default) => GetAsync($"api/v1/items/{id}", Json.ItemDetailsDto, ct);

    /// <param name="sort">Name | Updated | Created | Number | Type.</param>
    public Task<PagedResult<ItemSummaryDto>> GetChildrenAsync(Guid parentId, string sort = "Name", bool descending = false, int skip = 0, int take = 200, CancellationToken ct = default) =>
        GetAsync($"api/v1/items/{parentId}/children?{Paging(sort, descending, skip, take)}", Json.PagedResultItemSummaryDto, ct);

    public Task<PagedResult<ItemSummaryDto>> GetRootsAsync(string sort = "Name", bool descending = false, int skip = 0, int take = 200, CancellationToken ct = default) =>
        GetAsync($"api/v1/items/roots?{Paging(sort, descending, skip, take)}", Json.PagedResultItemSummaryDto, ct);

    public Task<PagedResult<ItemSummaryDto>> GetUnsortedAsync(int skip = 0, int take = 200, CancellationToken ct = default) =>
        GetAsync($"api/v1/items/unsorted?skip={skip}&take={take}", Json.PagedResultItemSummaryDto, ct);

    public Task<IReadOnlyList<TreeNodeDto>> GetTreeAsync(Guid? parentId, CancellationToken ct = default) =>
        GetAsync(parentId is { } id ? $"api/v1/tree?parentId={id}" : "api/v1/tree", Json.IReadOnlyListTreeNodeDto, ct);

    public Task<ItemDetailsDto> CreateItemAsync(SaveItemRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/v1/items", request, Json.SaveItemRequest, Json.ItemDetailsDto, ct);

    public Task<ItemDetailsDto> UpdateItemAsync(Guid id, SaveItemRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"api/v1/items/{id}", request, Json.SaveItemRequest, Json.ItemDetailsDto, ct);

    public Task MoveItemsAsync(IReadOnlyList<Guid> itemIds, Guid? targetId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/v1/items/move", new MoveItemsRequest(itemIds, targetId), Json.MoveItemsRequest, ct);

    public Task<ItemDetailsDto> DuplicateItemAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"api/v1/items/{id}/duplicate", Json.ItemDetailsDto, ct);

    public Task DeleteItemAsync(Guid id, DeleteContentsMode? contents = null, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, contents is { } mode ? $"api/v1/items/{id}?contents={mode}" : $"api/v1/items/{id}", ct);

    public Task<IReadOnlyList<ItemDetailsDto>> BatchGetAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/v1/items/batch-get", new BatchGetRequest(ids), Json.BatchGetRequest, Json.IReadOnlyListItemDetailsDto, ct);

    public Task<PagedResult<AuditEntryDto>> GetItemHistoryAsync(Guid id, int skip = 0, int take = 50, CancellationToken ct = default) =>
        GetAsync($"api/v1/items/{id}/history?skip={skip}&take={take}", Json.PagedResultAuditEntryDto, ct);

    public Task<PagedResult<TrashEntryDto>> GetTrashAsync(int skip = 0, int take = 100, CancellationToken ct = default) =>
        GetAsync($"api/v1/trash?skip={skip}&take={take}", Json.PagedResultTrashEntryDto, ct);

    public Task<ItemDetailsDto> RestoreAsync(Guid id, CancellationToken ct = default) => SendAsync(HttpMethod.Post, $"api/v1/trash/{id}/restore", Json.ItemDetailsDto, ct);

    public Task EmptyTrashAsync(CancellationToken ct = default) => SendAsync(HttpMethod.Delete, "api/v1/trash", ct);

    public Task<SyncChangesResponse> GetChangesAsync(long since, int take = 500, CancellationToken ct = default) =>
        GetAsync($"api/v1/sync/changes?since={since.ToString(CultureInfo.InvariantCulture)}&take={take}", Json.SyncChangesResponse, ct);

    // ---------- Фото ----------
    public async Task<IReadOnlyList<MediaDto>> UploadPhotosAsync(Guid itemId, IEnumerable<(string FileName, Stream Content)> files, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        foreach (var (fileName, content) in files)
        {
            var part = new StreamContent(content);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(part, "files", fileName);
        }

        using var response = await http.PostAsync(new Uri($"api/v1/items/{itemId}/media", UriKind.Relative), form, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync(Json.IReadOnlyListMediaDto, ct).ConfigureAwait(false))!;
    }

    public Task<byte[]> GetPhotoAsync(Guid mediaId, PhotoSize size, CancellationToken ct = default) =>
        http.GetByteArrayAsync(new Uri($"api/v1/media/{mediaId}?size={size}", UriKind.Relative), ct);

    public Task SetCoverAsync(Guid itemId, Guid? mediaId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"api/v1/items/{itemId}/cover", new SetCoverRequest(mediaId), Json.SetCoverRequest, ct);

    public Task DeletePhotoAsync(Guid mediaId, CancellationToken ct = default) => SendAsync(HttpMethod.Delete, $"api/v1/media/{mediaId}", ct);

    // ---------- Транспорт ----------
    private static string Paging(string sort, bool descending, int skip, int take) =>
        $"sort={Uri.EscapeDataString(sort)}&desc={(descending ? "true" : "false")}&skip={skip}&take={take}";

    private async Task<T> GetAsync<T>(string url, JsonTypeInfo<T> result, CancellationToken ct)
    {
        using var response = await http.GetAsync(new Uri(url.TrimStart('/'), UriKind.Relative), ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync(result, ct).ConfigureAwait(false))!;
    }

    private async Task<TResult> SendAsync<TBody, TResult>(HttpMethod method, string url, TBody body, JsonTypeInfo<TBody> bodyInfo, JsonTypeInfo<TResult> result, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative)) { Content = JsonContent.Create(body, bodyInfo) };
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync(result, ct).ConfigureAwait(false))!;
    }

    private async Task<TResult> SendAsync<TResult>(HttpMethod method, string url, JsonTypeInfo<TResult> result, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative));
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync(result, ct).ConfigureAwait(false))!;
    }

    private async Task SendAsync<TBody>(HttpMethod method, string url, TBody body, JsonTypeInfo<TBody> bodyInfo, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative)) { Content = JsonContent.Create(body, bodyInfo) };
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    private async Task SendAsync(HttpMethod method, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative));
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        string title = response.ReasonPhrase ?? "Ошибка сервера";
        string? detail = null;
        var errors = new Dictionary<string, string[]>();
        JsonElement? current = null;
        if (text.StartsWith('{'))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;
                title = root.TryGetProperty("title", out var t) ? t.GetString() ?? title : title;
                detail = root.TryGetProperty("detail", out var d) ? d.GetString() : null;
                if (root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object)
                {
                    foreach (var field in e.EnumerateObject())
                    {
                        errors[field.Name] = field.Value.EnumerateArray().Select(v => v.GetString() ?? string.Empty).ToArray();
                    }
                }

                current = root.TryGetProperty("current", out var c) ? c.Clone() : null;
            }
            catch (JsonException)
            {
                detail = text;
            }
        }

        throw new ApiException(response.StatusCode, title, detail, errors, current);
    }
}

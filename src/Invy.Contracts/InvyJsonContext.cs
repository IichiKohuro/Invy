using System.Text.Json;
using System.Text.Json.Serialization;
using Invy.Contracts.Catalog;
using Invy.Contracts.Items;
using Invy.Contracts.Server;
using Invy.Contracts.Sync;
using Invy.Contracts.Updates;

namespace Invy.Contracts;

/// <summary>
/// Source-generated контекст System.Text.Json: сериализация без рефлексии,
/// безопасная для тримминга на Android. Перечисления — строками (атрибуты на самих enum).
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ServerInfoDto))]
[JsonSerializable(typeof(AndroidReleaseManifest))]
[JsonSerializable(typeof(ItemTypeDto))]
[JsonSerializable(typeof(IReadOnlyList<ItemTypeDto>))]
[JsonSerializable(typeof(SaveItemTypeRequest))]
[JsonSerializable(typeof(ValueListDto))]
[JsonSerializable(typeof(IReadOnlyList<ValueListDto>))]
[JsonSerializable(typeof(SaveValueListRequest))]
[JsonSerializable(typeof(AddOptionRequest))]
[JsonSerializable(typeof(ValueListOptionDto))]
[JsonSerializable(typeof(TagDto))]
[JsonSerializable(typeof(IReadOnlyList<TagDto>))]
[JsonSerializable(typeof(SaveTagRequest))]
[JsonSerializable(typeof(MergeTagRequest))]
[JsonSerializable(typeof(ItemDetailsDto))]
[JsonSerializable(typeof(IReadOnlyList<ItemDetailsDto>))]
[JsonSerializable(typeof(PagedResult<ItemSummaryDto>))]
[JsonSerializable(typeof(IReadOnlyList<TreeNodeDto>))]
[JsonSerializable(typeof(SaveItemRequest))]
[JsonSerializable(typeof(MoveItemsRequest))]
[JsonSerializable(typeof(BatchGetRequest))]
[JsonSerializable(typeof(PagedResult<TrashEntryDto>))]
[JsonSerializable(typeof(PagedResult<AuditEntryDto>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(ChangeBatch))]
[JsonSerializable(typeof(SyncChangesResponse))]
public sealed partial class InvyJsonContext : JsonSerializerContext;

using System.Text.Json;
using System.Text.Json.Serialization;
using Invy.Contracts.Server;

namespace Invy.Contracts;

/// <summary>
/// Source-generated контекст System.Text.Json: сериализация без рефлексии,
/// безопасная для тримминга на Android.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ServerInfoDto))]
public sealed partial class InvyJsonContext : JsonSerializerContext;

using System.Text.Json;
using System.Text.Json.Serialization;
using Invy.Contracts.Server;
using Invy.Contracts.Updates;

namespace Invy.Contracts;

/// <summary>
/// Source-generated контекст System.Text.Json: сериализация без рефлексии,
/// безопасная для тримминга на Android.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ServerInfoDto))]
[JsonSerializable(typeof(AndroidReleaseManifest))]
public sealed partial class InvyJsonContext : JsonSerializerContext;

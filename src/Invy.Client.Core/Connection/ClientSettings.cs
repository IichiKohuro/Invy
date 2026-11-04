using System.Text.Json;
using System.Text.Json.Serialization;
using Invy.Client.Core.Shell;

namespace Invy.Client.Core.Connection;

/// <summary>Настройки клиента: сервер, токен устройства (защищённый), тема, недавние типы и места.</summary>
public sealed record ClientSettings
{
    public string? ServerUrl { get; init; }

    public Guid? ServerId { get; init; }

    /// <summary>Токен устройства в защищённом виде (<see cref="ITokenProtector"/>).</summary>
    public string? ProtectedToken { get; init; }

    public ThemeMode Theme { get; init; } = ThemeMode.System;

    public IReadOnlyList<Guid> RecentTypeIds { get; init; } = [];

    public IReadOnlyList<Guid> RecentPlaceIds { get; init; } = [];
}

/// <summary>Шифрование токена на устройстве: Windows — DPAPI, Android — Keystore.</summary>
public interface ITokenProtector
{
    string Protect(string token);

    string? Unprotect(string protectedToken);
}

/// <summary>Без шифрования — для тестов и платформ без защищённого хранилища.</summary>
public sealed class PlainTokenProtector : ITokenProtector
{
    public string Protect(string token) => token;

    public string? Unprotect(string protectedToken) => protectedToken;
}

public interface ISettingsStore
{
    ClientSettings Current { get; }

    void Save(ClientSettings settings);
}

/// <summary>Настройки в JSON-файле (<c>%AppData%\Invy\settings.json</c> на Windows).</summary>
public sealed class JsonFileSettingsStore : ISettingsStore
{
    private readonly string _path;

    public JsonFileSettingsStore(string path)
    {
        _path = path;
        Current = Load(path);
    }

    public ClientSettings Current { get; private set; }

    public void Save(ClientSettings settings)
    {
        Current = settings;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, ClientJsonContext.Default.ClientSettings));
        File.Move(temp, _path, overwrite: true);
    }

    private static ClientSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), ClientJsonContext.Default.ClientSettings) ?? new() : new();
        }
        catch (JsonException)
        {
            return new ClientSettings();   // повреждённый файл — начинаем с чистых настроек
        }
    }
}

/// <summary>Настройки в памяти — для тестов.</summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    public ClientSettings Current { get; private set; } = new();

    public void Save(ClientSettings settings) => Current = settings;
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ClientSettings))]
internal sealed partial class ClientJsonContext : JsonSerializerContext;

/// <summary>Выполнение в UI-потоке: события SignalR приходят из пула потоков.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

/// <summary>Сразу в текущем потоке — для тестов и консольных утилит.</summary>
public sealed class ImmediateDispatcher : IUiDispatcher
{
    public static ImmediateDispatcher Instance { get; } = new();

    public void Post(Action action) => action();
}

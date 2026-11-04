using System.Reflection;
using Microsoft.Extensions.Options;

namespace Invy.Api.Hosting;

/// <summary>
/// Постоянный идентификатор сервера (<c>server-id</c> в каталоге данных):
/// клиенты узнают «свой» сервер, даже если у него сменился IP-адрес.
/// </summary>
public sealed class ServerIdentity
{
    public ServerIdentity(IOptions<InvyServerOptions> options, IHostEnvironment environment)
    {
        var settings = options.Value;
        DataPath = settings.ResolveDataPath(environment.ContentRootPath);
        Name = string.IsNullOrWhiteSpace(settings.Name) ? Environment.MachineName : settings.Name;
        ServerId = LoadOrCreateId(Path.Combine(DataPath, "server-id"));
    }

    public Guid ServerId { get; }

    public string Name { get; }

    public string DataPath { get; }

    public static string Version { get; } =
        typeof(ServerIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    private static Guid LoadOrCreateId(string path)
    {
        if (File.Exists(path) && Guid.TryParse(File.ReadAllText(path).Trim(), out var existing))
        {
            return existing;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var id = Guid.CreateVersion7();
        File.WriteAllText(path, id.ToString());
        return id;
    }
}

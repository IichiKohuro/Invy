namespace Invy.Api.Hosting;

/// <summary>Секция <c>Invy</c> в appsettings.json.</summary>
public sealed class InvyServerOptions
{
    public const string SectionName = "Invy";

    /// <summary>Каталог данных. Пусто — <c>%ProgramData%\Invy</c> (Windows) или <c>~/.local/share/invy</c>.</summary>
    public string? DataPath { get; set; }

    /// <summary>Имя сервера в сети. Пусто — имя компьютера.</summary>
    public string? Name { get; set; }

    public string ResolveDataPath(string contentRoot)
    {
        if (!string.IsNullOrWhiteSpace(DataPath))
        {
            return Path.GetFullPath(DataPath, contentRoot);
        }

        var root = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, OperatingSystem.IsWindows() ? "Invy" : "invy");
    }
}

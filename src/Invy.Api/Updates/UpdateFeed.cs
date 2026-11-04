using Invy.Contracts;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Invy.Api.Updates;

/// <summary>
/// Раздача релизов клиентам в локальной сети: <c>/updates/win/*</c> — фид Velopack
/// (<c>releases.win.json</c> + <c>*.nupkg</c>, Setup.exe), <c>/updates/android/*</c> — APK и манифест.
/// Файлы кладёт <c>tools/release.ps1</c> в <c>{DataPath}/releases/{платформа}</c>.
/// </summary>
internal static class UpdateFeed
{
    public const string ReleasesFolder = "releases";

    private static readonly FileExtensionContentTypeProvider ContentTypes = new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".json"] = "application/json",
        [".nupkg"] = "application/octet-stream",
        [".exe"] = "application/vnd.microsoft.portable-executable",
        [".zip"] = "application/zip",
        [".apk"] = "application/vnd.android.package-archive",
        [".md"] = "text/markdown; charset=utf-8",
    });

    public static WebApplication UseUpdateFeed(this WebApplication app, string dataPath)
    {
        var releasesPath = Path.Combine(dataPath, ReleasesFolder);
        Directory.CreateDirectory(releasesPath);

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(releasesPath),
            RequestPath = ApiRoutes.Updates,
            ContentTypeProvider = ContentTypes,
            ServeUnknownFileTypes = false,
            OnPrepareResponse = context =>
                context.Context.Response.Headers.CacheControl = IsImmutable(context.File.Name) ? "public, max-age=31536000, immutable" : "no-cache",
        });

        return app;
    }

    /// <summary>
    /// Неизменяемы только пакеты с версией в имени (<c>Invy.Desktop-1.4.0-delta.nupkg</c>, <c>invy-1.4.0.apk</c>).
    /// Манифесты, <c>…-Setup.exe</c> и <c>…-Portable.zip</c> меняются с каждым выпуском под тем же именем.
    /// </summary>
    internal static bool IsImmutable(string fileName) =>
        fileName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase);
}

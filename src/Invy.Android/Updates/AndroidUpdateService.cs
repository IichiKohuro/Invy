using System.Net.Http.Json;
using System.Security.Cryptography;
using Android.Content;
using Android.Content.PM;
using Invy.Client.Core.Updates;
using Invy.Contracts;
using Invy.Contracts.Updates;

namespace Invy.Android.Updates;

/// <summary>
/// Самообновление Android-клиента из локальной сети: <c>{сервер}/updates/android/latest.json</c> →
/// загрузка APK в кэш приложения → проверка SHA-256 → установка через <see cref="ApkInstaller"/>.
/// Подпись APK проверяет сам Android: обновление с другим ключом будет отклонено системой.
/// </summary>
internal sealed class AndroidUpdateService(Context context) : IAppUpdater
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private string? _downloadedApk;

    public string CurrentVersion => PackageInfo().VersionName ?? "0.0.0";

    public bool IsSupported => true;

    public string? UnsupportedReason => null;

    public async Task<string?> CheckAndDownloadAsync(Uri serverUrl, CancellationToken cancellationToken = default)
    {
        var feed = new Uri(serverUrl, $"{ApiRoutes.Updates}/android/");
        var manifest = await Http.GetFromJsonAsync(new Uri(feed, "latest.json"), InvyJsonContext.Default.AndroidReleaseManifest, cancellationToken)
            .ConfigureAwait(false);
        if (manifest is null || manifest.VersionCode <= InstalledVersionCode())
        {
            return null;
        }

        var directory = Directory.CreateDirectory(Path.Combine(context.CacheDir!.AbsolutePath, "updates")).FullName;
        var apkPath = Path.Combine(directory, Path.GetFileName(manifest.ApkFileName));

        await using (var response = await Http.GetStreamAsync(new Uri(feed, Uri.EscapeDataString(manifest.ApkFileName)), cancellationToken).ConfigureAwait(false))
        await using (var file = File.Create(apkPath))
        {
            await response.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        await using (var file = File.OpenRead(apkPath))
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
            if (!string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(apkPath);
                throw new InvalidDataException("Загруженный APK повреждён: контрольная сумма не совпадает.");
            }
        }

        _downloadedApk = apkPath;
        return manifest.VersionName;
    }

    public void Install()
    {
        if (_downloadedApk is null)
        {
            return;
        }

        if (!ApkInstaller.CanRequestInstalls(context))
        {
            // Один раз: пользователь разрешает Invy устанавливать обновления, затем нажимает «Установить» ещё раз.
            ApkInstaller.OpenInstallPermissionSettings(context);
            return;
        }

        ApkInstaller.Install(context, _downloadedApk);
    }

    private long InstalledVersionCode()
    {
        var info = PackageInfo();
        return OperatingSystem.IsAndroidVersionAtLeast(28) ? info.LongVersionCode : LegacyVersionCode(info);
    }

#pragma warning disable CA1422 // VersionCode устарел начиная с API 28; ветка вызывается только на Android 8.x
    private static long LegacyVersionCode(PackageInfo info) => info.VersionCode;
#pragma warning restore CA1422

    private PackageInfo PackageInfo()
    {
        var packageManager = context.PackageManager!;
        var packageName = context.PackageName!;
        return OperatingSystem.IsAndroidVersionAtLeast(33)
            ? packageManager.GetPackageInfo(packageName, PackageManager.PackageInfoFlags.Of(0L))!
            : LegacyPackageInfo(packageManager, packageName);
    }

#pragma warning disable CA1422 // перегрузка с int-флагами устарела начиная с API 33
    private static PackageInfo LegacyPackageInfo(PackageManager packageManager, string packageName) =>
        packageManager.GetPackageInfo(packageName, 0)!;
#pragma warning restore CA1422
}

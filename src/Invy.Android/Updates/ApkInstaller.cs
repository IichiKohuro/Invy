using Android.App;
using Android.Content;
using Android.Content.PM;
using AndroidUri = Android.Net.Uri;
using AndroidSettings = Android.Provider.Settings;

namespace Invy.Android.Updates;

/// <summary>
/// Установка APK через <see cref="PackageInstaller"/> (сессия), без FileProvider и ACTION_VIEW.
/// Android 12+: если Invy — installer of record (после первого обновления через само приложение),
/// система ставит обновление без диалога подтверждения; иначе показывает обычное подтверждение.
/// </summary>
internal static class ApkInstaller
{
    public static bool CanRequestInstalls(Context context) => context.PackageManager!.CanRequestPackageInstalls();

    /// <summary>Системный экран «Установка неизвестных приложений» для Invy.</summary>
    public static void OpenInstallPermissionSettings(Context context)
    {
        var intent = new Intent(AndroidSettings.ActionManageUnknownAppSources, AndroidUri.Parse($"package:{context.PackageName}"));
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    public static void Install(Context context, string apkPath)
    {
        var installer = context.PackageManager!.PackageInstaller;
        var parameters = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
        parameters.SetAppPackageName(context.PackageName);
        parameters.SetSize(new FileInfo(apkPath).Length);

        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            // Привязка принимает int, а константы объявлены перечислением PackageInstallUserAction.
            parameters.SetRequireUserAction((int)PackageInstallUserAction.NotRequired);
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            parameters.SetRequestUpdateOwnership(true);
        }

        var sessionId = installer.CreateSession(parameters);
        using var session = installer.OpenSession(sessionId);

        using (var input = File.OpenRead(apkPath))
        using (var output = session.OpenWrite("invy.apk", 0, input.Length))
        {
            input.CopyTo(output);
            session.Fsync(output);
        }

        // Результат установки придёт в InstallStatusReceiver. Intent явный (Android 14 требует этого для mutable PendingIntent),
        // FLAG_MUTABLE — система дописывает в него статус установки.
        var callback = new Intent(context, typeof(InstallStatusReceiver));
        var flags = PendingIntentFlags.UpdateCurrent | (OperatingSystem.IsAndroidVersionAtLeast(31) ? PendingIntentFlags.Mutable : 0);
        var pending = PendingIntent.GetBroadcast(context, sessionId, callback, flags)!;
        session.Commit(pending.IntentSender!);
    }
}

using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Util;

namespace Invy.Android.Updates;

/// <summary>Принимает статус сессии <see cref="PackageInstaller"/>; при необходимости показывает системное подтверждение.</summary>
[BroadcastReceiver(Exported = false)]
public sealed class InstallStatusReceiver : BroadcastReceiver
{
    private const string LogTag = "Invy.Update";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null)
        {
            return;
        }

        var status = (PackageInstallStatus)intent.GetIntExtra(PackageInstaller.ExtraStatus, (int)PackageInstallStatus.Failure);
        if (status == PackageInstallStatus.PendingUserAction)
        {
            // Системе нужно подтверждение пользователя — показываем её диалог установки.
            if (ConfirmationIntent(intent) is { } confirm)
            {
                confirm.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(confirm);
            }

            return;
        }

        var message = intent.GetStringExtra(PackageInstaller.ExtraStatusMessage);
        Log.Info(LogTag, $"Install finished: {status} {message}");
    }

    private static Intent? ConfirmationIntent(Intent intent)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return (Intent?)intent.GetParcelableExtra(Intent.ExtraIntent, Java.Lang.Class.FromType(typeof(Intent)));
        }

#pragma warning disable CA1422 // GetParcelableExtra(string) устарел начиная с API 33; здесь — только для Android 8–12
        return (Intent?)intent.GetParcelableExtra(Intent.ExtraIntent);
#pragma warning restore CA1422
    }
}

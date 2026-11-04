using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Invy.Client.UI;

namespace Invy.Android;

[Application]
public sealed class MainApplication : AvaloniaAndroidApplication<App>
{
    public MainApplication(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    // App создаётся с Android-хостом: он показывает главный вид и даёт самообновление через APK.
    protected override AppBuilder CreateAppBuilder() =>
        AppBuilder.Configure(() => new App(new AndroidHost(this))).UseAndroid();

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();
}

using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace Invy.Android;

[Activity(
    Label = "Invy",
    Theme = "@style/InvyTheme",
    Icon = "@drawable/icon",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity;

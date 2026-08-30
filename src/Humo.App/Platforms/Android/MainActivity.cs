using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Humo.App;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize
        | ConfigChanges.Orientation
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density
        | ConfigChanges.LayoutDirection
        | ConfigChanges.Locale)]
public class MainActivity : MauiAppCompatActivity
{
    // Locale is listed in ConfigurationChanges so a device language change does
    // not restart the activity mid-cook.
}

using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;

namespace scada_demo_test.Maui;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize
        | ConfigChanges.Orientation
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Log.Info("MAUI_DIAG", "MainActivity.OnCreate: enter");
        try
        {
            base.OnCreate(savedInstanceState);
            Log.Info("MAUI_DIAG", "MainActivity.OnCreate: base done");
        }
        catch (Exception ex)
        {
            Log.Error("MAUI_DIAG", "MainActivity.OnCreate FAILED: " + ex);
            throw;
        }
    }
}
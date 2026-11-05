using Android.App;
using Android.Content.PM;
using Android.OS;

namespace CarKit.Sample.Navigation;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    // Stage 9: a run without touch (screenshots in the ledger): adb shell am start … --es carkit.autorun stop-route → an
    // environment variable read by MainPage (like SIMCTL_CHILD_CARKIT_SAMPLE_AUTORUN on iOS)
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        if (Intent?.GetStringExtra("carkit.autorun") is { } autorun)
        {
            System.Environment.SetEnvironmentVariable("CARKIT_SAMPLE_AUTORUN", autorun);
        }

        base.OnCreate(savedInstanceState);
    }
}

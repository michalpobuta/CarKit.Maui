using Android.App;
using Android.Content.PM;
using Android.OS;

namespace CarKit.Maui.DeviceTests;

[Activity(Name = "carkit.maui.devicetests.MainActivity", Theme = "@style/Maui.SplashTheme", MainLauncher = true, Exported = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Stage 10: test filter and repetitions (eng/run-device-tests.sh: am start --es) as environment variables for the
        // runner
        foreach (var name in new[] { "CARKIT_DEVICE_TESTS_FILTER", "CARKIT_DEVICE_TESTS_REPEAT" })
        {
            if (Intent?.GetStringExtra(name) is { Length: > 0 } value)
            {
                System.Environment.SetEnvironmentVariable(name, value);
            }
        }

        base.OnCreate(savedInstanceState);
    }
}

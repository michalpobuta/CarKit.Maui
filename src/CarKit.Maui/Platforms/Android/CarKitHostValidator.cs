using Android.Content;
using Android.Content.PM;
using AndroidX.Car.App.Validation;

namespace CarKit.Maui;

// Debug (app with the Debuggable flag): all hosts, so the DHU works. Release: car.Android.AllowedHosts, or the
// carkit_allowed_hosts resource when the list is empty. The NuGet package is always Release, so #if DEBUG does not work.
internal static class CarKitHostValidator
{
    public static HostValidator Create(Context context, CarKitAndroidOptions options)
    {
        if (context.ApplicationInfo is { } info && (info.Flags & ApplicationInfoFlags.Debuggable) != 0)
        {
            return HostValidator.AllowAllHostsValidator!;
        }

        var builder = new HostValidator.Builder(context);
        if (options.AllowedHosts.Count == 0)
        {
            return builder.AddAllowedHosts(Resource.Array.carkit_allowed_hosts)!.Build()!;
        }

        foreach (var entry in options.AllowedHosts)
        {
            var (digest, package) = CarKitAndroidOptions.ParseAllowedHost(entry);
            builder.AddAllowedHost(package, digest);
        }

        return builder.Build()!;
    }
}

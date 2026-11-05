using AndroidX.Car.App.Model;
using AndroidX.Core.Graphics.Drawable;
using CarKit.Maui;
using NativeAction = AndroidX.Car.App.Model.Action;
using NativeCarColor = AndroidX.Car.App.Model.CarColor;

namespace CarKit.Sample.Car;

// Android Auto escape hatch (Stage 4): the list's floating button (ListTemplate.Builder.AddAction since Car App API 6,
// SectionedItemTemplate.Builder.AddAction since API 8) does not exist in the CarKit model. The list has two builders
// depending on the host, so ConfigureAndroidAuto and ConfigureAndroidAutoSectioned both add the same button (the one
// the host uses takes effect).
public sealed partial class SampleScreens
{
    private const int FabApiLevel = 6;

    partial void ConfigurePlatform(ref CarListTemplate parkings)
    {
        if (session.Capabilities.CarAppApiLevel < FabApiLevel)
        {
            return;
        }

        parkings = parkings
            .ConfigureAndroidAuto(builder => builder.AddAction(SearchButton()))
            .ConfigureAndroidAutoSectioned(builder => builder.AddAction(SearchButton()));
    }

    // ACTIONS_CONSTRAINTS_FAB: an icon and a background color are required
    private NativeAction SearchButton()
    {
        var context = Android.App.Application.Context;
        return new NativeAction.Builder()
            .SetIcon(new CarIcon.Builder(IconCompat.CreateWithResource(context, Resource.Drawable.ic_carkit_search)!).Build()!)!
            .SetBackgroundColor(NativeCarColor.Primary!)!
            .SetOnClickListener(new Click(PushSearchAsync))!
            .Build()!;
    }

    // A click on the button from the hook does not go through CarKit (no ICarErrorHandler), so the sample logs errors
    private sealed class Click(Func<Task> open) : Java.Lang.Object, IOnClickListener
    {
        public void OnClick() => OpenAsync();

        private async void OpenAsync()
        {
            try
            {
                await open();
            }
            catch (CarKitException ex)
            {
                SampleDiagnostics.Log(ex.Message);
            }
        }
    }
}

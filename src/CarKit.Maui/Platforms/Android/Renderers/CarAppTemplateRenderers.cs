using Android.Content;
using AndroidX.Car.App.Model;
using CarKitText = CarKit.Maui.CarText;
using NativeAction = AndroidX.Car.App.Model.Action;
using NativeCarText = AndroidX.Car.App.Model.CarText;

namespace CarKit.Maui.Renderers;

// The template for the host and its shape (what the host compares when classifying refreshes), built from the same data
internal sealed record CarAppRendered(ITemplate Template, CarAppTemplateShape Shape);

// Model → Car App Library template. The ListTemplate/SectionedItemTemplate choice is fixed per connection (the same
// type for the host on every rebuild). ConfigureAndroidAuto hooks before Build() in every renderer (Stage 4). Module
// templates (Stage 5: Now Playing → MediaPlaybackTemplate) are rendered by the module renderers from the context;
// without a renderer (map: Stage 8) → NotSupportedException (session: CarPlatformException). POI (Stage 7): a module
// renderer (Stage 8: app map) takes precedence, otherwise PlaceListMapTemplate or a list of places.
internal static class CarAppTemplateRenderers
{
    public static CarAppRendered Render(CarTemplate template, CarAppRenderContext context, CarAppScreenState screen) => template switch
    {
        CarListTemplate list when context.UseSectionedItemTemplate => SectionedListRenderer.Render(list, context, screen),
        CarListTemplate list => ListTemplateRenderer.Render(list, context, screen),
        CarGridTemplate grid => GridTemplateRenderer.Render(grid, context, screen),
        CarTabTemplate tabs => TabTemplateRenderer.Render(tabs, context, screen),
        CarInformationTemplate information => PaneTemplateRenderer.Render(information, context, screen),
        CarSearchTemplate search => SearchTemplateRenderer.Render(search, context, screen),
        CarMessageTemplate message => MessageTemplateRenderer.Render(message, context, screen),
        CarLongMessageTemplate longMessage => LongMessageTemplateRenderer.Render(longMessage, context, screen),
        AndroidAutoSignInTemplate signIn => SignInTemplateRenderer.Render(signIn, context, screen),
        CarPointOfInterestTemplate points => context.Module(points)?.Render(points, context, screen) ?? PlaceListMapRenderer.Render(points, context, screen),
        _ => context.Module(template)?.Render(template, context, screen)
            ?? throw new NotSupportedException($"CarKit does not render {template.Kind} on Android Auto."),
    };

    // The main screen before SetRootAsync from OnConnectedAsync; the list type of this connection, so a list root is a
    // refresh
    public static CarAppRendered Loading(Context context, int apiLevel, bool useSectionedItemTemplate)
    {
        var title = context.ApplicationInfo?.LoadLabel(context.PackageManager!) ?? "CarKit";
        if (useSectionedItemTemplate)
        {
            var sectioned = new SectionedItemTemplate.Builder()
                .SetHeader(new Header.Builder().SetTitle(title)!.SetStartHeaderAction(NativeAction.AppIcon!)!.Build()!)!
                .SetLoading(true)!;
            return new(sectioned.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.Sectioned, title, IsLoading: true));
        }

        var builder = new ListTemplate.Builder().SetLoading(true)!;
        if (apiLevel >= HeaderRenderer.HeaderApiLevel)
        {
            builder.SetHeader(new Header.Builder().SetTitle(title)!.SetStartHeaderAction(NativeAction.AppIcon!)!.Build()!);
        }
        else
        {
#pragma warning disable CS0618 // Header requires Car App API 7; this is the path for older hosts
            builder.SetTitle(title)!.SetHeaderAction(NativeAction.AppIcon!);
#pragma warning restore CS0618
        }

        return new(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.List, title, IsLoading: true));
    }

    public static NativeCarText Text(CarKitText text)
    {
        var builder = new NativeCarText.Builder(text.Text);
        foreach (var variant in text.Variants.Skip(1))
        {
            builder.AddVariant(variant);
        }

        return builder.Build()!;
    }
}

namespace CarKit.Maui.Renderers;

// The registration point for module renderers (Stage 5: Now Playing in CarKit.Maui.Media). A module registers an
// implementation in DI, CarPlayHost passes them to the render context, and CarPlayTemplateRenderers asks them before
// NotSupportedException. Called on the main thread; after Render/UpdateInPlace CarPlayTemplateRenderers itself sets
// UserInfo, the theme and the hooks.
internal interface ICarPlayModuleRenderer
{
    bool CanRender(CarTemplate template);

    CarPlayTemplateState Render(CarTemplate template, CarPlayRenderContext context);

    // false = CarPlay has no API for an in-place change, the host replaces the template
    bool UpdateInPlace(CarPlayTemplateState state, CarTemplate updated, CarPlayRenderContext context);
}

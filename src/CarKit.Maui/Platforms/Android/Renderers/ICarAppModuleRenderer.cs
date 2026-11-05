namespace CarKit.Maui.Renderers;

// The registration point for Car App Library module renderers (Stage 5: MediaPlaybackTemplate in CarKit.Maui.Media;
// Stage 8: map, route preview and POI with the app map in CarKit.Maui.Navigation). A module registers its
// implementation in DI, CarAppLibraryHost passes them to the render context, and CarAppTemplateRenderers asks them
// before NotSupportedException (POI: before the built-in renderer). A context = one connection, so the module choice
// may depend on the host (fixed per connection). Main thread only.
internal interface ICarAppModuleRenderer
{
    bool CanRender(CarTemplate template, CarAppRenderContext context);

    CarAppRendered Render(CarTemplate template, CarAppRenderContext context, CarAppScreenState screen);

    // A connection to the host (e.g. registering the media session token in MediaPlaybackManager); carContext =
    // AndroidX.Car.App.CarContext
    void OnConnected(CarAppRenderContext context, Java.Lang.Object carContext);

    // Stage 8: the template type that Render will build (task steps are counted before the render); null =
    // CarAppTemplateTypes.Of
    string? TemplateType(CarTemplate template, CarAppRenderContext context) => null;

    // Stage 8: the end of the connection (releasing the surface, detaching from the navigation session)
    void OnDisconnected(CarAppRenderContext context)
    {
    }

    // Stage 8: the host's day/night mode after the connection (onCarConfigurationChanged), e.g. the CarScreen map surface
    void OnContentStyleChanged(CarAppRenderContext context, CarContentStyle style)
    {
    }

    // Stage 8: a session intent (OnCreateScreen, OnNewIntent), e.g. CarContext.ACTION_NAVIGATE for the Navigation module
    void OnIntent(CarAppRenderContext context, Android.Content.Intent intent)
    {
    }
}

// Stage 8: a Car App Library cluster session (SessionInfo.DISPLAY_TYPE_CLUSTER, category FEATURE_CLUSTER) from the
// Navigation module
internal interface ICarAppClusterSessionFactory
{
    AndroidX.Car.App.Session CreateClusterSession();
}

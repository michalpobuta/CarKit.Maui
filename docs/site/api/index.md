# API reference

The reference is generated from the XML documentation of the CarKit 0.1 packages:

- **[Core and Testing](core/toc.yml)** (`net10.0`): the template model, the session, the navigation session model,
  validation, `CarFeature` and `FakeCarHost`. Most of the API is here, in the `CarKit.Maui` and `CarKit.Maui.Testing`
  namespaces.
- **[Platform packages](ios/toc.yml)**: `CarKit.Maui`, `CarKit.Maui.Media`, `CarKit.Maui.Communication` and
  `CarKit.Maui.Navigation` as built for iOS 26.4 (`net10.0-ios26.4`). Types that exist on both platforms, such as
  `CarKitBuilder` or `ICarMapSurface`, are documented from this build; the iOS 26.0 build has the same API.
- **[Android-only types](android/toc.yml)** (`net10.0-android`): `CarKitAppServiceBase`, the `ConfigureAndroidAuto`
  extensions, `CarKitMediaLibraryService` and `CarKitMessageActionReceiver`. The one Android-only member of a shared
  type, `ICarMapSurface.UseView(Func<Context, View>)`, is described in [Navigation](../articles/navigation.md#map-surfaces).

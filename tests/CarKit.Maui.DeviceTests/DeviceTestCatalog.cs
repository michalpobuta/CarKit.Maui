namespace CarKit.Maui.DeviceTests;

// All device tests; order = execution order
internal static class DeviceTestCatalog
{
    public static IReadOnlyList<DeviceTest> All() =>
#if IOS
        [.. CarPlayRendererTests.All(), .. MediaBridgeTests.All(), .. CommunicationTests.All(), .. PointOfInterestTests.All(), .. NavigationTests.All(), .. PlatformFeatureTests.All()];
#elif ANDROID
        [.. CarAppRendererTests.All(), .. MediaSessionTests.All(), .. CommunicationTests.All(), .. PointOfInterestTests.All(), .. NavigationTests.All(), .. PlatformFeatureTests.All()];
#else
        [];
#endif
}

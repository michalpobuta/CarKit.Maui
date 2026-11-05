using CarPlay;
using ObjCRuntime;

namespace CarKit.Maui.DeviceTests;

// Stage 10: a list row from a native object. Items2 has a protocol type, so when the GC has collected the peer (renderer
// state without references, in Release right after the last use of the local), the binding gives
// CPListTemplateItemWrapper. Runtime.GetNSObject takes the type from the Objective-C class, so the type check stays and
// the result does not depend on the GC.
internal static class NativeListItems
{
    public static T Item<T>(this CPListSection section, int index) where T : class =>
        Check.Is<T>(Runtime.GetNSObject(((INativeObject)section.Items2[index]).Handle));

    public static IEnumerable<T> AllItems<T>(this CPListSection section) where T : class =>
        Enumerable.Range(0, section.Items2.Length).Select(section.Item<T>);
}

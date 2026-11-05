using CarPlay;

namespace CarKit.Maui.Renderers;

// Content limits read from CarPlay at runtime (spec: "Content limits read at runtime, not hard-coded"); null → the
// default from the rules
internal static class CarPlayLimits
{
    public static int? Read(CarLimitKind kind) => kind switch
    {
        CarLimitKind.ListItems => (int)CPListTemplate.MaximumItemCount,
        CarLimitKind.ListSections => (int)CPListTemplate.MaximumSectionCount,
        CarLimitKind.GridItems => (int)CPGridTemplate.MaximumItems,
        CarLimitKind.Tabs => (int)CPTabBarTemplate.MaximumTabCount,
        // Stage 10: depends on the entitlement (Audio 1, other categories 3); applies to the alert only (validator:
        // AlertActionLimit)
        CarLimitKind.Actions => (int)CPAlertTemplate.MaximumActionCount,
        _ => null,
    };
}

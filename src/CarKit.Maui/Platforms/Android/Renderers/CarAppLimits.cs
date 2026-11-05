using AndroidX.Car.App;
using AndroidX.Car.App.Constraints;
using Android.Runtime;

namespace CarKit.Maui.Renderers;

// Content limits from the host (ConstraintManager, Car App API ≥ 2; spec: "Content limits read at runtime, not
// hard-coded"); null → the default from the rules
internal static class CarAppLimits
{
    private const int ConstraintManagerApiLevel = 2;

    public static int? Read(CarContext? carContext, CarLimitKind kind)
    {
        if (carContext is null || carContext.CarAppApiLevel < ConstraintManagerApiLevel)
        {
            return null;
        }

        int? type = kind switch
        {
            CarLimitKind.ListItems => ConstraintManager.ContentLimitTypeList,
            CarLimitKind.GridItems => ConstraintManager.ContentLimitTypeGrid,
            CarLimitKind.InformationItems => ConstraintManager.ContentLimitTypePane,
            CarLimitKind.PointsOfInterest => ConstraintManager.ContentLimitTypePlaceList,
            _ => null,
        };
        if (type is not { } limitType)
        {
            return null;
        }

        var manager = carContext.GetCarService(CarContext.ConstraintService)!.JavaCast<ConstraintManager>()!;
        return manager.GetContentLimit(limitType);
    }
}

namespace CarKit.Maui.Renderers;

// CarDistance → Car App Library Distance (Stage 7): constants Distance.UNIT_* (javadoc: METERS 1, KILOMETERS 2,
// KILOMETERS_P1 3, MILES 4, MILES_P1 5, FEET 6, YARDS 7); kilometers and miles below 10 with a fractional part show
// one decimal place
internal static class CarDistanceUnits
{
    private const double OneDecimalBelow = 10;

    public static (double Value, int Unit) AndroidAuto(CarDistance distance) => distance.Unit switch
    {
        CarDistanceUnit.Meters => (distance.Value, 1),
        CarDistanceUnit.Kilometers => (distance.Value, HasDecimals(distance.Value) ? 3 : 2),
        CarDistanceUnit.Miles => (distance.Value, HasDecimals(distance.Value) ? 5 : 4),
        CarDistanceUnit.Feet => (distance.Value, 6),
        CarDistanceUnit.Yards => (distance.Value, 7),
        _ => throw new ArgumentOutOfRangeException(nameof(distance), distance.Unit, "Unknown distance unit."),
    };

    private static bool HasDecimals(double value) => value < OneDecimalBelow && value != Math.Floor(value);
}

using CarKit.Maui;

namespace LibPlain;

// A consumer library on net10.0-ios (TPV 26.0) gets the 26.0 asset and still sees the Core model (CarFeature gate at runtime)
public static class Screens
{
    public static CarListTemplate Parkings() => new("Parking lots")
    {
        DetailsHeader = new CarListDetailsHeader(CarImage.FromSystemSymbol("parkingsign", null)) { Title = "Downtown" },
    };
}

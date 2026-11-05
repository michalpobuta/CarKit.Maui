using CarKit.Maui;
using CarPlay;

namespace CarKit.Sample.Car;

// CarPlay escape hatch (Stage 4): the bar button style (CPBarButton.ButtonStyle) does not exist in the CarKit model.
// The hook runs after every render and grid update, so new CPBarButtons after "Change title" are rounded too.
public sealed partial class SampleScreens
{
    static partial void ConfigurePlatform(ref CarGridTemplate grid) =>
        grid = grid.ConfigureCarPlay(native =>
        {
            foreach (var button in native.TrailingNavigationBarButtons)
            {
                button.ButtonStyle = CPBarButtonStyle.Rounded;
            }
        });
}

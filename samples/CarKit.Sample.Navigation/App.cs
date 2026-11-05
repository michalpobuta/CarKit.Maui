namespace CarKit.Sample.Navigation;

public sealed class App(MainPage page) : Application
{
    protected override Window CreateWindow(IActivationState? activationState) => new(page) { Title = "CarKit Navigation" };
}

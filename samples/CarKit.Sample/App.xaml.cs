using Microsoft.Extensions.DependencyInjection;

namespace CarKit.Sample;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}

	// The CarKit scene manifest changes the MAUI lifecycle on iOS; the sample shows that these events still arrive (deep
	// links: MauiProgram)
	protected override void OnSleep() => SampleDiagnostics.Sleep();

	protected override void OnResume() => SampleDiagnostics.Resume();
}
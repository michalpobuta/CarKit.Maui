using Microsoft.Extensions.DependencyInjection;
using CarKit.Maui;

namespace CarKit.Sample;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        SampleDiagnostics.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(Refresh);
        Refresh();
    }

    private void Refresh()
    {
        var options = IPlatformApplication.Current?.Services.GetService<CarKitOptions>();
        CategoriesLabel.Text = $"Categories (.csproj): {SampleDiagnostics.ProjectCategories} → {options?.Categories}";
        ValidationLabel.Text = $"Validation: {options?.Validation}";
        CarLabel.Text = $"Car: {SampleDiagnostics.CarState}";
        LifecycleLabel.Text = $"OnSleep: {SampleDiagnostics.SleepCount}, OnResume: {SampleDiagnostics.ResumeCount}";
        AppLinkLabel.Text = $"Deep link: {SampleDiagnostics.LastAppLink ?? "—"}";
        CarEventLabel.Text = $"Car event: {SampleDiagnostics.LastCarEvent ?? "—"}";
    }
}

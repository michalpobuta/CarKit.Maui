namespace CarKit.Maui.DeviceTests;

// Window with the result; the tests start after the window is created, on the main thread, and the process ends
// with exit code = number of failures
public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var status = new Label { Text = "CarKit device tests…", Margin = new Thickness(24), FontSize = 18 };
        var window = new Window(new ContentPage { Content = status });
        window.Created += (_, _) => MainThread.BeginInvokeOnMainThread(async () =>
        {
            var failed = await DeviceTestRunner.RunAsync(DeviceTestRunner.Select(DeviceTestCatalog.All()), line => status.Text += "\n" + line);
            await Task.Delay(500);
            DeviceTestRunner.Exit(failed);
        });
        return window;
    }
}

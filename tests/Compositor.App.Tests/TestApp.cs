using Avalonia;
using Avalonia.Headless;
using Compositor.App;

// Attribute lives in Avalonia.Headless (base), discovered by HeadlessUnitTestSession.
[assembly: AvaloniaTestApplication(typeof(Compositor.App.Tests.TestApp))]

namespace Compositor.App.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

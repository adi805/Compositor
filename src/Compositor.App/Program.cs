using Avalonia;
using Compositor.Core;
using Compositor.Core.Project;
using Avalonia.Controls.ApplicationLifetimes;

namespace Compositor.App;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Contains("--ui"))
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }

        // Headless smoke entry (CI): exercise the document model and project I/O
        // without opening a window.
        var doc = new Document(64, 64);
        doc.AddLayer(new Layer("Background"));
        var path = Path.Combine(Path.GetTempPath(), $"compositor-smoke-{Guid.NewGuid():N}.comp");
        ProjectStore.Save(doc, path);
        var reloaded = ProjectStore.Load(path);
        File.Delete(path);
        Console.WriteLine(
            $"Compositor.Windows pre-alpha: doc {reloaded.Width}x{reloaded.Height}, " +
            $"layers={reloaded.Layers.Count}, round-trip OK");
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}

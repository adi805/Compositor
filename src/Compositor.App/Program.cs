using Compositor.Core;

namespace Compositor.App;

public static class Program
{
    public static int Main(string[] args)
    {
        // Phase 0: headless smoke entry point. The Avalonia shell lands in
        // phase 3 (see docs/ROADMAP.md).
        var doc = new Document(64, 64);
        doc.AddLayer(new Layer("Background"));
        Console.WriteLine($"Compositor.Windows pre-alpha: doc {doc.Width}x{doc.Height}, layers={doc.Layers.Count}");
        return 0;
    }
}

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>
/// Real control tests on the Avalonia headless platform: window opens,
/// layers panel reflects the document, and panel buttons mutate it.
/// </summary>
public sealed class HeadlessWindowTests
{
    [AvaloniaFact]
    public void Window_ShowsRows_MatchingDocument()
    {
        var doc = new Compositor.Core.Document(400, 300);
        doc.AddLayer(new Compositor.Core.Layer("bottom"));
        doc.AddLayer(new Compositor.Core.Layer("top"));
        var window = new MainWindow(new EditorViewModel(doc));

        window.Show();

        var list = window.FindControl<ListBox>("LayerList")!;
        Assert.Equal(2, list.ItemCount);
    }

    [AvaloniaFact]
    public void AddButton_InsertsRowAtTop_AndUpdatesSelection()
    {
        var window = new MainWindow(new EditorViewModel(new Compositor.Core.Document(400, 300)));
        window.Show();
        var list = window.FindControl<ListBox>("LayerList")!;
        var before = list.ItemCount;

        Click(window, "AddLayerButton");

        Assert.Equal(before + 1, list.ItemCount);
        Assert.NotNull(window.Editor!.Doc.ActiveLayerId);
    }

    [AvaloniaFact]
    public void DeleteButton_RemovesSelectedRow()
    {
        var doc = new Compositor.Core.Document(400, 300);
        doc.AddLayer(new Compositor.Core.Layer("only"));
        var window = new MainWindow(new EditorViewModel(doc));
        window.Show();

        Click(window, "DeleteLayerButton");

        Assert.Empty(window.Editor!.Doc.Layers);
        Assert.Equal(0, window.FindControl<ListBox>("LayerList")!.ItemCount);
    }

    [AvaloniaFact]
    public void MoveButtons_ReorderDocumentStack()
    {
        var doc = new Compositor.Core.Document(400, 300);
        doc.AddLayer(new Compositor.Core.Layer("A"));
        doc.AddLayer(new Compositor.Core.Layer("B"));
        var window = new MainWindow(new EditorViewModel(doc));
        window.Show();
        var vm = window.Editor!;

        vm.Selected = vm.Rows[0]; // B (top row in UI)
        Click(window, "MoveDownButton");

        Assert.Equal(["B", "A"], vm.Doc.Layers.Select(l => l.Name));
    }

    private static void Click(MainWindow window, string buttonName)
    {
        var button = window.FindControl<Button>(buttonName)
            ?? throw new InvalidOperationException($"Button '{buttonName}' not found.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}

using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Compositor.Core;

namespace Compositor.App;

public partial class MainWindow : Window
{
    private EditorViewModel? _viewModel;
    public EditorViewModel? Editor
    {
        get => _viewModel ??= DataContext as EditorViewModel;
        set { _viewModel = value; DataContext = value; }
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new EditorViewModel();
    }

    public MainWindow(EditorViewModel viewModel) : this()
    {
        Editor = viewModel;
    }

    private EditorViewModel? Vm => DataContext as EditorViewModel;

    private void OnAddLayer(object? sender, RoutedEventArgs e) => Vm?.AddLayer();

    private void OnDeleteLayer(object? sender, RoutedEventArgs e) => Vm?.DeleteSelected();

    private void OnMoveUp(object? sender, RoutedEventArgs e) => Vm?.MoveSelectedUp();

    private void OnMoveDown(object? sender, RoutedEventArgs e) => Vm?.MoveSelectedDown();

    private void OnUndo(object? sender, RoutedEventArgs e) => Vm?.Undo();

    private void OnRedo(object? sender, RoutedEventArgs e) => Vm?.Redo();

    private void OnBrushColor(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || Vm is null)
        {
            return;
        }

        var parts = tag.Split(',');
        if (parts.Length == 3 &&
            byte.TryParse(parts[0], out var r) &&
            byte.TryParse(parts[1], out var g) &&
            byte.TryParse(parts[2], out var b))
        {
            Vm.SetBrushColor(r, g, b);
        }
    }

    // ---------------------------------------------------------------------
    // View zoom/pan.
    // ---------------------------------------------------------------------

    private void OnZoomIn(object? sender, RoutedEventArgs e) => ZoomViewport(1.25);

    private void OnZoomOut(object? sender, RoutedEventArgs e) => ZoomViewport(0.8);

    private void OnFitView(object? sender, RoutedEventArgs e) => Vm?.ResetView();

    private void ZoomViewport(double factor)
    {
        if (Vm is null || EditorCanvas is null)
        {
            return;
        }

        var b = EditorCanvas.Bounds;
        Vm.ZoomAt(b.Width / 2, b.Height / 2, b.Width, b.Height, factor);
    }

    // ---------------------------------------------------------------------
    // File menu. Pickers only collect paths; all I/O lives in the VM so it
    // stays headless-testable.
    // ---------------------------------------------------------------------

    private void OnNew(object? sender, RoutedEventArgs e) => Editor = new EditorViewModel();

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open project",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Compositor project") { Patterns = ["*.comp"] },
                ],
            });
            if (files.Count != 1)
            {
                return;
            }

            Editor = EditorViewModel.LoadProject(files[0].Path.LocalPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Open failed: {ex.Message}");
        }
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Vm is null)
            {
                return;
            }

            if (Vm.CurrentFilePath is { } existing)
            {
                Vm.SaveProject(existing);
                return;
            }

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save project",
                DefaultExtension = "comp",
                FileTypeChoices =
                [
                    new FilePickerFileType("Compositor project") { Patterns = ["*.comp"] },
                ],
            });
            if (file is null)
            {
                return;
            }

            Vm.SaveProject(file.Path.LocalPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Save failed: {ex.Message}");
        }
    }

    private async void OnImportImage(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Vm is null)
            {
                return;
            }

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import PNG",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("PNG image") { Patterns = ["*.png"] },
                ],
            });
            if (files.Count != 1)
            {
                return;
            }

            Vm.ImportImagePng(files[0].Path.LocalPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Import failed: {ex.Message}");
        }
    }

    private async void OnExportPng(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Vm is null)
            {
                return;
            }

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export PNG",
                DefaultExtension = "png",
                SuggestedFileName = $"{Vm.Doc.Name}.png",
                FileTypeChoices =
                [
                    new FilePickerFileType("PNG image") { Patterns = ["*.png"] },
                ],
            });
            if (file is null)
            {
                return;
            }

            Vm.ExportPng(file.Path.LocalPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Export failed: {ex.Message}");
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.Z:
                    Vm?.Undo();
                    e.Handled = true;
                    return;
                case Key.Y:
                    Vm?.Redo();
                    e.Handled = true;
                    return;
                case Key.N:
                    OnNew(this, e);
                    e.Handled = true;
                    return;
                case Key.O:
                    OnOpen(this, e);
                    e.Handled = true;
                    return;
                case Key.S:
                    OnSave(this, e);
                    e.Handled = true;
                    return;
                case Key.I:
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        Vm?.InvertSelection();
                    }
                    else
                    {
                        OnImportImage(this, e);
                    }
                    e.Handled = true;
                    return;
                case Key.E:
                    OnExportPng(this, e);
                    e.Handled = true;
                    return;
                case Key.A:
                    Vm?.SelectAll();
                    e.Handled = true;
                    return;
                case Key.D:
                    Vm?.Deselect();
                    e.Handled = true;
                    return;
                case Key.X:
                    Vm?.CutSelection();
                    e.Handled = true;
                    return;
                case Key.C:
                    Vm?.CopySelection();
                    e.Handled = true;
                    return;
                case Key.V:
                    Vm?.PasteSelection();
                    e.Handled = true;
                    return;
            }
        }

        switch (e.Key)
        {
            case Key.Delete:
                Vm?.CutSelection();
                e.Handled = true;
                return;
            case Key.Enter:
                Vm?.CommitFloating();
                e.Handled = true;
                return;
            case Key.Escape:
                Vm?.CancelFloating();
                e.Handled = true;
                return;
        }

        base.OnKeyDown(e);
    }

    private void OnSelectAll(object? sender, RoutedEventArgs e) => Vm?.SelectAll();
    private void OnDeselect(object? sender, RoutedEventArgs e) => Vm?.Deselect();
    private void OnInvertSelection(object? sender, RoutedEventArgs e) => Vm?.InvertSelection();
    private void OnCut(object? sender, RoutedEventArgs e) => Vm?.CutSelection();
    private void OnCopy(object? sender, RoutedEventArgs e) => Vm?.CopySelection();
    private void OnPaste(object? sender, RoutedEventArgs e) => Vm?.PasteSelection();
    private void OnCommitFloating(object? sender, RoutedEventArgs e) => Vm?.CommitFloating();
    private void OnCancelFloating(object? sender, RoutedEventArgs e) => Vm?.CancelFloating();

    private void OnToolChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Vm is null || ToolPicker.SelectedItem is not ComboBoxItem item || item.Content is not string label)
        {
            return;
        }
        Vm.Tool = label switch
        {
            "Rect select" => EditorViewModel.EditorTool.RectangleSelect,
            "Ellipse select" => EditorViewModel.EditorTool.EllipseSelect,
            "Lasso select" => EditorViewModel.EditorTool.LassoSelect,
            _ => EditorViewModel.EditorTool.Brush,
        };
    }
}

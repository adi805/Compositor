using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.Z)
            {
                Vm?.Undo();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Y)
            {
                Vm?.Redo();
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }
}

using Avalonia.Controls;
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
}

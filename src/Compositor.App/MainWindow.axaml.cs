using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Compositor.Core;
using Compositor.Core.Adjustments;

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

    // ------------------------------------------------------ adjustment sheets

    private void ShowAdjustmentSheet(string title, params Avalonia.Controls.Control[] toShow)
    {
        if (Vm is null)
        {
            return;
        }

        AdjustmentTitle.Text = title;
        AdjustmentsPanel.IsVisible = true;
        LevelsControls.IsVisible = false;
        CurvesControls.IsVisible = false;
        HueSatControls.IsVisible = false;
        ExposureControls.IsVisible = false;
        GradientMapControls.IsVisible = false;
        foreach (var control in toShow)
        {
            control.IsVisible = true;
        }
    }

    private void HideAdjustmentPanel()
    {
        AdjustmentsPanel.IsVisible = false;
    }

    private void OnAdjustLevels(object? sender, RoutedEventArgs e)
    {
        if (Vm?.OpenAdjustmentSheet(EditorViewModel.AdjustmentKind.Levels) == true)
        {
            ShowAdjustmentSheet("Levels", LevelsControls);
            LevelsChannelPicker.SelectedIndex = (int)Vm.LevelsState.Channel;
            LoadLevelsSliders();
            DrawLevelsHistogram();
        }
    }

    private void OnAdjustCurves(object? sender, RoutedEventArgs e)
    {
        if (Vm?.OpenAdjustmentSheet(EditorViewModel.AdjustmentKind.Curves) == true)
        {
            ShowAdjustmentSheet("Curves", CurvesControls);
            CurvesChannelPicker.SelectedIndex = (int)Vm.CurvesState.Channel;
            DrawCurves();
        }
    }

    private void OnAdjustHueSat(object? sender, RoutedEventArgs e)
    {
        if (Vm?.OpenAdjustmentSheet(EditorViewModel.AdjustmentKind.HueSaturation) == true)
        {
            ShowAdjustmentSheet("Hue / Saturation", HueSatControls);
            HueRangePicker.SelectedIndex = (int)Vm.HueSatState.Range;
            LoadHueSatSliders();
        }
    }

    private void OnAdjustExposure(object? sender, RoutedEventArgs e)
    {
        if (Vm?.OpenAdjustmentSheet(EditorViewModel.AdjustmentKind.Exposure) == true)
        {
            ShowAdjustmentSheet("Exposure", ExposureControls);
            ExposureSlider.Value = Vm.ExposureState.Exposure;
            OffsetSlider.Value = Vm.ExposureState.Offset;
            GammaSlider.Value = Math.Clamp(Vm.ExposureState.Gamma, 0.1, 5);
        }
    }

    private void OnAdjustGradientMap(object? sender, RoutedEventArgs e)
    {
        if (Vm?.OpenAdjustmentSheet(EditorViewModel.AdjustmentKind.GradientMap) == true)
        {
            ShowAdjustmentSheet("Gradient Map", GradientMapControls);
            ShadowPicker.Color = ToAvaloniaColor(Vm.GradientMapState.Shadows);
            HighlightPicker.Color = ToAvaloniaColor(Vm.GradientMapState.Highlights);
            ReverseCheck.IsChecked = Vm.GradientMapState.Reversed;
        }
    }

    private static Avalonia.Media.Color ToAvaloniaColor(AdjustmentColor c) =>
        Avalonia.Media.Color.FromRgb(
            (byte)Math.Clamp(Math.Round(c.Red * 255), 0, 255),
            (byte)Math.Clamp(Math.Round(c.Green * 255), 0, 255),
            (byte)Math.Clamp(Math.Round(c.Blue * 255), 0, 255));

    private static AdjustmentColor ToAdjustmentColor(Avalonia.Media.Color c) =>
        new(c.R / 255d, c.G / 255d, c.B / 255d);

    private void OnAdjustmentOk(object? sender, RoutedEventArgs e)
    {
        Vm?.CommitAdjustment();
        HideAdjustmentPanel();
    }

    private void OnAdjustmentCancel(object? sender, RoutedEventArgs e)
    {
        Vm?.CancelAdjustment();
        HideAdjustmentPanel();
    }

    private void OnInvertColors(object? sender, RoutedEventArgs e) => Vm?.ApplyInvert();

    // Levels ------------------------------------------------------------------

    private void LoadLevelsSliders()
    {
        if (Vm is null)
        {
            return;
        }

        var r = Vm.LevelsState.Current;
        LevelsBlack.Value = r.Black;
        LevelsGamma.Value = r.Gamma;
        LevelsWhite.Value = r.White;
        LevelsOutBlack.Value = r.OutputBlack;
        LevelsOutWhite.Value = r.OutputWhite;
    }

    private void OnLevelsChannelChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Vm is null)
        {
            return;
        }

        Vm.LevelsState.Channel = (LevelsChannel)LevelsChannelPicker.SelectedIndex;
        LoadLevelsSliders();
    }

    private void OnLevelsSlider(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (Vm is null || Vm.OpenAdjustment != EditorViewModel.AdjustmentKind.Levels)
        {
            return;
        }

        var r = Vm.LevelsState.Current;
        r.Black = LevelsBlack.Value;
        r.Gamma = LevelsGamma.Value;
        r.White = LevelsWhite.Value;
        r.OutputBlack = LevelsOutBlack.Value;
        r.OutputWhite = LevelsOutWhite.Value;
        Vm.LevelsState.Current = r; // normalized on write
        Vm.UpdateAdjustmentPreview();
    }

    private void OnLevelsAuto(object? sender, RoutedEventArgs e, LevelsAuto mode)
    {
        if (Vm is null || Vm.OpenAdjustment != EditorViewModel.AdjustmentKind.Levels)
        {
            return;
        }

        Vm.ApplyLevelsAuto(mode);
        LoadLevelsSliders();
    }

    private void OnLevelsAutoContrast(object? sender, RoutedEventArgs e) => OnLevelsAuto(sender, e, LevelsAuto.Contrast);
    private void OnLevelsAutoColor(object? sender, RoutedEventArgs e) => OnLevelsAuto(sender, e, LevelsAuto.Color);
    private void OnLevelsAutoNeutral(object? sender, RoutedEventArgs e) => OnLevelsAuto(sender, e, LevelsAuto.Neutral);

    private void DrawLevelsHistogram()
    {
        LevelsHistogramCanvas.Children.Clear();
        if (Vm?.ActiveLayer?.Pixels is not { } surface)
        {
            return;
        }

        var bins = LevelsHistogram.Compute(surface, Vm.CurrentAdjustmentClip);
        var colors = new[] { "Gray", "Red", "Green", "Blue" };
        for (var c = 0; c < 4; c++)
        {
            var scale = LevelsHistogram.DisplayScale(bins[c]);
            if (scale <= 0)
            {
                continue;
            }

            var segments = new Avalonia.Controls.Shapes.Path
            {
                Stroke = c == 0 ? Avalonia.Media.Brushes.DimGray : new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Color.Parse(colors[c] switch { "Red" => "#C05040", "Green" => "#50A060", "Blue" => "#5070B0", _ => "#404040" })),
                StrokeThickness = c == 0 ? 1 : 1,
                Opacity = c == 0 ? 0.5 : 0.85,
                Data = BuildHistogramGeometry(bins[c], scale),
            };
            LevelsHistogramCanvas.Children.Add(segments);
        }
    }

    private static Avalonia.Media.StreamGeometry BuildHistogramGeometry(double[] bins, double scale)
    {
        var geometry = new Avalonia.Media.StreamGeometry();
        using (var ctx = geometry.Open())
        {
            const double height = 88;
            ctx.BeginFigure(new Avalonia.Point(0, height), false);
            for (var i = 0; i < 256; i++)
            {
                var y = height - Math.Min(height, bins[i] / scale * height);
                ctx.LineTo(new Avalonia.Point(i, y));
            }
        }

        return geometry;
    }

    // Curves ------------------------------------------------------------------

    private int _curvesDragIndex = -1;

    private int CurvesChannelIndex =>
        CurvesChannelPicker.SelectedIndex is { } i && i > 0 ? i : 0;

    private void OnCurvesChannelChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Vm is null)
        {
            return;
        }

        Vm.CurvesState.Channel = (LevelsChannel)CurvesChannelIndex;
        DrawCurves();
    }

    private void DrawCurves()
    {
        CurvesCanvas.Children.Clear();
        if (Vm is null)
        {
            return;
        }

        var channel = CurvesChannelIndex;
        // diagonal reference
        var diagonal = new Avalonia.Controls.Shapes.Line
        {
            StartPoint = new Avalonia.Point(0, 256),
            EndPoint = new Avalonia.Point(256, 0),
            Stroke = Avalonia.Media.Brushes.DimGray,
            StrokeThickness = 1,
            Opacity = 0.4,
        };
        CurvesCanvas.Children.Add(diagonal);

        var curve = new Avalonia.Controls.Shapes.Polyline
        {
            Stroke = Avalonia.Media.Brushes.White,
            StrokeThickness = 1.5,
        };
        var points = new Avalonia.Collections.AvaloniaList<Avalonia.Point>(17);
        for (var x = 0; x <= 256; x += 16)
        {
            var y = Vm.CurvesState.Value(x, channel);
            points.Add(new Avalonia.Point(x, 256 - y));
        }

        curve.Points = points;
        CurvesCanvas.Children.Add(curve);

        foreach (var p in Vm.CurvesState.Channels[channel])
        {
            CurvesCanvas.Children.Add(new Avalonia.Controls.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = Avalonia.Media.Brushes.Orange,
                Stroke = Avalonia.Media.Brushes.Black,
                [Canvas.LeftProperty] = p.X - 4,
                [Canvas.TopProperty] = 256 - p.Y - 4,
            });
        }
    }

    private void OnCurvesPointer(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is null || e.GetCurrentPoint(CurvesCanvas).Properties.IsLeftButtonPressed == false)
        {
            return;
        }

        var pos = e.GetPosition(CurvesCanvas);
        var channel = CurvesChannelIndex;
        var docX = Math.Clamp(pos.X, 0, 255);
        var docY = 256 - Math.Clamp(pos.Y, 0, 255);

        _curvesDragIndex = NearestPointIndex(Vm.CurvesState.Channels[channel], docX, docY, 12);
        if (_curvesDragIndex < 0 && docX is > 0 and < 255)
        {
            // insert a new handle at this x
            var points = Vm.CurvesState.Channels[channel].ToList();
            points.Add(new CurvePoint(docX, Vm.CurvesState.Value(docX, channel)));
            points.Sort((a, b) => a.X.CompareTo(b.X));
            Vm.CurvesState.Channels[channel] = points.ToArray();
            _curvesDragIndex = points.FindIndex(p => Math.Abs(p.X - docX) < 0.5);
        }

        e.Pointer.Capture(CurvesCanvas);
        MoveDraggedPoint(docX, docY);
    }

    private static int NearestPointIndex(CurvePoint[] points, double x, double y, double maxDist)
    {
        var best = -1;
        var bestDist = maxDist * maxDist;
        for (var i = 0; i < points.Length; i++)
        {
            var dx = points[i].X - x;
            var dy = points[i].Y - y;
            var dist = (dx * dx) + (dy * dy);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        return best;
    }

    private void OnCurvesPointerMove(object? sender, PointerEventArgs e)
    {
        if (Vm is null || _curvesDragIndex < 0)
        {
            return;
        }

        var pos = e.GetPosition(CurvesCanvas);
        MoveDraggedPoint(Math.Clamp(pos.X, 0, 255), 256 - Math.Clamp(pos.Y, 0, 255));
    }

    private void MoveDraggedPoint(double x, double y)
    {
        if (Vm is null || _curvesDragIndex < 0)
        {
            return;
        }

        var channel = CurvesChannelIndex;
        var points = Vm.CurvesState.Channels[channel].ToArray();
        var i = _curvesDragIndex;
        if (i == 0 || i == points.Length - 1)
        {
            // endpoints keep x, move y only
            points[i] = new CurvePoint(points[i].X, Math.Clamp(y, 0, 255));
        }
        else
        {
            var minX = points[i - 1].X + 1;
            var maxX = points[i + 1].X - 1;
            points[i] = new CurvePoint(Math.Clamp(x, minX, maxX), Math.Clamp(y, 0, 255));
        }

        Vm.CurvesState.Channels[channel] = points;
        DrawCurves();
        Vm.UpdateAdjustmentPreview();
    }

    private void OnCurvesPointerEnd(object? sender, PointerReleasedEventArgs e)
    {
        _curvesDragIndex = -1;
    }

    private void OnCurvesWheel(object? sender, PointerWheelEventArgs e)
    {
        // reserved for fine adjustments; mark handled so the canvas doesn't zoom
        e.Handled = true;
    }

    // Hue/Saturation ------------------------------------------------------------

    private bool _hueSatSyncing;

    private void LoadHueSatSliders()
    {
        if (Vm is null)
        {
            return;
        }

        _hueSatSyncing = true;
        HueSlider.Value = Vm.HueSatState.Hue;
        SatSlider.Value = Vm.HueSatState.Saturation;
        LightSlider.Value = Vm.HueSatState.Lightness;
        ColorizeCheck.IsChecked = Vm.HueSatState.Colorize;
        _hueSatSyncing = false;
    }

    private void OnHueRangeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Vm is null || _hueSatSyncing)
        {
            return;
        }

        Vm.HueSatState.Range = (ColorRange)HueRangePicker.SelectedIndex;
        LoadHueSatSliders();
        Vm.UpdateAdjustmentPreview();
    }

    private void OnHueSatSlider(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || _hueSatSyncing || Vm.OpenAdjustment != EditorViewModel.AdjustmentKind.HueSaturation)
        {
            return;
        }

        Vm.HueSatState.Colorize = ColorizeCheck.IsChecked == true;
        if (!Vm.HueSatState.Colorize)
        {
            Vm.HueSatState.Hue = HueSlider.Value;
            Vm.HueSatState.Saturation = SatSlider.Value;
            Vm.HueSatState.Lightness = LightSlider.Value;
        }
        else
        {
            // colorize uses the raw slider values as its master settings
            Vm.HueSatState.Hue = HueSlider.Value;
            Vm.HueSatState.Saturation = SatSlider.Value;
            Vm.HueSatState.Lightness = LightSlider.Value;
        }

        Vm.UpdateAdjustmentPreview();
    }

    // Exposure ------------------------------------------------------------------

    private void OnExposureSlider(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (Vm is null || Vm.OpenAdjustment != EditorViewModel.AdjustmentKind.Exposure)
        {
            return;
        }

        Vm.ExposureState.Exposure = ExposureSlider.Value;
        Vm.ExposureState.Offset = OffsetSlider.Value;
        Vm.ExposureState.Gamma = GammaSlider.Value;
        Vm.UpdateAdjustmentPreview();
    }

    // Gradient Map ----------------------------------------------------------------

    private void OnGradientMapColor(object? sender, Avalonia.Controls.ColorChangedEventArgs e) => SyncGradientMap();

    private void OnGradientMapReverse(object? sender, RoutedEventArgs e) => SyncGradientMap();

    private void SyncGradientMap()
    {
        if (Vm is null || Vm.OpenAdjustment != EditorViewModel.AdjustmentKind.GradientMap)
        {
            return;
        }

        Vm.GradientMapState.Shadows = ToAdjustmentColor(ShadowPicker.Color);
        Vm.GradientMapState.Highlights = ToAdjustmentColor(HighlightPicker.Color);
        Vm.GradientMapState.Reversed = ReverseCheck.IsChecked == true;
        Vm.UpdateAdjustmentPreview();
    }
}

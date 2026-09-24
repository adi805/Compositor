using System.IO;
using System.Globalization;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Compositor.Core;
using Compositor.Core.Adjustments;
using Compositor.Core.Filters;
using Compositor.Core.Imaging;

namespace Compositor.App;

public partial class MainWindow : Window
{
    private EditorViewModel? _viewModel;
    public EditorViewModel? Editor
    {
        get => _viewModel ??= DataContext as EditorViewModel;
        set
        {
            _viewModel = value;
            DataContext = value;
            if (BlendPicker is not null)
            {
                BlendPicker.ItemsSource = Enum.GetValues<Core.BlendMode>();
            }
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new EditorViewModel();
        BlendPicker.ItemsSource = Enum.GetValues<Core.BlendMode>();

        // Drop-to-import target (upstream IO/ImageFileDrop.swift). The canvas is the drop
        // surface so the drop point can be turned into a document position.
        DragDrop.SetAllowDrop(EditorCanvas, true);
        DragDrop.AddDragOverHandler(EditorCanvas, OnCanvasDragOver);
        DragDrop.AddDropHandler(EditorCanvas, OnCanvasDrop);
        DataContextChanged += (_, _) => ObserveImportError();
        ObserveImportError();
    }

    public MainWindow(EditorViewModel viewModel) : this()
    {
        Editor = viewModel;
    }

    private EditorViewModel? Vm => DataContext as EditorViewModel;

    private void OnGroupLayers(object? sender, RoutedEventArgs e) => Vm?.GroupSelected();
    private void OnAddFolder(object? sender, RoutedEventArgs e) => Vm?.AddGroup();
    private void OnMergeDown(object? sender, RoutedEventArgs e) => Vm?.MergeDown();
    private void OnFlipH(object? sender, RoutedEventArgs e) => Vm?.FlipActive(horizontally: true);
    private void OnFlipV(object? sender, RoutedEventArgs e) => Vm?.FlipActive(horizontally: false);
    private void OnFlipCanvasH(object? sender, RoutedEventArgs e) => Vm?.FlipCanvas(horizontally: true);
    private void OnFlipCanvasV(object? sender, RoutedEventArgs e) => Vm?.FlipCanvas(horizontally: false);

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
            if (Vm is not { } vm)
            {
                return;
            }

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import image",
                AllowMultiple = true,
                FileTypeFilter = ImportFilters(),
            });
            if (files.Count == 0)
            {
                return;
            }

            vm.ImportImages(files.Select(f => f.Path.LocalPath).ToArray());
            ShowImportNote(vm.ImportError);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Import failed: {ex.Message}");
        }
    }

    /// Picker filters generated from the format matrix, so the dialog can never offer a
    /// container the importer has not been shown to read.
    private static FilePickerFileType[] ImportFilters()
    {
        var types = new List<FilePickerFileType>
        {
            new("All supported images") { Patterns = ImageFormatPolicy.ImportPatterns.ToArray() },
        };
        foreach (var row in ImageFormatPolicy.Importable)
        {
            types.Add(new FilePickerFileType(row.DisplayName)
            {
                Patterns = row.Extensions.Select(e => "*." + e).ToArray(),
            });
        }

        return types.ToArray();
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

    // ---------------------------------------------------------------------
    // Drag and drop import. Upstream's ImageFileDrop resolves file promises first and
    // falls back to raw image data; the drop point decides where the image lands.
    // ---------------------------------------------------------------------

    private void OnCanvasDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = AcceptsDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;

    private static bool AcceptsDrop(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles() is { Length: > 0 } || e.DataTransfer.Contains(DataFormat.Bitmap);

    private void OnCanvasDrop(object? sender, DragEventArgs e)
    {
        if (Vm is not { } vm)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var at = DropPoint(e);
        var files = e.DataTransfer.TryGetFiles();
        if (files is { Length: > 0 })
        {
            var paths = files
                .Where(item => item.Path is not null)
                .Select(item => item.Path!.LocalPath)
                .ToArray();
            var added = vm.ImportImages(paths, at);
            if (paths.Length == 0)
            {
                vm.ReportUnreadableDrop();
            }

            e.DragEffects = added.Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
            ShowImportNote(vm.ImportError);
            return;
        }

        // No file behind it: a screenshot or a picture dragged out of a browser hands over
        // image data instead. Upstream copies that to a temporary file; we keep it in memory
        // and feed the same decoder, so nothing lands on disk.
        var bitmap = e.DataTransfer.TryGetBitmap();
        if (bitmap is not null)
        {
            using var encoded = new MemoryStream();
            bitmap.Save(encoded);
            var layer = vm.ImportImageBytes(encoded.ToArray(), "Dropped image", at);
            e.DragEffects = layer is null ? DragDropEffects.None : DragDropEffects.Copy;
            ShowImportNote(vm.ImportError);
            return;
        }

        vm.ReportUnreadableDrop();
        e.DragEffects = DragDropEffects.None;
        ShowImportNote(vm.ImportError);
    }

    /// The document point under the cursor, so an image lands where it was dropped.
    private (float X, float Y)? DropPoint(DragEventArgs e)
    {
        if (Vm is not { } vm)
        {
            return null;
        }

        var p = e.GetPosition(EditorCanvas);
        return vm.ScreenToDoc(p.X, p.Y, EditorCanvas.Bounds.Width, EditorCanvas.Bounds.Height);
    }

    // ---------------------------------------------------------------------
    // Import/export note. The VM owns the message so it stays testable; this only mirrors it.
    // ---------------------------------------------------------------------

    private EditorViewModel? _notedViewModel;

    private void ObserveImportError()
    {
        if (ReferenceEquals(_notedViewModel, Vm))
        {
            return;
        }

        if (_notedViewModel is not null)
        {
            _notedViewModel.PropertyChanged -= OnVmPropertyChanged;
        }

        _notedViewModel = Vm;
        if (_notedViewModel is not null)
        {
            _notedViewModel.PropertyChanged += OnVmPropertyChanged;
        }

        ShowImportNote(Vm?.ImportError);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.ImportError))
        {
            ShowImportNote(Vm?.ImportError);
        }
    }

    private void ShowImportNote(string? message)
    {
        StatusNote.IsVisible = !string.IsNullOrEmpty(message);
        StatusNoteText.Text = message ?? string.Empty;
    }

    // ---------------------------------------------------------------------
    // JPEG export sheet: upstream's JPEGExportSheet. Quality 0...1 with a percent readout,
    // a matte for transparency, live encoded-size feedback, remembered quality.
    // ---------------------------------------------------------------------

    private JpegOptions _jpegOptions = new();
    private bool _jpegReady;

    private void OnExportJpeg(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm)
        {
            return;
        }

        AdjustmentsPanel.IsVisible = false;
        HideFilterPanel();
        GeometryPanel.IsVisible = false;

        _jpegOptions = JpegExportSettings.Load();
        JpegPanel.IsVisible = true;

        // Assigning a slider fires ValueChanged, which recomputes the options and the preview,
        // so the matte has to be written before the quality or the colour would be dropped.
        JpegMatte.Color = MatteColor(_jpegOptions);
        JpegQuality.Value = _jpegOptions.Quality;
        JpegInfo.Text = JpegLine(vm, bytes: null);
    }

    private static Avalonia.Media.Color MatteColor(JpegOptions options) =>
        Avalonia.Media.Color.FromRgb(
            (byte)Math.Clamp((int)Math.Round(options.Red * 255, MidpointRounding.AwayFromZero), 0, 255),
            (byte)Math.Clamp((int)Math.Round(options.Green * 255, MidpointRounding.AwayFromZero), 0, 255),
            (byte)Math.Clamp((int)Math.Round(options.Blue * 255, MidpointRounding.AwayFromZero), 0, 255));

    private void OnJpegQuality(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (Vm is not { } vm || !JpegPanel.IsVisible)
        {
            return;
        }

        _jpegOptions = (_jpegOptions with { Quality = JpegQuality.Value }).Normalize();
        JpegQualityReadout.Text = _jpegOptions.QualityReadout;
        RefreshJpegPreview(vm);
    }

    private void OnJpegMatteChanged(object? sender, Avalonia.Controls.ColorChangedEventArgs e)
    {
        if (Vm is not { } vm || !JpegPanel.IsVisible)
        {
            return;
        }

        var color = JpegMatte.Color;
        _jpegOptions = (_jpegOptions with
        {
            Red = color.R / 255d,
            Green = color.G / 255d,
            Blue = color.B / 255d,
        }).Normalize();
        RefreshJpegPreview(vm);
    }

    /// <summary>
    /// Encodes the current canvas so the sheet can show a real byte count. Upstream debounces
    /// this with a spinner because its preview decodes a thumbnail too; ours is synchronous and
    /// only reports size, which is the part that changes with quality.
    /// </summary>
    private void RefreshJpegPreview(EditorViewModel vm)
    {
        try
        {
            var bytes = vm.EncodeJpeg(_jpegOptions);
            _jpegReady = true;
            JpegInfo.Text = JpegLine(vm, bytes.Length);
        }
        catch (ImageException ex)
        {
            _jpegReady = false;
            JpegInfo.Text = ex.Message;
        }

        JpegExport.IsEnabled = _jpegReady;
    }

    private static string JpegLine(EditorViewModel vm, int? bytes)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{vm.Doc.Width} × {vm.Doc.Height} px · sRGB · {vm.Doc.Resolution:0.#} dpi");
        return bytes is { } count ? line + " · " + FormatBytes(count) : line;
    }

    private static string FormatBytes(long count) => count switch
    {
        >= 1024 * 1024 => (count / (1024d * 1024d)).ToString("0.0", CultureInfo.InvariantCulture) + " MB",
        >= 1024 => (count / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " KB",
        _ => count + " bytes",
    };

    private async void OnJpegExport(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm)
        {
            return;
        }

        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export JPEG",
                DefaultExtension = "jpg",
                SuggestedFileName = $"{vm.Doc.Name}.jpg",
                FileTypeChoices =
                [
                    new FilePickerFileType("JPEG image") { Patterns = ["*.jpg", "*.jpeg"] },
                ],
            });
            if (file is null)
            {
                return;
            }

            vm.ExportJpeg(file.Path.LocalPath, _jpegOptions);
            JpegExportSettings.Save(_jpegOptions);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Export failed: {ex.Message}");
        }
        finally
        {
            JpegPanel.IsVisible = false;
        }
    }

    private void OnJpegCancel(object? sender, RoutedEventArgs e) => JpegPanel.IsVisible = false;

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
                case Key.G:
                    Vm?.GroupSelected();
                    e.Handled = true;
                    return;
                case Key.M:
                    Vm?.MergeDown();
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
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        OnExportJpeg(this, e);
                    }
                    else
                    {
                        OnExportPng(this, e);
                    }

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
            "Magic wand" => EditorViewModel.EditorTool.MagicWand,
            "Gradient" => EditorViewModel.EditorTool.Gradient,
            "Shape" => EditorViewModel.EditorTool.Shape,
            "Blur" => EditorViewModel.EditorTool.Blur,
            "Smudge" => EditorViewModel.EditorTool.Smudge,
            "Clone stamp" => EditorViewModel.EditorTool.CloneStamp,
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
        GrainControls.IsVisible = false;
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

    private void OnAdjustGrain(object? sender, RoutedEventArgs e)
    {
        if (Vm?.OpenAdjustmentSheet(EditorViewModel.AdjustmentKind.Grain) == true)
        {
            ShowAdjustmentSheet("Grain", GrainControls);
            GrainSliderSet(Vm.GrainState);
        }
    }

    private void GrainSliderSet(GrainSettings g)
    {
        GrainAmount.Value = g.Amount;
        GrainSize.Value = g.Size;
        GrainRoughness.Value = g.Roughness;
    }

    private void OnGrainSlider(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (Vm is null || Vm.OpenAdjustment != EditorViewModel.AdjustmentKind.Grain)
        {
            return;
        }

        Vm.SetGrainState(Vm.GrainState with
        {
            Amount = GrainAmount.Value,
            Size = GrainSize.Value,
            Roughness = GrainRoughness.Value,
        });
    }

    // ------------------------------------------------------------------ filters

    private void ShowFilterSheet(string title, params Avalonia.Controls.Control[] toShow)
    {
        FilterTitle.Text = title;
        FilterPanel.IsVisible = true;
        GaussianBlurControls.IsVisible = false;
        MotionBlurControls.IsVisible = false;
        AddNoiseControls.IsVisible = false;
        LensCorrectionControls.IsVisible = false;
        foreach (var control in toShow)
        {
            control.IsVisible = true;
        }
    }

    private void HideFilterPanel() => FilterPanel.IsVisible = false;

    private void FilterSliderSet(FilterSettings f)
    {
        RadiusSlider.Value = f.Radius;
        AngleSlider.Value = f.Angle;
        DistanceSlider.Value = f.Distance;
        NoiseAmountSlider.Value = f.Amount;
        DistortionSlider.Value = f.Distortion;
        GaussianNoiseCheck.IsChecked = f.Gaussian;
        MonochromeNoiseCheck.IsChecked = f.Monochromatic;
        WriteFilterReadouts(f);
    }

    private void WriteFilterReadouts(FilterSettings f)
    {
        RadiusReadout.Text = " " + f.Radius.ToString("0.0", CultureInfo.InvariantCulture);
        AngleReadout.Text = " " + f.Angle.ToString("0", CultureInfo.InvariantCulture);
        DistanceReadout.Text = " " + f.Distance.ToString("0", CultureInfo.InvariantCulture);
        NoiseAmountReadout.Text = " " + f.Amount.ToString("0.0", CultureInfo.InvariantCulture);
        DistortionReadout.Text = " " + f.Distortion.ToString("0", CultureInfo.InvariantCulture);
    }

    private void OnFilterGaussianBlur(object? sender, RoutedEventArgs e)
        => OpenFilterSheet(EditorViewModel.FilterMenuKind.GaussianBlur, "Gaussian Blur", GaussianBlurControls);

    private void OnFilterMotionBlur(object? sender, RoutedEventArgs e)
        => OpenFilterSheet(EditorViewModel.FilterMenuKind.MotionBlur, "Motion Blur", MotionBlurControls);

    private void OnFilterAddNoise(object? sender, RoutedEventArgs e)
        => OpenFilterSheet(EditorViewModel.FilterMenuKind.AddNoise, "Add Noise", AddNoiseControls);

    private void OnFilterLensCorrection(object? sender, RoutedEventArgs e)
        => OpenFilterSheet(EditorViewModel.FilterMenuKind.LensCorrection, "Lens Correction", LensCorrectionControls);

    private void OpenFilterSheet(EditorViewModel.FilterMenuKind kind, string title, Avalonia.Controls.Control group)
    {
        if (Vm?.OpenFilterSheet(kind) == true)
        {
            ShowFilterSheet(title, group);
            FilterSliderSet(Vm.FilterState);
        }
    }

    /// <summary>
    /// One handler for every filter slider. A record is immutable, so each change is written back
    /// as a whole settings object; reading all sliders (not just the sender) keeps the filter's own
    /// parameters independent of which control fired.
    /// </summary>
    private void OnFilterSlider(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (Vm?.OpenFilter is not { } kind)
        {
            return;
        }

        var next = Vm.FilterState with
        {
            Radius = RadiusSlider.Value,
            Angle = AngleSlider.Value,
            Distance = DistanceSlider.Value,
            Amount = NoiseAmountSlider.Value,
            Distortion = DistortionSlider.Value,
            Gaussian = GaussianNoiseCheck.IsChecked == true,
            Monochromatic = MonochromeNoiseCheck.IsChecked == true,
        };
        Vm.SetFilterState(next);
        WriteFilterReadouts(next);
    }

    private void OnFilterCheck(object? sender, RoutedEventArgs e)
    {
        if (Vm?.OpenFilter is not { })
        {
            return;
        }

        var next = Vm.FilterState with
        {
            Gaussian = GaussianNoiseCheck.IsChecked == true,
            Monochromatic = MonochromeNoiseCheck.IsChecked == true,
        };
        Vm.SetFilterState(next);
    }

    private void OnFilterOk(object? sender, RoutedEventArgs e)
    {
        Vm?.CommitFilter();
        HideFilterPanel();
    }

    private void OnFilterCancel(object? sender, RoutedEventArgs e)
    {
        Vm?.CancelFilter();
        HideFilterPanel();
    }

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

    private void OnContentAwareFill(object? sender, RoutedEventArgs e) => Vm?.ContentAwareFill();

    // Geometry sheets: image size / canvas size / crop -------------------------

    private void ShowGeometrySheet(string title, params Avalonia.Controls.Control[] toShow)
    {
        GeometryTitle.Text = title;
        GeometryPanel.IsVisible = true;
        ImageSizeControls.IsVisible = false;
        CanvasSizeControls.IsVisible = false;
        CropControls.IsVisible = false;
        foreach (var control in toShow)
        {
            control.IsVisible = true;
        }
    }

    private static bool TryParseInt(string? text, out int value) => int.TryParse(text?.Trim(), out value);

    private void OnImageSize(object? sender, RoutedEventArgs e)
    {
        if (Vm is null)
        {
            return;
        }
        ImageSizeW.Text = Vm.Doc.Width.ToString(CultureInfo.InvariantCulture);
        ImageSizeH.Text = Vm.Doc.Height.ToString(CultureInfo.InvariantCulture);
        ImageSizeDpi.Text = Vm.Doc.Resolution.ToString("0.#", CultureInfo.InvariantCulture);
        ShowGeometrySheet("Image Size", ImageSizeControls);
    }

    private void OnImageSizeApply(object? sender, RoutedEventArgs e)
    {
        if (Vm is null
            || !TryParseInt(ImageSizeW.Text, out var w)
            || !TryParseInt(ImageSizeH.Text, out var h)
            || !double.TryParse(ImageSizeDpi.Text?.Trim(), System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var dpi))
        {
            return;
        }
        if (Vm.ApplyImageSize(w, h, dpi))
        {
            GeometryPanel.IsVisible = false;
        }
    }

    private void OnCanvasSize(object? sender, RoutedEventArgs e)
    {
        if (Vm is null)
        {
            return;
        }
        CanvasSizeW.Text = Vm.Doc.Width.ToString(CultureInfo.InvariantCulture);
        CanvasSizeH.Text = Vm.Doc.Height.ToString(CultureInfo.InvariantCulture);
        CanvasAnchorPicker.SelectedIndex = 4;
        ShowGeometrySheet("Canvas Size", CanvasSizeControls);
    }

    private void OnCanvasSizeApply(object? sender, RoutedEventArgs e)
    {
        var anchor = CanvasAnchorPicker.SelectedIndex;
        if (Vm is null
            || !TryParseInt(CanvasSizeW.Text, out var w)
            || !TryParseInt(CanvasSizeH.Text, out var h)
            || anchor < 0)
        {
            return;
        }
        (byte, byte, byte)? fill = CanvasFillCheck.IsChecked == true ? ((byte)255, (byte)255, (byte)255) : null;
        if (Vm.ApplyCanvasSize(w, h, anchor, fill))
        {
            GeometryPanel.IsVisible = false;
        }
    }

    private void OnCropSheet(object? sender, RoutedEventArgs e)
    {
        if (Vm is null)
        {
            return;
        }
        CropX.Text = "0";
        CropY.Text = "0";
        CropW.Text = Vm.Doc.Width.ToString(CultureInfo.InvariantCulture);
        CropH.Text = Vm.Doc.Height.ToString(CultureInfo.InvariantCulture);
        ShowGeometrySheet("Crop", CropControls);
    }

    private void OnCropApply(object? sender, RoutedEventArgs e)
    {
        if (Vm is null
            || !TryParseInt(CropX.Text, out var x)
            || !TryParseInt(CropY.Text, out var y)
            || !TryParseInt(CropW.Text, out var w)
            || !TryParseInt(CropH.Text, out var h))
        {
            return;
        }
        if (Vm.ApplyCrop(x, y, w, h))
        {
            GeometryPanel.IsVisible = false;
        }
    }

    private void OnCropToSelection(object? sender, RoutedEventArgs e) => Vm?.ApplyCropToSelection();

    private void OnGeometryClose(object? sender, RoutedEventArgs e) => GeometryPanel.IsVisible = false;

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

using Avalonia;
using Compositor.Core;
using System.ComponentModel;

namespace Compositor.App;

/// <summary>
/// A single row in the layers panel: a thin wrapper around a Core Layer.
/// Top-first order (the UI convention); the document list is bottom-first.
/// </summary>
public sealed class LayerRow : INotifyPropertyChanged
{
    public Layer Layer { get; }

    public Guid Id => Layer.Id;

    public string Name => Layer.Name;

    /// <summary>Hierarchy depth for indentation (0 = root).</summary>
    public int Depth { get; init; }

    public Thickness Indent => new(Depth * 16, 0, 0, 0);

    /// <summary>List label: folders get a folder glyph.</summary>
    public string DisplayName => (Layer.IsGroup ? "\U0001F4C1 " : string.Empty) + Layer.Name;

    public bool IsGroup => Layer.IsGroup;

    /// <summary>Raised when a proxied value changed so the canvas can refresh.</summary>
    public event Action<LayerRow>? LayerChanged;

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnPropertyChanged(nameof(IsActive)); } }
    }

    public bool IsVisible
    {
        get => Layer.IsVisible;
        set
        {
            if (Layer.IsVisible != value)
            {
                Layer.IsVisible = value;
                OnPropertyChanged(nameof(IsVisible));
                LayerChanged?.Invoke(this);
            }
        }
    }

    public LayerRow(Layer layer) => Layer = layer;

    /// <summary>Re-reads proxied values after an external mutation.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsVisible));
    }

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public event PropertyChangedEventHandler? PropertyChanged;
}

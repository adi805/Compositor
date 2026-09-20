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

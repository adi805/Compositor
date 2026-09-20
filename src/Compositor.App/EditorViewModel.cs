using System.Collections.ObjectModel;
using System.ComponentModel;
using Compositor.Core;

namespace Compositor.App;

/// <summary>
/// Editor view-model: binds a Core Document to the layers panel and canvas.
/// Pure INPC, no Avalonia types, so it is testable without a UI platform.
/// </summary>
public sealed class EditorViewModel : INotifyPropertyChanged
{
    public Document Doc { get; }

    /// <summary>Rows top-first; the document list is bottom-first.</summary>
    public ObservableCollection<LayerRow> Rows { get; } = new();

    private LayerRow? _selected;
    public LayerRow? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value))
            {
                return;
            }

            if (_selected is not null)
            {
                _selected.IsActive = false;
            }

            _selected = value;
            if (_selected is not null)
            {
                _selected.IsActive = true;
                Doc.ActiveLayerId = _selected.Id;
            }

            OnPropertyChanged(nameof(Selected));
        }
    }

    public string Title => $"{Doc.Name} - {Doc.Width}x{Doc.Height}";

    public EditorViewModel(Document doc)
    {
        Doc = doc ?? throw new ArgumentNullException(nameof(doc));
        RebuildRows();
        var active = doc.ActiveLayerId;
        Selected = Rows.FirstOrDefault(r => r.Id == active) ?? Rows.FirstOrDefault();
    }

    public EditorViewModel() : this(NewDocument()) { }

    private static Document NewDocument()
    {
        var doc = new Document(1280, 720) { Name = "Untitled" };
        doc.AddLayer(new Layer("Background"));
        return doc;
    }

    public void AddLayer()
    {
        var layer = new Layer($"Layer {Doc.Layers.Count + 1}");
        Doc.AddLayer(layer);
        var row = new LayerRow(layer);
        // Newest layer is topmost: insert at the top of the reversed view.
        Rows.Insert(0, row);
        Selected = row;
        OnPropertyChanged(nameof(Title));
    }

    public void DeleteSelected()
    {
        if (_selected is null)
        {
            return;
        }

        var index = Doc.Layers.IndexOf(_selected.Layer);
        if (index < 0)
        {
            return;
        }

        Doc.Layers.RemoveAt(index);
        Rows.Remove(_selected);
        Selected = Rows.FirstOrDefault();
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>Moves the selected layer one step toward the top of the stack.</summary>
    public void MoveSelectedUp() => MoveSelected(+1);

    /// <summary>Moves the selected layer one step toward the bottom of the stack.</summary>
    public void MoveSelectedDown() => MoveSelected(-1);

    private void MoveSelected(int docIndexDelta)
    {
        if (_selected is null)
        {
            return;
        }

        // Capture before rebuilding: clearing Rows can reset the ListBox
        // selection, which would push Selected = null back through binding.
        var selectedId = _selected.Id;
        var index = Doc.Layers.IndexOf(_selected.Layer);
        var target = index + docIndexDelta;
        if (index < 0 || target < 0 || target >= Doc.Layers.Count)
        {
            return;
        }

        Doc.Layers.RemoveAt(index);
        Doc.Layers.Insert(target, _selected.Layer);
        RebuildRows();
        Selected = Rows.FirstOrDefault(r => r.Id == selectedId);
    }

    public static void ToggleVisibility(LayerRow row) => row.IsVisible = !row.IsVisible;

    private void RebuildRows()
    {
        Rows.Clear();
        for (var i = Doc.Layers.Count - 1; i >= 0; i--)
        {
            Rows.Add(new LayerRow(Doc.Layers[i]));
        }
    }

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public event PropertyChangedEventHandler? PropertyChanged;
}

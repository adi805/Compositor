using System.Text.Json;
using System.Text.Json.Serialization;

namespace Compositor.Core.Project;

/// <summary>Manifest DTO for the .comp format, version 2 (Mac v6 subset + layer groups).</summary>
public sealed record Manifest
{
    [JsonPropertyName("identifier")]
    public string Identifier { get; init; } = ProjectStore.Identifier;

    [JsonPropertyName("version")]
    public int Version { get; init; } = 2;

    [JsonPropertyName("documentUUID")]
    public string DocumentUuid { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = "Untitled";

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    /// <summary>Print resolution DPI. Optional for pre-v6 Mac files; default 72.</summary>
    [JsonPropertyName("resolution")]
    public double? Resolution { get; init; }

    [JsonPropertyName("activeLayerUUID")]
    public string? ActiveLayerUuid { get; init; }

    [JsonPropertyName("layers")]
    public List<ManifestLayer> Layers { get; init; } = new();
}

/// <summary>Layer record inside the manifest. Bottom-to-top order.</summary>
public sealed record ManifestLayer
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = "Layer";

    [JsonPropertyName("isVisible")]
    public bool IsVisible { get; init; } = true;

    [JsonPropertyName("isLocked")]
    public bool IsLocked { get; init; }

    [JsonPropertyName("opacity")]
    public double Opacity { get; init; } = 1.0;

    [JsonPropertyName("blendMode")]
    public string BlendMode { get; init; } = "normal";

    [JsonPropertyName("transform")]
    public ManifestTransform Transform { get; init; } = new();

    /// <summary>Image entry name under images/, absent for blank layers.</summary>
    [JsonPropertyName("image")]
    public string? Image { get; init; }

    /// <summary>Parent group UUID string, absent for root-level layers (format v2).</summary>
    [JsonPropertyName("parentUUID")]
    public string? ParentUuid { get; init; }

    /// <summary>True for group folders (no image, children follow in list order).</summary>
    [JsonPropertyName("isGroup")]
    public bool IsGroup { get; init; }
}

/// <summary>Transform block: origin, size, clockwise degrees, flips.</summary>
public sealed record ManifestTransform
{
    [JsonPropertyName("originX")]
    public double OriginX { get; init; }

    [JsonPropertyName("originY")]
    public double OriginY { get; init; }

    [JsonPropertyName("width")]
    public double Width { get; init; }

    [JsonPropertyName("height")]
    public double Height { get; init; }

    [JsonPropertyName("rotationDegrees")]
    public double RotationDegrees { get; init; }

    [JsonPropertyName("flipH")]
    public bool FlipH { get; init; }

    [JsonPropertyName("flipV")]
    public bool FlipV { get; init; }
}

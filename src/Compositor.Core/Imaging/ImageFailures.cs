namespace Compositor.Core.Imaging;

/// <summary>
/// IO failure taxonomy. Names and wording follow upstream
/// <c>ImageImportError</c> / <c>ExportError</c> so a user sees the same diagnosis
/// on either platform; the unsupported-format list is ours (see <see cref="ImageFormatPolicy"/>).
/// </summary>
public enum ImageFailure
{
    /// Bytes exist but no decoder produced pixels (upstream <c>.unreadable</c>).
    Unreadable,

    /// Decodable in principle, but not a format this build handles (upstream <c>.unsupported</c>).
    Unsupported,

    /// Image does not fit the document's remaining pixel budget (upstream import <c>.tooLarge</c>).
    ImportTooLarge,

    /// Canvas outside the export budget (upstream export <c>.tooLarge</c>).
    ExportTooLarge,

    /// The stack could not be rasterised (upstream <c>.render</c>).
    Render,

    /// Encoding to PNG/JPEG failed (upstream <c>.encode</c>).
    Encode,
}

/// <summary>An image IO failure carrying an upstream-shaped, user-presentable message.</summary>
public sealed class ImageException : Exception
{
    public ImageException(ImageFailure failure)
        : this(failure, inner: null)
    {
    }

    public ImageException(ImageFailure failure, Exception? inner)
        : base(MessageFor(failure), inner) => Failure = failure;

    public ImageException(ImageFailure failure, string message)
        : base(message) => Failure = failure;

    public ImageFailure Failure { get; }

    /// The message upstream would show for this failure.
    public static string MessageFor(ImageFailure failure) => failure switch
    {
        ImageFailure.Unreadable => "The image could not be read. It may be damaged or unavailable.",
        ImageFailure.Unsupported => ImageFormatPolicy.UnsupportedImportMessage,
        ImageFailure.ImportTooLarge => "This import exceeds the current 100-megapixel document budget or 30,000-pixel side limit.",
        ImageFailure.ExportTooLarge => "Image export supports canvases up to 100 megapixels and 30,000 pixels per side.",
        ImageFailure.Render => "The canvas could not be rendered. Try a smaller canvas.",
        ImageFailure.Encode => "The image could not be encoded.",
        _ => "The image could not be handled.",
    };
}

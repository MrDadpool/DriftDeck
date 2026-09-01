using System.IO;

namespace DriftDeck.Models;

/// <summary>
/// The rules an image panel follows, kept apart from the control so they can be checked without
/// a window.
/// <para>
/// A panel points at a file rather than holding a copy of it. An image the user chose is their
/// file in their folder, and silently duplicating it into DriftDeck's own storage would grow a
/// folder they never asked for and never see. The cost is that a pinned image can go missing,
/// which the panel reports plainly. Pasted images are the exception — they have no file of
/// their own, so DriftDeck has to write one; that is <see cref="Services.ImageStore"/>.
/// </para>
/// </summary>
public static class ImagePin
{
    /// <summary>
    /// What WPF's own imaging decoders handle. Anything else is refused at the door rather than
    /// producing a broken panel.
    /// </summary>
    public static readonly string[] SupportedExtensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".wdp"];

    public static bool IsSupportedFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path.Trim());
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Picks the first usable image out of a drop, which may carry several files.</summary>
    public static string? FirstSupportedFile(IEnumerable<string>? paths) =>
        paths?.FirstOrDefault(IsSupportedFile);

    /// <summary>
    /// The one line under the image. It names the file so the panel says where its picture came
    /// from, and it is the only place a missing file is reported.
    /// </summary>
    public static string Describe(string? path, bool fileExists)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "No image yet";
        }

        var name = Path.GetFileName(path.Trim());
        return fileExists ? name : $"{name} — file is missing";
    }
}

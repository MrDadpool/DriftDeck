using System.IO;
using System.Windows.Media.Imaging;

namespace DriftDeck.Services;

/// <summary>
/// Holds the only images DriftDeck owns: the ones pasted from the clipboard, which have no file
/// anywhere else. Everything else an image panel shows stays where the user put it.
/// </summary>
public sealed class ImageStore
{
    private readonly string _directory;

    public ImageStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DriftDeck",
            "pasted-images"))
    {
    }

    /// <summary>
    /// Lets a caller point the store at another directory. The only caller that does is the
    /// test suite.
    /// </summary>
    public ImageStore(string directory)
    {
        _directory = directory;
    }

    public string Directory => _directory;

    /// <summary>
    /// Writes a pasted image as PNG and returns its path. PNG rather than JPEG because a paste
    /// is usually a screenshot or a diagram, where re-encoding artefacts are the whole problem.
    /// </summary>
    public string Save(BitmapSource image)
    {
        System.IO.Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{Guid.NewGuid():n}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    /// <summary>
    /// Removes pasted files no panel points at any more. Panels are the only thing that can say
    /// which those are, so the caller supplies the paths that are still in use.
    /// </summary>
    public int RemoveUnreferenced(IEnumerable<string?> referencedPaths)
    {
        if (!System.IO.Directory.Exists(_directory))
        {
            return 0;
        }

        var referenced = referencedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!.Trim()))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removed = 0;
        foreach (var path in System.IO.Directory.EnumerateFiles(_directory, "*.png"))
        {
            if (referenced.Contains(Path.GetFullPath(path)))
            {
                continue;
            }

            try
            {
                File.Delete(path);
                removed++;
            }
            catch (IOException)
            {
                // A file still open somewhere is not worth failing the sweep over.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }
}

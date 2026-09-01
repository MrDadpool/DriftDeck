using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DriftDeck.Models;
using DriftDeck.Services;
using Xunit;

namespace DriftDeck.Tests;

/// <summary>
/// Image panels point at the user's own files and copy nothing, with pasted images as the one
/// exception — they arrive as pixels with no file behind them, so DriftDeck has to write one.
/// These cover which files are accepted, what the caption says, and the store that owns the
/// pasted ones.
/// </summary>
public class ImagePinTests : IDisposable
{
    private readonly string _directory;
    private readonly ImageStore _store;

    public ImagePinTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "DriftDeck.Tests", Guid.NewGuid().ToString("n"));
        _store = new ImageStore(_directory);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private static BitmapSource TinyImage()
    {
        // 2x2 BGRA. Small enough to be free, real enough to encode.
        var pixels = new byte[2 * 2 * 4];
        return BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 2 * 4);
    }

    [Theory]
    [InlineData("shot.png")]
    [InlineData("shot.PNG")]
    [InlineData("photo.jpg")]
    [InlineData("photo.jpeg")]
    [InlineData(@"C:\pictures\map.bmp")]
    [InlineData("scan.tiff")]
    public void IsSupportedFile_accepts_what_the_imaging_decoders_handle(string path) =>
        Assert.True(ImagePin.IsSupportedFile(path));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notes.txt")]
    [InlineData("archive.zip")]
    [InlineData("noextension")]
    [InlineData("shot.png.txt")]
    public void IsSupportedFile_refuses_everything_else(string? path) =>
        Assert.False(ImagePin.IsSupportedFile(path));

    [Fact]
    public void FirstSupportedFile_picks_the_usable_file_out_of_a_mixed_drop()
    {
        Assert.Equal(
            "map.png",
            ImagePin.FirstSupportedFile(["notes.txt", "map.png", "photo.jpg"]));
    }

    [Fact]
    public void FirstSupportedFile_returns_null_when_a_drop_carries_nothing_usable()
    {
        Assert.Null(ImagePin.FirstSupportedFile(["notes.txt", "archive.zip"]));
        Assert.Null(ImagePin.FirstSupportedFile([]));
        Assert.Null(ImagePin.FirstSupportedFile(null));
    }

    [Fact]
    public void Describe_says_nothing_is_pinned_yet()
    {
        Assert.Equal("No image yet", ImagePin.Describe(null, fileExists: false));
        Assert.Equal("No image yet", ImagePin.Describe("   ", fileExists: false));
    }

    [Fact]
    public void Describe_names_the_file_a_panel_is_showing()
    {
        Assert.Equal("map.png", ImagePin.Describe(@"C:\pictures\map.png", fileExists: true));
    }

    [Fact]
    public void Describe_is_the_one_place_a_missing_file_is_reported()
    {
        // The path is kept rather than cleared, so the caption has to say what went wrong.
        Assert.Equal(
            "map.png — file is missing",
            ImagePin.Describe(@"C:\pictures\map.png", fileExists: false));
    }

    [Fact]
    public void A_new_image_panel_starts_with_no_file()
    {
        var panel = PanelDefinition.CreateImagePin(10, 20);

        Assert.Equal(PanelKind.ImagePin, panel.Kind);
        Assert.Equal(string.Empty, panel.ImagePath);
    }

    [Fact]
    public void Save_writes_a_pasted_image_as_a_png_in_the_store()
    {
        var path = _store.Save(TinyImage());

        Assert.True(File.Exists(path));
        Assert.Equal(".png", Path.GetExtension(path));
        Assert.Equal(_directory, Path.GetDirectoryName(path));
    }

    [Fact]
    public void Save_gives_every_paste_its_own_file()
    {
        var first = _store.Save(TinyImage());
        var second = _store.Save(TinyImage());

        Assert.NotEqual(first, second);
        Assert.Equal(2, Directory.GetFiles(_directory, "*.png").Length);
    }

    [Fact]
    public void Save_writes_something_the_decoders_can_read_back()
    {
        var path = _store.Save(TinyImage());

        var decoded = new BitmapImage();
        decoded.BeginInit();
        decoded.CacheOption = BitmapCacheOption.OnLoad;
        decoded.UriSource = new Uri(path);
        decoded.EndInit();

        Assert.Equal(2, decoded.PixelWidth);
        Assert.Equal(2, decoded.PixelHeight);
    }

    [Fact]
    public void RemoveUnreferenced_deletes_only_the_files_no_panel_points_at()
    {
        var kept = _store.Save(TinyImage());
        var dropped = _store.Save(TinyImage());

        Assert.Equal(1, _store.RemoveUnreferenced([kept]));
        Assert.True(File.Exists(kept));
        Assert.False(File.Exists(dropped));
    }

    [Fact]
    public void RemoveUnreferenced_ignores_the_blank_paths_every_other_panel_kind_carries()
    {
        var kept = _store.Save(TinyImage());

        Assert.Equal(0, _store.RemoveUnreferenced([kept, null, "", "   "]));
        Assert.True(File.Exists(kept));
    }

    [Fact]
    public void RemoveUnreferenced_matches_a_path_however_it_was_written()
    {
        var kept = _store.Save(TinyImage());
        var awkward = Path.Combine(_directory, ".", Path.GetFileName(kept));

        Assert.Equal(0, _store.RemoveUnreferenced([awkward]));
        Assert.True(File.Exists(kept));
    }

    [Fact]
    public void RemoveUnreferenced_leaves_a_file_a_panel_still_points_at_after_a_second_sweep()
    {
        var kept = _store.Save(TinyImage());

        _store.RemoveUnreferenced([kept]);
        _store.RemoveUnreferenced([kept]);

        Assert.True(File.Exists(kept));
    }

    [Fact]
    public void RemoveUnreferenced_on_a_store_that_was_never_written_to_does_nothing()
    {
        Assert.Equal(0, _store.RemoveUnreferenced([]));
    }

    [Fact]
    public void RemoveUnreferenced_empties_the_store_when_no_panel_points_at_anything()
    {
        _store.Save(TinyImage());
        _store.Save(TinyImage());

        Assert.Equal(2, _store.RemoveUnreferenced([]));
        Assert.Empty(Directory.GetFiles(_directory, "*.png"));
    }
}

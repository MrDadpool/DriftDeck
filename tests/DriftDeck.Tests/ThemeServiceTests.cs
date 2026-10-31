using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DriftDeck.Services;
using Xunit;

namespace DriftDeck.Tests;

/// <summary>
/// A theme is a local file that renames nothing and adds nothing: it only gives existing slots
/// new values. These cover how each kind of slot is read, that a bad entry costs only that slot,
/// and that turning a theme off or reloading it puts every built-in value back.
/// </summary>
public class ThemeServiceTests : IDisposable
{
    private static readonly Color BuiltInText = Color.FromRgb(0xF2, 0xF7, 0xF8);

    private readonly string _directory;
    private readonly ResourceDictionary _resources;
    private readonly ThemeService _theme;

    public ThemeServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "DriftDeck.Tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);
        _resources = new ResourceDictionary
        {
            ["TextBrush"] = new SolidColorBrush(BuiltInText),
            ["ButtonArtLeftBrush"] = new SolidColorBrush(Colors.Transparent),
            ["SurfaceDeepColor"] = Color.FromRgb(0x09, 0x0F, 0x16),
            ["ButtonArtCapWidth"] = 0d,
            ["ControlRadius"] = new CornerRadius(4),
            ["BodyFont"] = new FontFamily("Segoe UI"),
            ["DurFast"] = new Duration(TimeSpan.FromMilliseconds(120))
        };
        _theme = new ThemeService(_resources, _directory);
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
            // A temp folder left behind is not worth failing a test over.
        }
    }

    private void WriteTheme(string json) => File.WriteAllText(_theme.FilePath, json);

    private Color TextColor => ((SolidColorBrush)_resources["TextBrush"]).Color;

    [Fact]
    public void Without_a_theme_file_nothing_changes()
    {
        var report = _theme.Apply();

        Assert.False(report.IsActive);
        Assert.Empty(report.Problems);
        Assert.Equal(BuiltInText, TextColor);
    }

    [Fact]
    public void Each_slot_is_read_as_the_type_it_already_has()
    {
        WriteTheme("""
            {
              // Comments and trailing commas are allowed: people edit this by hand.
              "name": "Gold",
              "resources": {
                "TextBrush": "#FFD100",
                "SurfaceDeepColor": "#80102030",
                "ButtonArtCapWidth": 17,
                "ControlRadius": 2,
                "BodyFont": "Georgia",
              }
            }
            """);

        var report = _theme.Apply();

        Assert.True(report.IsActive);
        Assert.Equal("Gold", report.Name);
        Assert.Empty(report.Problems);
        Assert.Equal(Color.FromRgb(0xFF, 0xD1, 0x00), TextColor);
        Assert.Equal(Color.FromArgb(0x80, 0x10, 0x20, 0x30), (Color)_resources["SurfaceDeepColor"]);
        Assert.Equal(17d, (double)_resources["ButtonArtCapWidth"]);
        Assert.Equal(new CornerRadius(2), (CornerRadius)_resources["ControlRadius"]);
        Assert.Equal("Georgia", ((FontFamily)_resources["BodyFont"]).Source);
    }

    [Fact]
    public void A_bad_entry_costs_only_its_own_slot()
    {
        WriteTheme("""
            {
              "resources": {
                "NoSuchSlot": "#FFFFFF",
                "ControlRadius": "round",
                "DurFast": 5,
                "TextBrush": "#FFD100"
              }
            }
            """);

        var report = _theme.Apply();

        Assert.Equal(3, report.Problems.Count);
        Assert.Contains(report.Problems, problem => problem.StartsWith("NoSuchSlot:"));
        Assert.Contains(report.Problems, problem => problem.StartsWith("ControlRadius:"));
        Assert.Contains(report.Problems, problem => problem.StartsWith("DurFast:"));
        Assert.Equal(new CornerRadius(4), (CornerRadius)_resources["ControlRadius"]);
        Assert.Equal(Color.FromRgb(0xFF, 0xD1, 0x00), TextColor);
    }

    [Fact]
    public void A_missing_image_keeps_the_built_in_brush()
    {
        WriteTheme("""{ "resources": { "ButtonArtLeftBrush": { "image": "art/missing.blp" } } }""");

        var report = _theme.Apply();

        Assert.Single(report.Problems);
        Assert.IsType<SolidColorBrush>(_resources["ButtonArtLeftBrush"]);
    }

    [Fact]
    public void An_image_brush_crops_in_source_pixels_relative_to_root()
    {
        var art = Path.Combine(_directory, "game", "Interface");
        Directory.CreateDirectory(art);
        SavePng(Path.Combine(art, "button.png"), 128, 32);
        WriteTheme($$"""
            {
              "root": {{JsonValue.Create(Path.Combine(_directory, "game")).ToJsonString()}},
              "resources": {
                "ButtonArtLeftBrush": { "image": "Interface/button.png", "crop": [12, 0, 56, 22], "opacity": 0.5 }
              }
            }
            """);

        var report = _theme.Apply();

        Assert.Empty(report.Problems);
        var brush = Assert.IsType<ImageBrush>(_resources["ButtonArtLeftBrush"]);
        Assert.Equal(BrushMappingMode.RelativeToBoundingBox, brush.ViewboxUnits);
        Assert.Equal(12d / 128, brush.Viewbox.X, 6);
        Assert.Equal(56d / 128, brush.Viewbox.Width, 6);
        Assert.Equal(22d / 32, brush.Viewbox.Height, 6);
        Assert.Equal(0.5, brush.Opacity);
        Assert.True(brush.IsFrozen);
    }

    [Fact]
    public void A_tiled_image_repeats_at_the_given_size()
    {
        SavePng(Path.Combine(_directory, "tile.png"), 8, 8);
        WriteTheme("""{ "resources": { "ButtonArtLeftBrush": { "image": "tile.png", "tile": 64 } } }""");

        _theme.Apply();

        var brush = Assert.IsType<ImageBrush>(_resources["ButtonArtLeftBrush"]);
        Assert.Equal(TileMode.Tile, brush.TileMode);
        Assert.Equal(new Rect(0, 0, 64, 64), brush.Viewport);
        Assert.Equal(BrushMappingMode.Absolute, brush.ViewportUnits);
    }

    [Fact]
    public void A_rotated_tile_turns_about_its_own_centre_so_tiles_stay_on_the_grid()
    {
        SavePng(Path.Combine(_directory, "edge.png"), 8, 8);
        WriteTheme("""{ "resources": { "ButtonArtLeftBrush": { "image": "edge.png", "tile": 24, "rotate": 90 } } }""");

        _theme.Apply();

        var brush = Assert.IsType<ImageBrush>(_resources["ButtonArtLeftBrush"]);
        var rotation = Assert.IsType<RotateTransform>(brush.Transform);
        Assert.Equal((90d, 12d, 12d), (rotation.Angle, rotation.CenterX, rotation.CenterY));
        Assert.True(brush.RelativeTransform.Value.IsIdentity);
    }

    [Fact]
    public void A_rotated_stretched_image_turns_in_relative_space()
    {
        SavePng(Path.Combine(_directory, "corner.png"), 8, 8);
        WriteTheme("""{ "resources": { "ButtonArtLeftBrush": { "image": "corner.png", "rotate": 180 } } }""");

        _theme.Apply();

        var brush = Assert.IsType<ImageBrush>(_resources["ButtonArtLeftBrush"]);
        var rotation = Assert.IsType<RotateTransform>(brush.RelativeTransform);
        Assert.Equal((180d, 0.5, 0.5), (rotation.Angle, rotation.CenterX, rotation.CenterY));
    }

    [Fact]
    public void A_rotation_that_is_not_a_quarter_turn_is_reported()
    {
        SavePng(Path.Combine(_directory, "edge.png"), 8, 8);
        WriteTheme("""{ "resources": { "ButtonArtLeftBrush": { "image": "edge.png", "rotate": 45 } } }""");

        var report = _theme.Apply();

        Assert.IsType<SolidColorBrush>(_resources["ButtonArtLeftBrush"]);
        Assert.Contains(report.Problems, problem => problem.Contains("rotate"));
    }

    [Fact]
    public void Turning_the_theme_off_restores_every_built_in_value()
    {
        WriteTheme("""{ "resources": { "TextBrush": "#FFD100", "ControlRadius": 0 } }""");
        _theme.Apply();

        _theme.SetEnabled(false);
        var report = _theme.Apply();

        Assert.False(report.IsActive);
        Assert.Equal(BuiltInText, TextColor);
        Assert.Equal(new CornerRadius(4), (CornerRadius)_resources["ControlRadius"]);
    }

    [Fact]
    public void Reloading_drops_slots_the_edited_theme_no_longer_names()
    {
        WriteTheme("""{ "resources": { "TextBrush": "#FFD100", "ControlRadius": 0 } }""");
        _theme.Apply();

        WriteTheme("""{ "resources": { "ControlRadius": 1 } }""");
        _theme.Apply();

        Assert.Equal(BuiltInText, TextColor);
        Assert.Equal(new CornerRadius(1), (CornerRadius)_resources["ControlRadius"]);
    }

    [Fact]
    public void Unreadable_json_is_reported_not_thrown()
    {
        WriteTheme("{ not json");

        var report = _theme.Apply();

        Assert.Single(report.Problems);
        Assert.Equal(BuiltInText, TextColor);
    }

    [Fact]
    public void Using_a_game_folder_writes_the_preset_with_that_root()
    {
        _theme.UseGameFolder(@"C:\Games\World of Warcraft\_example_");

        var written = JsonNode.Parse(File.ReadAllText(_theme.FilePath))!.AsObject();
        Assert.Equal(@"C:\Games\World of Warcraft\_example_", written["root"]!.GetValue<string>());
        Assert.True(written["enabled"]!.GetValue<bool>());
        Assert.NotNull(written["resources"]!["ButtonArtLeftBrush"]);
    }

    [Fact]
    public void Using_a_game_folder_keeps_an_edited_theme()
    {
        WriteTheme("""{ "name": "Mine", "enabled": false, "resources": { "TextBrush": "#123456" } }""");

        _theme.UseGameFolder(@"D:\Game");

        var written = JsonNode.Parse(File.ReadAllText(_theme.FilePath))!.AsObject();
        Assert.Equal("Mine", written["name"]!.GetValue<string>());
        Assert.Equal("#123456", written["resources"]!["TextBrush"]!.GetValue<string>());
        Assert.Equal(@"D:\Game", written["root"]!.GetValue<string>());
        Assert.True(written["enabled"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData(-1, 0, 10, 10)]
    [InlineData(0, 0, 0, 10)]
    [InlineData(120, 0, 16, 10)]
    [InlineData(0, 30, 10, 4)]
    public void A_crop_outside_the_image_is_refused(int x, int y, int width, int height)
    {
        Assert.Throws<ArgumentException>(() => ThemeService.CropToViewbox(new Int32Rect(x, y, width, height), 128, 32));
    }

    [Fact]
    public void Relative_paths_accept_either_slash()
    {
        var expected = Path.GetFullPath(Path.Combine(_directory, "Fonts", "FRIZQT__.TTF"));

        Assert.Equal(expected, ThemeService.ResolvePath(_directory, "Fonts/FRIZQT__.TTF"));
        Assert.Equal(expected, ThemeService.ResolvePath(_directory, @"Fonts\FRIZQT__.TTF"));
    }

    private static void SavePng(string path, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}

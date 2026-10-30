using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DriftDeck.Services;

/// <summary>What the last <see cref="ThemeService.Apply"/> did, in words the status strip can show.</summary>
public sealed record ThemeReport(string? Name, IReadOnlyList<string> Problems)
{
    public static readonly ThemeReport Off = new(null, []);

    public bool IsActive => Name is not null;
}

/// <summary>
/// Loads an optional local theme from <c>%LOCALAPPDATA%\DriftDeck\theme\theme.json</c> and swaps
/// it into the application resources.
///
/// The theme names slots that already exist in <c>App.xaml</c> and gives each a new value; the
/// slot's current type decides how the value is read, so a brush accepts a colour or an image, a
/// font accepts a font file, and so on. Every slot is referenced dynamically, which lets a reload
/// repaint open windows without restarting.
///
/// Images and fonts are read from wherever the theme points, typically a game's own folder. That
/// is the whole reason the theme is a local file: it lets someone dress DriftDeck in artwork they
/// are entitled to use on their own machine without that artwork ever entering this repository
/// or its installer.
/// </summary>
public sealed class ThemeService
{
    public const string FileName = "theme.json";
    private const string GameThemeResource = "DriftDeck.Themes.game.theme.json";

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ResourceDictionary _resources;
    private readonly Dictionary<string, object> _defaults = new(StringComparer.Ordinal);

    public ThemeService(ResourceDictionary resources)
        : this(resources, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DriftDeck",
            "theme"))
    {
    }

    /// <summary>
    /// Lets a caller point the service at another directory. The only caller that does is the
    /// test suite.
    /// </summary>
    public ThemeService(ResourceDictionary resources, string directory)
    {
        _resources = resources;
        Directory = directory;
    }

    public string Directory { get; }

    public string FilePath => Path.Combine(Directory, FileName);

    public ThemeReport LastReport { get; private set; } = ThemeReport.Off;

    /// <summary>
    /// Restores every slot the previous theme changed, then applies the theme file if there is
    /// one and it is switched on. A slot that cannot be read keeps the built-in value and is
    /// reported, so one missing image never leaves a button without a face.
    /// </summary>
    public ThemeReport Apply()
    {
        RestoreDefaults();
        LastReport = Load();
        return LastReport;
    }

    private ThemeReport Load()
    {
        if (!File.Exists(FilePath))
        {
            return ThemeReport.Off;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(FilePath), ReadOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ThemeReport("Theme", [$"{FileName} could not be read: {exception.Message}"]);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new ThemeReport("Theme", [$"{FileName} must be a JSON object."]);
            }

            if (root.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False)
            {
                return ThemeReport.Off;
            }

            var name = root.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()!
                : "Theme";
            var baseDirectory = root.TryGetProperty("root", out var rootElement) && rootElement.ValueKind == JsonValueKind.String
                ? rootElement.GetString()!
                : Directory;

            var problems = new List<string>();
            if (root.TryGetProperty("resources", out var resources) && resources.ValueKind == JsonValueKind.Object)
            {
                var images = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
                foreach (var slot in resources.EnumerateObject())
                {
                    ApplySlot(slot, baseDirectory, images, problems);
                }
            }

            return new ThemeReport(name, problems);
        }
    }

    private void ApplySlot(JsonProperty slot, string baseDirectory, Dictionary<string, BitmapSource> images, List<string> problems)
    {
        if (!_resources.Contains(slot.Name))
        {
            problems.Add($"{slot.Name}: not a theme slot");
            return;
        }

        var current = _resources[slot.Name];
        try
        {
            var value = ReadValue(current, slot.Value, baseDirectory, images);
            if (!_defaults.ContainsKey(slot.Name))
            {
                _defaults[slot.Name] = current;
            }
            _resources[slot.Name] = value;
        }
        // Deliberately broad. Image and font decoding sit on WIC and DirectWrite, which report a
        // corrupt file as a COMException or worse, and this runs during startup: one bad file in
        // a user's theme must cost one slot, not every launch.
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            problems.Add($"{slot.Name}: {exception.Message}");
        }
    }

    private void RestoreDefaults()
    {
        foreach (var (key, value) in _defaults)
        {
            _resources[key] = value;
        }
        _defaults.Clear();
    }

    private static object ReadValue(object current, JsonElement value, string baseDirectory, Dictionary<string, BitmapSource> images) =>
        current switch
        {
            Color => ParseColor(value),
            Brush => ReadBrush(value, baseDirectory, images),
            double => ReadNumber(value),
            CornerRadius => new CornerRadius(ReadNumber(value)),
            FontFamily => ReadFont(value, baseDirectory),
            _ => throw new NotSupportedException($"slots of type {current.GetType().Name} cannot be themed")
        };

    private static Color ParseColor(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException("expected a colour such as \"#FFD100\"");
        }

        return ParseColor(value.GetString()!);
    }

    /// <summary>Accepts <c>#RRGGBB</c>, <c>#AARRGGBB</c>, and WPF's named colours.</summary>
    public static Color ParseColor(string text)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(text.Trim());
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException)
        {
            throw new FormatException($"‘{text}’ is not a colour");
        }
    }

    private static double ReadNumber(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && number >= 0 && double.IsFinite(number)
            ? number
            : throw new FormatException("expected a number of 0 or more");

    /// <summary>
    /// A brush is either a colour string or an image object:
    /// <c>{ "image": "path", "crop": [x, y, w, h], "tile": 64, "opacity": 0.9 }</c>.
    /// <c>crop</c> is in source pixels, so slices can be copied straight from the game's own
    /// texture coordinates; <c>tile</c> repeats the image at that size instead of stretching it.
    /// </summary>
    private static Brush ReadBrush(JsonElement value, string baseDirectory, Dictionary<string, BitmapSource> images)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var solid = new SolidColorBrush(ParseColor(value.GetString()!));
            solid.Freeze();
            return solid;
        }

        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("image", out var imageElement)
            || imageElement.ValueKind != JsonValueKind.String)
        {
            throw new FormatException("expected a colour or an object with an \"image\" path");
        }

        var path = ResolvePath(baseDirectory, imageElement.GetString()!);
        if (!images.TryGetValue(path, out var image))
        {
            image = LoadImage(path);
            images[path] = image;
        }

        var brush = new ImageBrush(image) { Stretch = Stretch.Fill };
        if (value.TryGetProperty("crop", out var crop))
        {
            brush.Viewbox = CropToViewbox(ReadCrop(crop), image.PixelWidth, image.PixelHeight);
            brush.ViewboxUnits = BrushMappingMode.RelativeToBoundingBox;
        }
        if (value.TryGetProperty("tile", out var tile))
        {
            var size = ReadNumber(tile);
            if (size < 1)
            {
                throw new FormatException("tile must be at least 1");
            }
            brush.TileMode = TileMode.Tile;
            brush.Viewport = new Rect(0, 0, size, size);
            brush.ViewportUnits = BrushMappingMode.Absolute;
        }
        if (value.TryGetProperty("opacity", out var opacity))
        {
            brush.Opacity = Math.Clamp(ReadNumber(opacity), 0, 1);
        }

        brush.Freeze();
        return brush;
    }

    private static Int32Rect ReadCrop(JsonElement crop)
    {
        if (crop.ValueKind != JsonValueKind.Array || crop.GetArrayLength() != 4)
        {
            throw new FormatException("crop must be [x, y, width, height]");
        }

        var parts = crop.EnumerateArray().Select(part => part.TryGetInt32(out var number)
            ? number
            : throw new FormatException("crop values must be whole pixels")).ToArray();
        return new Int32Rect(parts[0], parts[1], parts[2], parts[3]);
    }

    /// <summary>
    /// Converts a crop in source pixels to a relative viewbox. Relative rather than absolute
    /// because absolute viewbox units are device-independent pixels, which differ from source
    /// pixels for any image not saved at 96 dpi.
    /// </summary>
    public static Rect CropToViewbox(Int32Rect crop, int pixelWidth, int pixelHeight)
    {
        if (crop.Width <= 0 || crop.Height <= 0 || crop.X < 0 || crop.Y < 0
            || crop.X + crop.Width > pixelWidth || crop.Y + crop.Height > pixelHeight)
        {
            throw new ArgumentException($"crop [{crop.X}, {crop.Y}, {crop.Width}, {crop.Height}] is outside the {pixelWidth} × {pixelHeight} image");
        }

        return new Rect(
            (double)crop.X / pixelWidth,
            (double)crop.Y / pixelHeight,
            (double)crop.Width / pixelWidth,
            (double)crop.Height / pixelHeight);
    }

    private static BitmapSource LoadImage(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{path} does not exist");
        }

        if (string.Equals(Path.GetExtension(path), ".blp", StringComparison.OrdinalIgnoreCase))
        {
            return BlpDecoder.Load(path);
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>
    /// A font slot takes either an installed family name or a font file. A file is loaded in
    /// place, without installing it, by reading the family name out of the file itself.
    /// </summary>
    private static FontFamily ReadFont(JsonElement value, string baseDirectory)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException("expected a font file path or a font family name");
        }

        var text = value.GetString()!;
        var extension = Path.GetExtension(text);
        if (!extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".otf", StringComparison.OrdinalIgnoreCase))
        {
            return new FontFamily(text);
        }

        var path = ResolvePath(baseDirectory, text);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{path} does not exist");
        }

        var typeface = new GlyphTypeface(new Uri(path));
        var family = typeface.FamilyNames.TryGetValue(CultureInfo.GetCultureInfo("en-US"), out var english)
            ? english
            : typeface.FamilyNames.Values.FirstOrDefault()
              ?? throw new InvalidDataException($"{Path.GetFileName(path)} has no family name");
        // The folder form is the documented one, and enumerating it returns only families WPF
        // could actually read, so a font that would silently fall back is reported instead.
        var folder = new Uri(Path.GetDirectoryName(path)! + Path.DirectorySeparatorChar);
        return Fonts.GetFontFamilies(folder).FirstOrDefault(candidate =>
                   candidate.FamilyNames.Values.Contains(family, StringComparer.OrdinalIgnoreCase))
               ?? throw new InvalidDataException($"{Path.GetFileName(path)} could not be loaded as ‘{family}’");
    }

    /// <summary>Relative paths are relative to the theme's <c>root</c>; either slash works.</summary>
    public static string ResolvePath(string baseDirectory, string path)
    {
        var normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(normalized) ? normalized : Path.Combine(baseDirectory, normalized));
    }

    /// <summary>
    /// Points the theme at a game folder. A theme the user has already edited keeps every edit:
    /// only <c>root</c> and <c>enabled</c> change. Without one, the bundled game preset is
    /// written, which names art and font files by path and contains none of them.
    /// </summary>
    public void UseGameFolder(string folder)
    {
        System.IO.Directory.CreateDirectory(Directory);
        JsonObject theme;
        if (File.Exists(FilePath))
        {
            theme = JsonNode.Parse(File.ReadAllText(FilePath), documentOptions: ReadOptions) as JsonObject
                    ?? throw new InvalidDataException($"{FileName} must be a JSON object.");
        }
        else
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(GameThemeResource)
                               ?? throw new InvalidOperationException("The game theme preset is missing from this build.");
            theme = JsonNode.Parse(stream, documentOptions: ReadOptions) as JsonObject
                    ?? throw new InvalidDataException("The game theme preset is not a JSON object.");
        }

        theme["root"] = folder;
        theme["enabled"] = true;
        Write(theme);
    }

    /// <summary>Switches the theme on or off without touching anything else in the file.</summary>
    public void SetEnabled(bool enabled)
    {
        if (!File.Exists(FilePath))
        {
            return;
        }

        var theme = JsonNode.Parse(File.ReadAllText(FilePath), documentOptions: ReadOptions) as JsonObject
                    ?? throw new InvalidDataException($"{FileName} must be a JSON object.");
        theme["enabled"] = enabled;
        Write(theme);
    }

    private void Write(JsonObject theme) =>
        File.WriteAllText(FilePath, theme.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}

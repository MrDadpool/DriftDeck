using System.IO;
using System.Text.Json;
using DriftDeck.Models;

namespace DriftDeck.Services;

public sealed class LayoutStore
{
    private const string LastLayoutFileName = "last-layout.txt";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _layoutDirectory;

    public LayoutStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _layoutDirectory = Path.Combine(appData, "DriftDeck", "layouts");
    }

    public async Task<OverlayLayout> LoadLastAsync()
    {
        var lastNamePath = Path.Combine(_layoutDirectory, LastLayoutFileName);
        try
        {
            var name = File.Exists(lastNamePath)
                ? (await File.ReadAllTextAsync(lastNamePath)).Trim()
                : "Default";
            return await LoadAsync(string.IsNullOrWhiteSpace(name) ? "Default" : name);
        }
        catch (IOException)
        {
            return OverlayLayout.CreateDefault();
        }
    }

    public async Task<OverlayLayout> LoadAsync(string name)
    {
        var safeName = NormalizeName(name);
        var filePath = GetLayoutPath(safeName);
        try
        {
            if (!File.Exists(filePath))
            {
                var freshLayout = OverlayLayout.CreateDefault();
                freshLayout.Name = safeName;
                return freshLayout;
            }

            await using var stream = File.OpenRead(filePath);
            var layout = await JsonSerializer.DeserializeAsync<OverlayLayout>(stream, JsonOptions)
                         ?? OverlayLayout.CreateDefault();
            layout.Name = safeName;
            layout.Panels ??= [];
            if (layout.Version < 2)
            {
                foreach (var panel in layout.Panels)
                {
                    panel.X += layout.Left;
                    panel.Y += layout.Top + 94;
                }

                layout.Version = 2;
                layout.Height = 94;
            }
            if (layout.Panels.Count == 0)
            {
                layout.Panels.Add(PanelDefinition.CreateBrowser(24, 24));
            }

            return layout;
        }
        catch (JsonException)
        {
            return OverlayLayout.CreateDefault();
        }
        catch (IOException)
        {
            return OverlayLayout.CreateDefault();
        }
    }

    public async Task SaveAsync(OverlayLayout layout)
    {
        layout.Name = NormalizeName(layout.Name);
        Directory.CreateDirectory(_layoutDirectory);

        var filePath = GetLayoutPath(layout.Name);
        var temporaryPath = filePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, layout, JsonOptions);
        }

        File.Move(temporaryPath, filePath, true);
        await File.WriteAllTextAsync(Path.Combine(_layoutDirectory, LastLayoutFileName), layout.Name);
    }

    /// <summary>
    /// Writes the layout under a different name without changing which layout is "current",
    /// so duplicating never moves the user off the layout they are editing.
    /// </summary>
    public async Task SaveCopyAsync(OverlayLayout layout, string copyName)
    {
        var originalName = layout.Name;
        Directory.CreateDirectory(_layoutDirectory);
        var safeName = NormalizeName(copyName);
        var filePath = GetLayoutPath(safeName);
        var temporaryPath = filePath + ".tmp";
        try
        {
            layout.Name = safeName;
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, layout, JsonOptions);
            }

            File.Move(temporaryPath, filePath, true);
        }
        finally
        {
            layout.Name = originalName;
        }
    }

    public bool Exists(string? name) => File.Exists(GetLayoutPath(NormalizeName(name)));

    public IReadOnlyList<string> ListNames()
    {
        try
        {
            if (!Directory.Exists(_layoutDirectory))
            {
                return ["Default"];
            }

            var names = Directory.EnumerateFiles(_layoutDirectory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!names.Contains("Default", StringComparer.OrdinalIgnoreCase))
            {
                names.Insert(0, "Default");
            }

            return names;
        }
        catch (IOException)
        {
            return ["Default"];
        }
    }

    public static string NormalizeName(string? name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "Default" : name.Trim();
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var normalized = new string(trimmed
            .Where(character => !invalidCharacters.Contains(character))
            .Take(40)
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "Default" : normalized;
    }

    public bool Delete(string name)
    {
        var safeName = NormalizeName(name);
        if (safeName.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var filePath = GetLayoutPath(safeName);
            if (!File.Exists(filePath))
            {
                return false;
            }

            File.Delete(filePath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string GetLayoutPath(string name) => Path.Combine(_layoutDirectory, $"{NormalizeName(name)}.json");
}

using System.IO;
using System.Text.Json;
using DriftDeck.Models;

namespace DriftDeck.Services;

public sealed class LayoutStore
{
    private const string LastLayoutFileName = "last-layout.txt";
    public string? LoadWarning { get; private set; }
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly string _layoutDirectory;

    public LayoutStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DriftDeck",
            "layouts"))
    {
    }

    /// <summary>
    /// Lets a caller point the store at another directory. The only caller that does is the
    /// test suite, which needs a store that reads and writes somewhere disposable rather than
    /// the user's real layout folder.
    /// </summary>
    public LayoutStore(string layoutDirectory)
    {
        _layoutDirectory = layoutDirectory;
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LoadWarning = $"Could not restore the last layout: {exception.Message}";
            return OverlayLayout.CreateDefault();
        }
    }

    public Task<OverlayLayout> LoadAsync(string name)
    {
        var safeName = NormalizeName(name);
        var layout = JsonFile.Read<OverlayLayout>(GetLayoutPath(safeName), out var warning)
                     ?? OverlayLayout.CreateDefault();
        LoadWarning = warning;
        layout.Name = safeName;
        layout.Panels ??= [];
        layout.RecentUrls ??= [];
        if (layout.Version < 2)
        {
            foreach (var panel in layout.Panels)
            {
                panel.X += layout.Left;
                panel.Y += layout.Top + 94;
            }
            layout.Height = 94;
        }
        layout.Version = 3;
        return Task.FromResult(layout);
    }

    public Task SaveAsync(OverlayLayout layout)
    {
        layout.Name = NormalizeName(layout.Name);
        return SaveSnapshotAsync(layout, layout.Name, makeCurrent: true);
    }

    public Task SaveCopyAsync(OverlayLayout layout, string copyName) =>
        SaveSnapshotAsync(layout, copyName, makeCurrent: false);

    private async Task SaveSnapshotAsync(OverlayLayout layout, string name, bool makeCurrent)
    {
        // A copy must not rename the live layout while another save is suspended.
        var snapshot = JsonSerializer.Deserialize<OverlayLayout>(JsonSerializer.Serialize(layout))!;
        snapshot.Name = NormalizeName(name);
        await _writes.WaitAsync();
        try
        {
            await JsonFile.WriteAsync(GetLayoutPath(snapshot.Name), snapshot, overwrite: makeCurrent);
            if (makeCurrent)
            {
                var lastPath = Path.Combine(_layoutDirectory, LastLayoutFileName);
                await File.WriteAllTextAsync(lastPath + ".tmp", snapshot.Name);
                File.Move(lastPath + ".tmp", lastPath, true);
            }
        }
        finally
        {
            _writes.Release();
        }
    }

    public string SuggestCopyName(string name)
    {
        var stem = NormalizeName(name);
        for (var number = 1; ; number++)
        {
            var suffix = number == 1 ? " Copy" : $" Copy {number}";
            var candidate = stem[..Math.Min(stem.Length, 40 - suffix.Length)] + suffix;
            if (!Exists(candidate)) return candidate;
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ["Default"];
        }
    }

    public static string NormalizeName(string? name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "Default" : name.Trim();
        var invalidCharacters = Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*").ToHashSet();
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

            File.Delete(filePath + ".bak");
            File.Delete(filePath + ".tmp");
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

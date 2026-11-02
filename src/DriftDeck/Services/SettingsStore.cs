using System.IO;
using DriftDeck.Models;

namespace DriftDeck.Services;

public sealed class SettingsStore
{
    private readonly string _filePath;
    public string? LoadWarning { get; private set; }

    public SettingsStore() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DriftDeck", "settings.json"))
    {
    }

    public SettingsStore(string filePath)
    {
        _filePath = filePath;
    }

    public AppSettings Load()
    {
        var settings = JsonFile.Read<AppSettings>(_filePath, out var warning);
        LoadWarning = warning;
        settings ??= new AppSettings();
        settings.Bookmarks ??= [];
        settings.LayoutRules ??= [];
        settings.QuickLayouts ??= [];
        return settings;
    }

    public Task SaveAsync(AppSettings settings) => JsonFile.WriteAsync(_filePath, settings);
}

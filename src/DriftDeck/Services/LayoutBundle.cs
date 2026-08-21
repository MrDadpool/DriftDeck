using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DriftDeck.Models;

namespace DriftDeck.Services;

/// <summary>
/// Every saved layout in one file, so a workspace can be backed up, moved to another machine, or
/// handed to someone else. Layouts otherwise exist only as loose JSON under
/// <c>%LOCALAPPDATA%</c>, which is not somewhere a user can be expected to go digging.
/// </summary>
public sealed class LayoutBundle
{
    public const string FormatMarker = "driftdeck.layouts";
    public const string FileExtension = ".driftdeck";

    /// <summary>Identifies the file as ours before anything in it is trusted.</summary>
    public string Format { get; set; } = FormatMarker;

    public int Version { get; set; } = 1;

    public string ExportedAt { get; set; } = string.Empty;

    public List<OverlayLayout> Layouts { get; set; } = [];
}

/// <summary>Outcome of an import, so the caller can say what actually happened.</summary>
public readonly record struct ImportResult(int Added, int Renamed, string? Error)
{
    public bool Failed => Error is not null;
}

public static class LayoutBundleStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>Writes every saved layout to <paramref name="filePath"/>.</summary>
    public static async Task<int> ExportAsync(LayoutStore store, string filePath)
    {
        var bundle = new LayoutBundle
        {
            ExportedAt = DateTime.UtcNow.ToString("u"),
            Layouts = []
        };

        foreach (var name in store.ListNames())
        {
            if (!store.Exists(name))
            {
                // A name is listed for "Default" even before it has been saved once.
                continue;
            }

            bundle.Layouts.Add(await store.LoadAsync(name));
        }

        var temporaryPath = filePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, bundle, Options);
        }

        File.Move(temporaryPath, filePath, true);
        return bundle.Layouts.Count;
    }

    /// <summary>
    /// Adds the file's layouts to the local set. An imported name that already exists is saved
    /// alongside the local one rather than over it: silently replacing a layout someone has been
    /// building for weeks is not a recoverable mistake.
    /// </summary>
    public static async Task<ImportResult> ImportAsync(LayoutStore store, string filePath)
    {
        LayoutBundle? bundle;
        try
        {
            await using var stream = File.OpenRead(filePath);
            bundle = await JsonSerializer.DeserializeAsync<LayoutBundle>(stream, Options);
        }
        catch (JsonException)
        {
            return new ImportResult(0, 0, "That file is not readable as a DriftDeck layout bundle.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ImportResult(0, 0, $"That file could not be read: {exception.Message}");
        }

        if (bundle is null || !string.Equals(bundle.Format, LayoutBundle.FormatMarker, StringComparison.Ordinal))
        {
            return new ImportResult(0, 0, "That file is not a DriftDeck layout bundle.");
        }

        if (bundle.Layouts.Count == 0)
        {
            return new ImportResult(0, 0, "That bundle contains no layouts.");
        }

        var added = 0;
        var renamed = 0;
        foreach (var layout in bundle.Layouts)
        {
            layout.Panels ??= [];
            var wanted = LayoutStore.NormalizeName(layout.Name);
            var target = wanted;
            if (store.Exists(target))
            {
                target = UniqueName(store, wanted);
                renamed++;
            }

            try
            {
                await store.SaveCopyAsync(layout, target);
                added++;
            }
            catch (IOException exception)
            {
                return new ImportResult(added, renamed, $"‘{target}’ could not be written: {exception.Message}");
            }
        }

        return new ImportResult(added, renamed, null);
    }

    private static string UniqueName(LayoutStore store, string wanted)
    {
        var candidate = LayoutStore.NormalizeName($"{wanted} (imported)");
        var suffix = 2;
        while (store.Exists(candidate))
        {
            candidate = LayoutStore.NormalizeName($"{wanted} (imported {suffix++})");
        }

        return candidate;
    }
}

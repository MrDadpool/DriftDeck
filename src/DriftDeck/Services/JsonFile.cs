using System.IO;
using System.Text.Json;

namespace DriftDeck.Services;

// Settings and layouts share the same atomic write and last-good-copy recovery rules.
internal static class JsonFile
{
    private static readonly SemaphoreSlim Writes = new(1, 1);
    internal static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static T? Read<T>(string path, out string? warning) where T : class
    {
        warning = null;
        try
        {
            var saved = ReadFile<T>(path);
            if (saved is not null) return saved;
        }
        catch (Exception exception) when (IsStorageError(exception))
        {
            warning = $"Could not read {Path.GetFileName(path)}: {exception.Message}";
        }

        try
        {
            var recovered = ReadFile<T>(path + ".bak");
            if (recovered is not null)
            {
                warning = $"Recovered {Path.GetFileName(path)} from its last good backup.";
                return recovered;
            }
        }
        catch (Exception exception) when (IsStorageError(exception))
        {
            // Keep both originals on disk for recovery when neither can be read.
        }

        return null;
    }

    private static T? ReadFile<T>(string path) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
                ?? throw new JsonException("The saved file contains no data.");
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public static Task WriteAsync<T>(string path, T value, bool overwrite = true)
    {
        // Capture before yielding: live panel/settings objects may change while a write waits.
        var json = JsonSerializer.Serialize(value, Options);
        return WriteSnapshotAsync<T>(path, json, overwrite);
    }

    private static async Task WriteSnapshotAsync<T>(string path, string json, bool overwrite)
    {
        await Writes.WaitAsync();
        var temporaryPath = path + ".tmp";
        try
        {
            if (!overwrite && File.Exists(path))
                throw new IOException($"A saved file named {Path.GetFileName(path)} already exists.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(temporaryPath, json);
            if (File.Exists(path))
            {
                try
                {
                    var previous = JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path), Options);
                    if (previous is null) throw new JsonException("The saved file contains no data.");
                }
                catch (JsonException)
                {
                    // Never rotate a corrupt primary over a usable backup.
                    File.Move(path, path + $".corrupt-{Guid.NewGuid():N}");
                    File.Move(temporaryPath, path);
                    return;
                }

                File.Replace(temporaryPath, path, path + ".bak");
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            Writes.Release();
        }
    }

    private static bool IsStorageError(Exception exception) =>
        exception is JsonException or IOException or UnauthorizedAccessException;
}

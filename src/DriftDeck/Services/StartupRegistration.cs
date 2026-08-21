using Microsoft.Win32;

namespace DriftDeck.Services;

/// <summary>
/// Starts DriftDeck with Windows, via the per-user <c>Run</c> key.
/// <para>
/// An overlay you have to remember to launch is an overlay you forget, and the app it belongs
/// over is usually started from somewhere else entirely. The per-user key was chosen over a
/// Startup-folder shortcut because it needs no COM shell interop to create, and over any
/// machine-wide key because DriftDeck is a portable folder owned by one user and must never
/// need elevation.
/// </para>
/// <para>
/// The registry is treated as the truth rather than a copy in settings.json: the user can turn
/// this off from Task Manager's Startup tab, and a stored duplicate would then disagree with
/// reality. Note that disabling it there leaves this value in place while Windows declines to
/// run it, so <see cref="IsEnabled"/> means "registered", not "will definitely run".
/// </para>
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DriftDeck";

    /// <summary>The executable Windows should launch, quoted for paths containing spaces.</summary>
    public static string? CommandLine
    {
        get
        {
            var path = Environment.ProcessPath;
            return string.IsNullOrEmpty(path) ? null : $"\"{path}\"";
        }
    }

    /// <summary>True when a value for DriftDeck exists under the per-user Run key.</summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) is string value && value.Length > 0;
            }
            catch (Exception exception) when (exception is System.Security.SecurityException
                                                  or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// True when the registered command no longer names this executable, which is what happens
    /// when a portable folder is moved or renamed. The entry then silently launches nothing.
    /// </summary>
    public static bool IsStale
    {
        get
        {
            var wanted = CommandLine;
            if (wanted is null)
            {
                return false;
            }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) is string value
                       && !string.Equals(value, wanted, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (exception is System.Security.SecurityException
                                                  or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Adds or removes the entry. Returns the failure reason, or null on success — the caller
    /// reports it in the status strip rather than throwing: failing to register a convenience
    /// must never stop the overlay from running.
    /// </summary>
    public static string? Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return "The Windows startup list could not be opened.";
            }

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return null;
            }

            var command = CommandLine;
            if (command is null)
            {
                return "DriftDeck could not determine its own location.";
            }

            key.SetValue(ValueName, command, RegistryValueKind.String);
            return null;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException
                                              or UnauthorizedAccessException
                                              or System.IO.IOException)
        {
            return exception.Message;
        }
    }

    /// <summary>Rewrites a stale entry to the current location. Does nothing when not registered.</summary>
    public static bool RefreshIfStale()
    {
        if (!IsEnabled || !IsStale)
        {
            return false;
        }

        return Apply(true) is null;
    }
}

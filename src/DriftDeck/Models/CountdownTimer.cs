using System.Globalization;

namespace DriftDeck.Models;

/// <summary>
/// The state of a timer panel, as a value with pure transitions.
/// <para>
/// A running timer is stored as the instant it ends rather than as a tick count, so nothing has
/// to run for it to stay correct: the panel can be shaded, the layout saved, or the process
/// restarted, and the remaining time is still whatever the clock says. That is also what keeps
/// the ~650 ms layout save off the per-second path — the timer writes on start, pause, and
/// reset, never on a tick.
/// </para>
/// </summary>
public readonly record struct TimerState(int DurationSeconds, int RemainingSeconds, DateTime? EndUtc)
{
    /// <summary>A day. Past this the panel is the wrong tool and the field is the wrong size.</summary>
    public const int MaximumDurationSeconds = 24 * 60 * 60;

    public const int DefaultDurationSeconds = 5 * 60;

    public bool IsRunning => EndUtc is not null;

    public static TimerState FromDuration(int durationSeconds)
    {
        var clamped = Math.Clamp(durationSeconds, 1, MaximumDurationSeconds);
        return new TimerState(clamped, clamped, null);
    }

    /// <summary>
    /// Seconds left at <paramref name="utcNow"/>, floored at zero. A paused timer ignores the
    /// clock entirely.
    /// </summary>
    public int RemainingAt(DateTime utcNow) =>
        EndUtc is { } end
            ? Math.Max(0, (int)Math.Ceiling((end - utcNow).TotalSeconds))
            : Math.Max(0, RemainingSeconds);

    public bool IsFinishedAt(DateTime utcNow) => RemainingAt(utcNow) == 0;

    /// <summary>
    /// Starts, or resumes a pause. Starting from zero restarts the full duration, so the button
    /// never becomes a no-op the user has to work out.
    /// </summary>
    public TimerState Start(DateTime utcNow)
    {
        if (IsRunning)
        {
            return this;
        }

        var seconds = RemainingSeconds > 0 ? RemainingSeconds : DurationSeconds;
        return this with { RemainingSeconds = seconds, EndUtc = utcNow.AddSeconds(seconds) };
    }

    public TimerState Pause(DateTime utcNow) =>
        IsRunning
            ? this with { RemainingSeconds = RemainingAt(utcNow), EndUtc = null }
            : this;

    public TimerState Reset() => this with { RemainingSeconds = DurationSeconds, EndUtc = null };

    /// <summary>Changes the configured length. A running timer is stopped: the old end instant
    /// no longer means anything once the length it was derived from has changed.</summary>
    public TimerState WithDuration(int durationSeconds) => FromDuration(durationSeconds);

    /// <summary>
    /// <c>m:ss</c> under an hour and <c>h:mm:ss</c> at or over one. Minutes are not zero-padded
    /// in the short form, which is how a stopwatch reads.
    /// </summary>
    public static string Format(int seconds)
    {
        seconds = Math.Max(0, seconds);
        var span = TimeSpan.FromSeconds(seconds);
        return seconds >= 3600
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{(int)span.TotalMinutes}:{span.Seconds:00}";
    }

    /// <summary>
    /// Reads what the user typed into the duration box. A bare number is minutes — the unit
    /// almost every timer is set in — while colons mean exactly what they look like.
    /// </summary>
    public static bool TryParseDuration(string? text, out int seconds)
    {
        seconds = 0;
        var parts = (text ?? string.Empty)
            .Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is 0 or > 3)
        {
            return false;
        }

        var values = new int[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            // Only the leading field may exceed 59: "90:00" is a legitimate 90 minutes.
            if (index > 0 && value > 59)
            {
                return false;
            }

            values[index] = value;
        }

        var total = parts.Length switch
        {
            1 => values[0] * 60L,
            2 => values[0] * 60L + values[1],
            _ => values[0] * 3600L + values[1] * 60L + values[2]
        };

        if (total is < 1 or > MaximumDurationSeconds)
        {
            return false;
        }

        seconds = (int)total;
        return true;
    }
}

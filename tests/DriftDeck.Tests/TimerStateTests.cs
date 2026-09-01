using DriftDeck.Models;
using Xunit;

namespace DriftDeck.Tests;

/// <summary>
/// The timer panel keeps no tick count: a running timer is the instant it ends, and every
/// transition is a pure function of that plus the clock. These tests supply the clock, so none
/// of them wait for real time to pass.
/// </summary>
public class TimerStateTests
{
    private static readonly DateTime Now = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FromDuration_starts_paused_at_the_full_length()
    {
        var timer = TimerState.FromDuration(300);

        Assert.Equal(300, timer.DurationSeconds);
        Assert.Equal(300, timer.RemainingSeconds);
        Assert.False(timer.IsRunning);
    }

    [Fact]
    public void FromDuration_clamps_out_of_range_lengths()
    {
        Assert.Equal(1, TimerState.FromDuration(0).DurationSeconds);
        Assert.Equal(1, TimerState.FromDuration(-30).DurationSeconds);
        Assert.Equal(
            TimerState.MaximumDurationSeconds,
            TimerState.FromDuration(TimerState.MaximumDurationSeconds + 1).DurationSeconds);
    }

    [Fact]
    public void Start_sets_the_end_instant_from_the_clock()
    {
        var timer = TimerState.FromDuration(300).Start(Now);

        Assert.True(timer.IsRunning);
        Assert.Equal(Now.AddSeconds(300), timer.EndUtc);
    }

    [Fact]
    public void A_running_timer_counts_down_against_the_clock_with_nothing_ticking()
    {
        var timer = TimerState.FromDuration(300).Start(Now);

        Assert.Equal(300, timer.RemainingAt(Now));
        Assert.Equal(240, timer.RemainingAt(Now.AddSeconds(60)));
        Assert.Equal(0, timer.RemainingAt(Now.AddSeconds(300)));
    }

    [Fact]
    public void Remaining_never_goes_below_zero_however_late_it_is_read()
    {
        // The overlay dies with whatever it sits over, so a timer can be read hours past its end.
        var timer = TimerState.FromDuration(60).Start(Now);

        Assert.Equal(0, timer.RemainingAt(Now.AddHours(9)));
        Assert.True(timer.IsFinishedAt(Now.AddHours(9)));
    }

    [Fact]
    public void Start_on_a_running_timer_changes_nothing()
    {
        var timer = TimerState.FromDuration(300).Start(Now);

        Assert.Equal(timer, timer.Start(Now.AddSeconds(30)));
    }

    [Fact]
    public void Pause_freezes_the_time_that_was_left()
    {
        var timer = TimerState.FromDuration(300).Start(Now).Pause(Now.AddSeconds(60));

        Assert.False(timer.IsRunning);
        Assert.Equal(240, timer.RemainingSeconds);
        // A paused timer ignores the clock entirely.
        Assert.Equal(240, timer.RemainingAt(Now.AddHours(1)));
    }

    [Fact]
    public void Pause_on_a_paused_timer_changes_nothing()
    {
        var timer = TimerState.FromDuration(300);

        Assert.Equal(timer, timer.Pause(Now));
    }

    [Fact]
    public void Resuming_continues_from_where_the_pause_left_it()
    {
        var timer = TimerState.FromDuration(300)
            .Start(Now)
            .Pause(Now.AddSeconds(60))
            .Start(Now.AddMinutes(30));

        Assert.Equal(Now.AddMinutes(30).AddSeconds(240), timer.EndUtc);
        Assert.Equal(240, timer.RemainingAt(Now.AddMinutes(30)));
    }

    [Fact]
    public void Starting_a_finished_timer_restarts_the_full_length()
    {
        // Otherwise the button is a no-op the user has to work out for themselves.
        var timer = TimerState.FromDuration(300).Start(Now).Pause(Now.AddSeconds(300));
        Assert.Equal(0, timer.RemainingSeconds);

        var restarted = timer.Start(Now.AddSeconds(300));

        Assert.Equal(300, restarted.RemainingAt(Now.AddSeconds(300)));
    }

    [Fact]
    public void Reset_returns_to_the_full_length_and_stops()
    {
        var timer = TimerState.FromDuration(300).Start(Now).Reset();

        Assert.False(timer.IsRunning);
        Assert.Equal(300, timer.RemainingSeconds);
    }

    [Fact]
    public void Changing_the_length_stops_a_running_timer()
    {
        // The persisted end instant was derived from the old length and means nothing now.
        var timer = TimerState.FromDuration(300).Start(Now).WithDuration(60);

        Assert.False(timer.IsRunning);
        Assert.Equal(60, timer.DurationSeconds);
        Assert.Equal(60, timer.RemainingSeconds);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(9, "0:09")]
    [InlineData(60, "1:00")]
    [InlineData(300, "5:00")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3661, "1:01:01")]
    [InlineData(86400, "24:00:00")]
    public void Format_reads_like_a_stopwatch(int seconds, string expected) =>
        Assert.Equal(expected, TimerState.Format(seconds));

    [Fact]
    public void Format_floors_a_negative_value_at_zero()
    {
        Assert.Equal("0:00", TimerState.Format(-5));
    }

    [Theory]
    [InlineData("5", 300)]
    [InlineData("90", 5400)]
    [InlineData("5:00", 300)]
    [InlineData("0:30", 30)]
    [InlineData("90:00", 5400)]
    [InlineData("1:30:00", 5400)]
    [InlineData(" 1 : 30 : 00 ", 5400)]
    public void TryParseDuration_reads_a_bare_number_as_minutes_and_colons_literally(
        string text,
        int expected)
    {
        Assert.True(TimerState.TryParseDuration(text, out var seconds));
        Assert.Equal(expected, seconds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("0:00")]
    [InlineData("5:60")]
    [InlineData("1:60:00")]
    [InlineData("1:2:3:4")]
    [InlineData("1441")]
    public void TryParseDuration_refuses_what_it_cannot_mean(string? text)
    {
        Assert.False(TimerState.TryParseDuration(text, out _));
    }

    [Fact]
    public void TryParseDuration_accepts_the_longest_length_the_panel_allows()
    {
        Assert.True(TimerState.TryParseDuration("24:00:00", out var seconds));
        Assert.Equal(TimerState.MaximumDurationSeconds, seconds);
        Assert.False(TimerState.TryParseDuration("24:00:01", out _));
    }

    [Fact]
    public void A_new_timer_panel_starts_at_five_minutes_and_stopped()
    {
        var panel = PanelDefinition.CreateTimer(10, 20);

        Assert.Equal(PanelKind.Timer, panel.Kind);
        Assert.Equal(TimerState.DefaultDurationSeconds, panel.TimerDurationSeconds);
        Assert.Equal(TimerState.DefaultDurationSeconds, panel.TimerRemainingSeconds);
        Assert.Null(panel.TimerEndUtc);
    }
}

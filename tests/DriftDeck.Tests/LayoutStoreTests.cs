using System.IO;
using DriftDeck.Models;
using DriftDeck.Services;
using Xunit;

namespace DriftDeck.Tests;

/// <summary>
/// The layout store is the only thing between a user's arrangement and a blank overlay, and it
/// writes on a ~650 ms debounce while panels move. Each test gets its own directory so a failure
/// leaves nothing behind and nothing touches the real per-user DriftDeck folder.
/// </summary>
public class LayoutStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly LayoutStore _store;

    public LayoutStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "DriftDeck.Tests", Guid.NewGuid().ToString("n"));
        _store = new LayoutStore(_directory);
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
            // A leftover temp directory is not worth failing a green test run over.
        }
    }

    private static OverlayLayout Layout(string name, double left = 10, double top = 20) => new()
    {
        Version = 2,
        Name = name,
        Left = left,
        Top = top,
        Panels = [PanelDefinition.CreateNotes(5, 6)],
    };

    [Theory]
    [InlineData(null, "Default")]
    [InlineData("", "Default")]
    [InlineData("   ", "Default")]
    [InlineData("  Racing  ", "Racing")]
    [InlineData("Racing", "Racing")]
    public void NormalizeName_falls_back_to_Default_for_an_empty_name(string? name, string expected) =>
        Assert.Equal(expected, LayoutStore.NormalizeName(name));

    [Fact]
    public void NormalizeName_strips_characters_a_file_name_cannot_hold()
    {
        Assert.Equal("abcd", LayoutStore.NormalizeName("a/b\\c:d"));
    }

    [Fact]
    public void NormalizeName_returns_Default_when_stripping_leaves_nothing()
    {
        Assert.Equal("Default", LayoutStore.NormalizeName("///"));
    }

    [Fact]
    public void NormalizeName_caps_the_length_at_forty_characters()
    {
        Assert.Equal(40, LayoutStore.NormalizeName(new string('x', 100)).Length);
    }

    [Fact]
    public async Task A_saved_layout_comes_back_unchanged()
    {
        var layout = Layout("Racing", left: 111, top: 222);
        layout.Panels[0].Notes = "lap times";
        await _store.SaveAsync(layout);

        var loaded = await _store.LoadAsync("Racing");

        Assert.Equal("Racing", loaded.Name);
        Assert.Equal(111, loaded.Left);
        Assert.Equal(222, loaded.Top);
        var panel = Assert.Single(loaded.Panels);
        Assert.Equal(PanelKind.Notes, panel.Kind);
        Assert.Equal("lap times", panel.Notes);
    }

    [Fact]
    public async Task Loading_a_layout_that_was_never_saved_gives_a_default_under_that_name()
    {
        var loaded = await _store.LoadAsync("Nothing here");

        Assert.Equal("Nothing here", loaded.Name);
        Assert.NotEmpty(loaded.Panels);
    }

    [Fact]
    public async Task Saving_makes_the_layout_the_one_that_loads_next_launch()
    {
        await _store.SaveAsync(Layout("Racing", left: 111));

        var loaded = await _store.LoadLastAsync();

        Assert.Equal("Racing", loaded.Name);
        Assert.Equal(111, loaded.Left);
    }

    [Fact]
    public async Task LoadLast_falls_back_to_Default_on_a_first_run()
    {
        Assert.Equal("Default", (await _store.LoadLastAsync()).Name);
    }

    [Fact]
    public async Task Saving_under_an_unusable_name_writes_the_normalized_one()
    {
        var layout = Layout("  Bad/Name  ");
        await _store.SaveAsync(layout);

        Assert.Equal("BadName", layout.Name);
        Assert.True(_store.Exists("BadName"));
    }

    [Fact]
    public async Task SaveCopy_writes_the_copy_and_leaves_the_original_current()
    {
        var layout = Layout("Racing", left: 111);
        await _store.SaveAsync(layout);

        await _store.SaveCopyAsync(layout, "Racing copy");

        Assert.Equal("Racing", layout.Name);
        Assert.True(_store.Exists("Racing copy"));
        Assert.Equal(111, (await _store.LoadAsync("Racing copy")).Left);
        // The copy must not become the layout the next launch opens.
        Assert.Equal("Racing", (await _store.LoadLastAsync()).Name);
    }

    [Fact]
    public void Exists_is_false_before_anything_is_saved()
    {
        Assert.False(_store.Exists("Racing"));
        Assert.False(_store.Exists(null));
    }

    [Fact]
    public async Task ListNames_sorts_alphabetically()
    {
        await _store.SaveAsync(Layout("Racing"));
        await _store.SaveAsync(Layout("Coding"));
        await _store.SaveAsync(Layout("Default"));

        Assert.Equal(["Coding", "Default", "Racing"], _store.ListNames());
    }

    [Fact]
    public async Task ListNames_puts_an_unsaved_Default_first_rather_than_in_sort_order()
    {
        // Default is synthesised when no file for it exists, and it leads the list because it
        // is the layout every fallback path loads, not because of where the name sorts.
        await _store.SaveAsync(Layout("Coding"));

        Assert.Equal(["Default", "Coding"], _store.ListNames());
    }

    [Fact]
    public void ListNames_offers_Default_when_the_directory_does_not_exist()
    {
        Assert.Equal(["Default"], _store.ListNames());
    }

    [Fact]
    public async Task Delete_removes_a_layout()
    {
        await _store.SaveAsync(Layout("Racing"));

        Assert.True(_store.Delete("Racing"));
        Assert.False(_store.Exists("Racing"));
    }

    [Fact]
    public async Task Delete_refuses_to_remove_Default()
    {
        // Default is the layout every fallback path loads; deleting it has no safe outcome.
        await _store.SaveAsync(Layout("Default"));

        Assert.False(_store.Delete("Default"));
        Assert.False(_store.Delete("default"));
        Assert.True(_store.Exists("Default"));
    }

    [Fact]
    public void Delete_reports_false_for_a_layout_that_is_not_there()
    {
        Assert.False(_store.Delete("Racing"));
    }

    [Fact]
    public async Task A_corrupt_file_loads_as_the_default_layout_rather_than_throwing()
    {
        // A crash mid-write, or a hand-edited file, must not stop the overlay from starting.
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "Racing.json"), "{ not json");

        var loaded = await _store.LoadAsync("Racing");

        Assert.NotEmpty(loaded.Panels);
    }

    [Fact]
    public async Task A_layout_with_no_panels_stays_empty()
    {
        var layout = Layout("Racing");
        layout.Panels.Clear();
        await _store.SaveAsync(layout);

        Assert.Empty((await _store.LoadLastAsync()).Panels);
    }

    [Fact]
    public async Task A_checklist_panel_round_trips_its_items()
    {
        var layout = Layout("Racing");
        layout.Panels.Clear();
        var panel = PanelDefinition.CreateChecklist(5, 6);
        panel.Items.Add(new ChecklistItem { Text = "warm up", IsDone = true });
        panel.Items.Add(new ChecklistItem { Text = "qualify" });
        layout.Panels.Add(panel);
        await _store.SaveAsync(layout);

        var loaded = Assert.Single((await _store.LoadAsync("Racing")).Panels);

        Assert.Equal(PanelKind.Checklist, loaded.Kind);
        Assert.Equal(["warm up", "qualify"], loaded.Items.Select(item => item.Text));
        Assert.Equal([true, false], loaded.Items.Select(item => item.IsDone));
    }

    [Fact]
    public async Task A_running_timer_panel_round_trips_its_end_instant()
    {
        // The end instant is what makes a timer survive the crash an overlay usually dies in.
        var endUtc = new DateTime(2026, 8, 31, 12, 5, 0, DateTimeKind.Utc);
        var layout = Layout("Racing");
        layout.Panels.Clear();
        var panel = PanelDefinition.CreateTimer(5, 6);
        panel.TimerDurationSeconds = 300;
        panel.TimerRemainingSeconds = 300;
        panel.TimerEndUtc = endUtc;
        layout.Panels.Add(panel);
        await _store.SaveAsync(layout);

        var loaded = Assert.Single((await _store.LoadAsync("Racing")).Panels);

        Assert.Equal(PanelKind.Timer, loaded.Kind);
        Assert.Equal(300, loaded.TimerDurationSeconds);
        Assert.Equal(endUtc, loaded.TimerEndUtc!.Value.ToUniversalTime());
    }

    [Fact]
    public async Task A_layout_saved_before_checklists_existed_loads_with_an_empty_item_list()
    {
        // Items is absent from every file written by an earlier build, and a null list would
        // fault the first time a panel enumerated it.
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "Racing.json"),
            "{ \"Version\": 2, \"Name\": \"Racing\", \"Panels\": [ { \"Kind\": 1, \"X\": 1, \"Y\": 2 } ] }");

        var panel = Assert.Single((await _store.LoadAsync("Racing")).Panels);

        Assert.NotNull(panel.Items);
        Assert.Empty(panel.Items);
    }

    [Fact]
    public async Task A_version_one_layout_is_migrated_to_absolute_panel_positions()
    {
        // Version 1 stored panel positions relative to the dock. Version 2 stores screen
        // coordinates, so the old values shift by the dock origin plus its 94 px height.
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "Racing.json"),
            "{ \"Version\": 1, \"Name\": \"Racing\", \"Left\": 100, \"Top\": 200," +
            "  \"Panels\": [ { \"Kind\": 1, \"X\": 10, \"Y\": 20 } ] }");

        var loaded = await _store.LoadAsync("Racing");

        // Migration stamps the current version, which is 3 since RecentUrls was added.
        Assert.Equal(3, loaded.Version);
        Assert.Equal(94, loaded.Height);
        var panel = Assert.Single(loaded.Panels);
        Assert.Equal(110, panel.X);
        Assert.Equal(314, panel.Y);
    }

    [Fact]
    public async Task A_version_two_layout_is_left_alone()
    {
        var layout = Layout("Racing", left: 100, top: 200);
        layout.Panels[0].X = 10;
        layout.Panels[0].Y = 20;
        await _store.SaveAsync(layout);

        var panel = Assert.Single((await _store.LoadAsync("Racing")).Panels);
        Assert.Equal(10, panel.X);
        Assert.Equal(20, panel.Y);
    }
    [Fact]
    public async Task Overlapping_saves_capture_each_layout_and_restore_the_latest()
    {
        var layout = Layout("Racing");
        var saves = new List<Task>();
        for (var i = 0; i < 30; i++)
        {
            layout.Panels[0].Notes = $"note {i}";
            saves.Add(_store.SaveAsync(layout));
        }
        await Task.WhenAll(saves);
        Assert.Equal("note 29", Assert.Single((await _store.LoadLastAsync()).Panels).Notes);
    }

    [Fact]
    public async Task Copy_does_not_rename_a_live_layout_during_another_save()
    {
        var layout = Layout("Racing");
        var copy = _store.SaveCopyAsync(layout, "Other");
        Assert.Equal("Racing", layout.Name);
        var save = _store.SaveAsync(layout);
        await Task.WhenAll(copy, save);
        Assert.Equal("Racing", (await _store.LoadLastAsync()).Name);
        Assert.Equal("Other", (await _store.LoadAsync("Other")).Name);
    }

    [Fact]
    public async Task A_damaged_primary_recovers_the_backup_and_keeps_it_after_saving()
    {
        var layout = Layout("Racing");
        layout.Panels[0].Notes = "keep me";
        await _store.SaveAsync(layout);
        await _store.SaveAsync(layout);
        await File.WriteAllTextAsync(Path.Combine(_directory, "Racing.json"), "broken");
        var recovered = await _store.LoadLastAsync();
        Assert.Equal("keep me", Assert.Single(recovered.Panels).Notes);
        Assert.Contains("Recovered", _store.LoadWarning);
        await _store.SaveAsync(recovered);
        Assert.Single(Directory.GetFiles(_directory, "Racing.json.corrupt-*"));
        Assert.Contains("keep me", await File.ReadAllTextAsync(Path.Combine(_directory, "Racing.json.bak")));
    }

    [Fact]
    public async Task Deleting_a_layout_also_removes_its_recoverable_backup()
    {
        await _store.SaveAsync(Layout("Racing"));
        await _store.SaveAsync(Layout("Racing"));
        Assert.True(_store.Delete("Racing"));
        Assert.False(File.Exists(Path.Combine(_directory, "Racing.json.bak")));
    }

    [Fact]
    public async Task Panel_preferences_and_collapsed_dock_survive_restart()
    {
        var layout = Layout("Racing");
        layout.IsDockCollapsed = true;
        layout.Opacity = 0.61;
        var panel = layout.Panels[0];
        panel.ContentScale = 1.3;
        panel.Opacity = 0.72;
        panel.IsCollapsed = true;
        panel.RestoreHeight = 550;
        panel.IsLocked = true;
        panel.IsMuted = true;
        panel.HasCustomTitle = true;
        panel.Title = "My reference";
        await _store.SaveAsync(layout);
        var restored = await new LayoutStore(_directory).LoadLastAsync();
        Assert.True(restored.IsDockCollapsed);
        Assert.Equal(0.61, restored.Opacity);
        var actual = Assert.Single(restored.Panels);
        Assert.Equal(1.3, actual.ContentScale);
        Assert.Equal(0.72, actual.Opacity);
        Assert.True(actual.IsCollapsed);
        Assert.True(actual.IsLocked);
        Assert.True(actual.IsMuted);
        Assert.True(actual.HasCustomTitle);
        Assert.Equal(550, actual.RestoreHeight);
        Assert.Equal("My reference", actual.Title);
    }

    [Fact]
    public async Task Copying_cannot_overwrite_an_existing_layout()
    {
        var original = Layout("Racing");
        original.Panels[0].Notes = "original";
        await _store.SaveAsync(original);
        var other = Layout("Other");
        await Assert.ThrowsAsync<IOException>(() => _store.SaveCopyAsync(other, "Racing"));
        Assert.Equal("original", Assert.Single((await _store.LoadAsync("Racing")).Panels).Notes);
    }

    [Fact]
    public async Task A_long_layout_name_gets_distinct_copy_names_that_fit()
    {
        var original = Layout(new string('a', 40));
        await _store.SaveAsync(original);
        var first = _store.SuggestCopyName(original.Name);
        Assert.NotEqual(original.Name, first);
        Assert.True(first.Length <= 40);
        await _store.SaveCopyAsync(original, first);
        var second = _store.SuggestCopyName(original.Name);
        Assert.NotEqual(first, second);
        Assert.True(second.Length <= 40);
    }

}

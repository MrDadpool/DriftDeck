using System.IO;
using DriftDeck.Models;
using DriftDeck.Services;
using Xunit;

namespace DriftDeck.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DriftDeck.Tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "settings.json");

    [Fact]
    public async Task Preferences_and_bookmarks_survive_a_new_store_instance()
    {
        var store = new SettingsStore(FilePath);
        await store.SaveAsync(new AppSettings
        {
            HasSeenOnboarding = true, StartHidden = true, IdleDimEnabled = true,
            IdleDimSeconds = 45, CheckForUpdates = false,
            Bookmarks = [new Bookmark { Title = "Wiki", Url = "https://example.com/wiki" }]
        });
        var saved = new SettingsStore(FilePath).Load();
        Assert.True(saved.HasSeenOnboarding);
        Assert.True(saved.StartHidden);
        Assert.True(saved.IdleDimEnabled);
        Assert.Equal(45, saved.IdleDimSeconds);
        Assert.False(saved.CheckForUpdates);
        Assert.Equal("https://example.com/wiki", Assert.Single(saved.Bookmarks).Url);
    }

    [Fact]
    public async Task Rapid_saves_do_not_share_an_open_temporary_file()
    {
        var store = new SettingsStore(FilePath);
        var settings = new AppSettings();
        var pending = new List<Task>();
        for (var i = 3; i < 60; i++)
        {
            settings.IdleDimSeconds = i;
            pending.Add(store.SaveAsync(settings));
        }
        await Task.WhenAll(pending);
        Assert.Equal(59, store.Load().IdleDimSeconds);
    }

    [Fact]
    public async Task A_save_captures_values_before_the_live_settings_change()
    {
        var store = new SettingsStore(FilePath);
        var settings = new AppSettings { IdleDimSeconds = 35 };
        var save = store.SaveAsync(settings);
        settings.IdleDimSeconds = 95;
        await save;
        Assert.Equal(35, store.Load().IdleDimSeconds);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    public async Task Invalid_settings_recover_the_last_good_copy(string invalid)
    {
        var store = new SettingsStore(FilePath);
        var settings = new AppSettings { HasSeenOnboarding = true, IdleDimSeconds = 48 };
        await store.SaveAsync(settings);
        await store.SaveAsync(settings);
        await File.WriteAllTextAsync(FilePath, invalid);
        var restored = store.Load();
        Assert.True(restored.HasSeenOnboarding);
        Assert.Equal(48, restored.IdleDimSeconds);
        Assert.Contains("Recovered", store.LoadWarning);
        await store.SaveAsync(restored);
        Assert.Single(Directory.GetFiles(_directory, "settings.json.corrupt-*"));
    }

    [Fact]
    public async Task Missing_primary_can_recover_a_backup()
    {
        var store = new SettingsStore(FilePath);
        await store.SaveAsync(new AppSettings { IdleDimSeconds = 49 });
        await store.SaveAsync(new AppSettings { IdleDimSeconds = 50 });
        File.Delete(FilePath);
        Assert.Equal(49, store.Load().IdleDimSeconds);
        Assert.Contains("Recovered", store.LoadWarning);
    }

    [Fact]
    public async Task Unrecoverable_data_is_reported_and_preserved()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, "broken");
        var store = new SettingsStore(FilePath);
        var defaults = store.Load();
        Assert.NotNull(store.LoadWarning);
        Assert.Equal("broken", await File.ReadAllTextAsync(FilePath));
        await store.SaveAsync(defaults);
        var preserved = Assert.Single(Directory.GetFiles(_directory, "settings.json.corrupt-*"));
        Assert.Equal("broken", await File.ReadAllTextAsync(preserved));
    }

    [Fact]
    public async Task Failed_write_does_not_replace_the_previous_file()
    {
        var store = new SettingsStore(FilePath);
        await store.SaveAsync(new AppSettings { IdleDimSeconds = 47 });
        Directory.CreateDirectory(FilePath + ".tmp");
        var error = await Record.ExceptionAsync(() => store.SaveAsync(new AppSettings { IdleDimSeconds = 60 }));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal(47, store.Load().IdleDimSeconds);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}

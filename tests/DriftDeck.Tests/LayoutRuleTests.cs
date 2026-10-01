using DriftDeck.Models;
using Xunit;

namespace DriftDeck.Tests;

/// <summary>
/// Layout rules decide which layout appears when the user tabs to another application. Getting
/// precedence wrong swaps the overlay under someone mid-game, so the ordering guarantee is
/// worth pinning down.
/// </summary>
public class LayoutRuleTests
{
    private static LayoutRule Rule(
        string process,
        string title = "",
        string layout = "Layout",
        bool enabled = true) =>
        new()
        {
            ProcessName = process,
            TitleContains = title,
            LayoutName = layout,
            Enabled = enabled,
        };

    [Fact]
    public void Matches_is_case_insensitive_on_the_process_name()
    {
        Assert.True(Rule("EldenRing").Matches("eldenring", "ELDEN RING"));
    }

    [Fact]
    public void Matches_trims_a_process_name_the_user_typed_with_spaces()
    {
        Assert.True(Rule("  eldenring  ").Matches("eldenring", "ELDEN RING"));
    }

    [Fact]
    public void Matches_requires_the_whole_process_name()
    {
        // A substring match would let "ring" fire on "eldenring".
        Assert.False(Rule("ring").Matches("eldenring", "ELDEN RING"));
    }

    [Fact]
    public void Matches_narrows_by_a_title_substring_when_one_is_set()
    {
        var rule = Rule("chrome", "GitHub");
        Assert.True(rule.Matches("chrome", "DriftDeck - GitHub"));
        Assert.False(rule.Matches("chrome", "Some other page"));
    }

    [Fact]
    public void Matches_compares_the_title_case_insensitively()
    {
        Assert.True(Rule("chrome", "github").Matches("chrome", "DriftDeck - GitHub"));
    }

    [Fact]
    public void A_disabled_rule_never_matches()
    {
        Assert.False(Rule("eldenring", enabled: false).Matches("eldenring", "ELDEN RING"));
    }

    [Fact]
    public void A_rule_with_no_process_name_never_matches()
    {
        // An empty row in Settings is a half-finished rule, not a wildcard.
        Assert.False(Rule("   ").Matches("eldenring", "ELDEN RING"));
        Assert.False(Rule("").Matches("", ""));
    }

    [Fact]
    public void FirstMatch_returns_null_when_nothing_matches()
    {
        Assert.Null(LayoutRule.FirstMatch([Rule("chrome")], "eldenring", "ELDEN RING"));
    }

    [Fact]
    public void FirstMatch_returns_null_for_an_empty_rule_list()
    {
        Assert.Null(LayoutRule.FirstMatch([], "eldenring", "ELDEN RING"));
    }

    [Fact]
    public void FirstMatch_prefers_a_title_qualified_rule_listed_after_a_bare_one()
    {
        var rules = new[]
        {
            Rule("chrome", layout: "Browsing"),
            Rule("chrome", "GitHub", "Code"),
        };

        Assert.Equal("Code", LayoutRule.FirstMatch(rules, "chrome", "DriftDeck - GitHub")!.LayoutName);
    }

    [Fact]
    public void FirstMatch_prefers_a_title_qualified_rule_listed_before_a_bare_one()
    {
        var rules = new[]
        {
            Rule("chrome", "GitHub", "Code"),
            Rule("chrome", layout: "Browsing"),
        };

        Assert.Equal("Code", LayoutRule.FirstMatch(rules, "chrome", "DriftDeck - GitHub")!.LayoutName);
    }

    [Fact]
    public void FirstMatch_falls_back_to_the_bare_rule_when_the_title_does_not_match()
    {
        var rules = new[]
        {
            Rule("chrome", "GitHub", "Code"),
            Rule("chrome", layout: "Browsing"),
        };

        Assert.Equal("Browsing", LayoutRule.FirstMatch(rules, "chrome", "News")!.LayoutName);
    }

    [Fact]
    public void FirstMatch_keeps_list_order_among_equally_specific_rules()
    {
        var rules = new[]
        {
            Rule("chrome", layout: "First"),
            Rule("chrome", layout: "Second"),
        };

        Assert.Equal("First", LayoutRule.FirstMatch(rules, "chrome", "anything")!.LayoutName);
    }

    [Fact]
    public void FirstMatch_skips_a_disabled_rule_and_takes_the_next()
    {
        var rules = new[]
        {
            Rule("chrome", "GitHub", "Code", enabled: false),
            Rule("chrome", layout: "Browsing"),
        };

        Assert.Equal("Browsing", LayoutRule.FirstMatch(rules, "chrome", "DriftDeck - GitHub")!.LayoutName);
    }
}

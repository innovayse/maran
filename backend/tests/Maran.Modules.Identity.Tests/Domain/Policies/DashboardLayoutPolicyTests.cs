using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;

namespace Maran.Modules.Identity.Tests.Domain.Policies;

/// <summary>Behavioural contract of the rules about a person's dashboard layout.</summary>
public sealed class DashboardLayoutPolicyTests
{
    /// <summary>The administrator's default widgets, in the order they are drawn.</summary>
    private static readonly string[] AdministratorWidgets =
    {
        "attention", "firstRun", "cpu", "memory", "disk", "network", "services", "totals", "activity",
    };

    /// <summary>The widget keys after the person put the network card first and turned the disk card off.</summary>
    private static readonly string[] NetworkFirstThenTheRest =
    {
        "network", "disk", "attention", "firstRun", "cpu", "memory", "services", "totals", "activity",
    };

    private static DashboardPreference Stored(DashboardPreferenceKind kind, string key, int position, bool hidden)
    {
        return new DashboardPreference(Guid.NewGuid(), Guid.NewGuid(), kind, key, position, hidden);
    }

    /// <summary>Keys the SPA names its widgets with are accepted.</summary>
    [Theory]
    [InlineData("cpu")]
    [InlineData("firstRun")]
    [InlineData("addCronJob")]
    [InlineData("w2")]
    public void A_camel_case_key_is_valid(string key)
    {
        Assert.True(DashboardLayoutPolicy.IsValidKey(key));
    }

    /// <summary>Anything else is refused, control characters and separators included.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1abc")]
    [InlineData("a b")]
    [InlineData("a-b")]
    [InlineData("a_b")]
    [InlineData("a/b")]
    [InlineData("a\nb")]
    [InlineData("écran")]
    public void A_key_outside_the_charset_is_invalid(string? key)
    {
        Assert.False(DashboardLayoutPolicy.IsValidKey(key));
    }

    /// <summary>The length ceiling is inclusive.</summary>
    [Fact]
    public void A_key_is_valid_up_to_the_ceiling_and_invalid_beyond_it()
    {
        Assert.True(DashboardLayoutPolicy.IsValidKey(new string('a', DashboardLayoutPolicy.MaxKeyLength)));
        Assert.False(DashboardLayoutPolicy.IsValidKey(new string('a', DashboardLayoutPolicy.MaxKeyLength + 1)));
    }

    /// <summary>A person who stored nothing gets the administrator's defaults, none hidden.</summary>
    [Fact]
    public void An_administrator_with_nothing_stored_gets_every_default_visible()
    {
        var widgets = DashboardLayoutPolicy.Effective(
            Array.Empty<DashboardPreference>(), DashboardPreferenceKind.Widget, forAdministrator: true);

        Assert.Equal(AdministratorWidgets, widgets.Select(entry =>
        {
            return entry.Key;
        }));
        Assert.All(widgets, entry =>
        {
            Assert.False(entry.Hidden);
        });
        Assert.Equal(
            6,
            DashboardLayoutPolicy.Effective(
                Array.Empty<DashboardPreference>(), DashboardPreferenceKind.Action, forAdministrator: true).Count);
    }

    /// <summary>Any other caller has no default dashboard, so an unarranged list is empty.</summary>
    [Fact]
    public void A_caller_who_is_not_an_administrator_with_nothing_stored_gets_empty_lists()
    {
        Assert.Empty(DashboardLayoutPolicy.Effective(
            Array.Empty<DashboardPreference>(), DashboardPreferenceKind.Widget, forAdministrator: false));
        Assert.Empty(DashboardLayoutPolicy.Effective(
            Array.Empty<DashboardPreference>(), DashboardPreferenceKind.Action, forAdministrator: false));
    }

    /// <summary>Stored entries lead in the person's order, and a default they have no entry for follows in the default order.</summary>
    [Fact]
    public void Stored_entries_lead_and_the_defaults_without_one_follow()
    {
        var stored = new[]
        {
            Stored(DashboardPreferenceKind.Widget, "disk", 1, hidden: true),
            Stored(DashboardPreferenceKind.Widget, "network", 0, hidden: false),
        };

        var widgets = DashboardLayoutPolicy.Effective(stored, DashboardPreferenceKind.Widget, forAdministrator: true);

        Assert.Equal(NetworkFirstThenTheRest, widgets.Select(entry =>
        {
            return entry.Key;
        }));
        Assert.True(widgets.Single(entry =>
        {
            return entry.Key == "disk";
        }).Hidden);
    }

    /// <summary>A key no default names is kept where the person put it, for any caller.</summary>
    [Fact]
    public void A_key_no_default_names_is_kept_in_place()
    {
        var stored = new[]
        {
            Stored(DashboardPreferenceKind.Widget, "mailQueue", 0, hidden: false),
        };

        var forAdministrator = DashboardLayoutPolicy.Effective(
            stored, DashboardPreferenceKind.Widget, forAdministrator: true);
        var forOthers = DashboardLayoutPolicy.Effective(
            stored, DashboardPreferenceKind.Widget, forAdministrator: false);

        Assert.Equal("mailQueue", forAdministrator[0].Key);
        Assert.Equal(AdministratorWidgets.Length + 1, forAdministrator.Count);
        Assert.Equal("mailQueue", Assert.Single(forOthers).Key);
    }

    /// <summary>A stored shortcut never appears in the widget list, and the reverse.</summary>
    [Fact]
    public void The_two_lists_do_not_mix()
    {
        var stored = new[]
        {
            Stored(DashboardPreferenceKind.Action, "addSite", 0, hidden: true),
        };

        var widgets = DashboardLayoutPolicy.Effective(stored, DashboardPreferenceKind.Widget, forAdministrator: false);
        var actions = DashboardLayoutPolicy.Effective(stored, DashboardPreferenceKind.Action, forAdministrator: false);

        Assert.Empty(widgets);
        Assert.True(Assert.Single(actions).Hidden);
    }

    /// <summary>An entry cannot be created with a malformed key.</summary>
    [Fact]
    public void An_entry_with_a_malformed_key_cannot_be_created()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return Stored(DashboardPreferenceKind.Widget, "not a key", 0, hidden: false);
        });
    }
}

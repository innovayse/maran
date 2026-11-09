using Maran.Modules.Updates.Domain.Policies;

namespace Maran.Modules.Updates.Tests.Domain.Policies;

/// <summary>The rules that decide whether a value root wrote is plain enough to show.</summary>
public sealed class PanelUpdateValueRulesTests
{
    /// <summary>A plain https address is accepted, and anything that could break out of a link or a line is not.</summary>
    [Theory]
    [InlineData("https://example.net/notes/1.1.0", true)]
    [InlineData("http://example.net/notes", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://example.net/a b", false)]
    [InlineData("https://example.net/a\nb", false)]
    [InlineData("https://example.net/a\"b", false)]
    [InlineData("https://example.net/<script>", false)]
    [InlineData("https://example.net/a\\b", false)]
    [InlineData("https://example.net/é", false)]
    [InlineData("", false)]
    public void A_notes_address_must_be_a_plain_https_address(string text, bool expected)
    {
        Assert.Equal(expected, PanelUpdateValueRules.IsPlainHttpsUrl(text));
    }

    /// <summary>An address longer than the limit is refused and one at the limit is accepted.</summary>
    [Fact]
    public void An_address_over_the_length_limit_is_refused()
    {
        var atLimit = "https://example.net/" + new string('a', PanelUpdateValueRules.MaxUrlLength - "https://example.net/".Length);

        Assert.True(PanelUpdateValueRules.IsPlainHttpsUrl(atLimit));
        Assert.False(PanelUpdateValueRules.IsPlainHttpsUrl(atLimit + "a"));
    }

    /// <summary>Only the two channels the installer knows are accepted, spelled exactly.</summary>
    [Theory]
    [InlineData("stable", true)]
    [InlineData("beta", true)]
    [InlineData("Stable", false)]
    [InlineData("nightly", false)]
    [InlineData("", false)]
    public void Only_stable_and_beta_are_channels(string text, bool expected)
    {
        Assert.Equal(expected, PanelUpdateValueRules.IsChannel(text));
    }

    /// <summary>An instant is accepted only in the one format root writes.</summary>
    [Theory]
    [InlineData("2026-11-06T08:00:00Z", true)]
    [InlineData("2026-11-06 08:00:00", false)]
    [InlineData("2026-11-06T08:00:00+02:00", false)]
    [InlineData("yesterday", false)]
    [InlineData("", false)]
    public void An_instant_must_be_in_the_exact_format(string text, bool expected)
    {
        Assert.Equal(expected, PanelUpdateValueRules.TryInstant(text, out _));
    }

    /// <summary>A date is accepted only as year-month-day.</summary>
    [Theory]
    [InlineData("2026-11-05", true)]
    [InlineData("2026-13-05", false)]
    [InlineData("05/11/2026", false)]
    [InlineData("", false)]
    public void A_date_must_be_year_month_day(string text, bool expected)
    {
        Assert.Equal(expected, PanelUpdateValueRules.IsDate(text));
    }

    /// <summary>A release name is a version or the name a legacy install is adopted under, and never a path.</summary>
    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("1.2.3-beta.4", true)]
    [InlineData("legacy-20261101T120000Z", true)]
    [InlineData("../etc", false)]
    [InlineData("1.0.0/../x", false)]
    [InlineData("current", false)]
    [InlineData("", false)]
    public void A_release_name_is_a_version_or_a_legacy_name(string text, bool expected)
    {
        Assert.Equal(expected, PanelUpdateValueRules.IsReleaseName(text));
    }

    /// <summary>A plain release name has the installer's alphabet and a trailing newline is not part of it.</summary>
    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("1.2.3\n", false)]
    [InlineData("1.2.3;reboot", false)]
    [InlineData("", false)]
    public void A_plain_release_name_uses_only_the_installers_alphabet(string text, bool expected)
    {
        Assert.Equal(expected, PanelUpdateValueRules.IsPlainReleaseName(text));
    }
}

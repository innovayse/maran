using Maran.Modules.Monitoring.Domain.Enums;
using Maran.Modules.Monitoring.Domain.ValueObjects;

namespace Maran.Modules.Monitoring.Tests.Domain.ValueObjects;

/// <summary>
/// A panel update report holds only an outcome the panel defines and release names that are safe to put
/// in a journal subject and a mail, because both arrive from a command-line argument.
/// </summary>
public sealed class PanelUpdateReportTests
{
    /// <summary>Plain release names, a SemVer pre-release and a legacy name among them, are accepted (inverse control).</summary>
    [Theory]
    [InlineData("1.2.3", "1.2.4")]
    [InlineData("1.0.0-beta.4", "1.0.0-beta.5")]
    [InlineData("legacy-20261101T120000Z", "1.0.1")]
    public void Plain_release_names_are_accepted(string from, string to)
    {
        var created = PanelUpdateReport.TryCreate(PanelUpdateOutcome.RolledBack, from, to, out var report);

        Assert.True(created);
        Assert.Equal(from, report!.From);
        Assert.Equal(to, report.To);
        Assert.Equal(PanelUpdateOutcome.RolledBack, report.Outcome);
    }

    /// <summary>A name with a space, a control character, a path part, a quote or a trailing newline is refused.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("1.2.3 ")]
    [InlineData("1.2 3")]
    [InlineData("1.2.3\n")]
    [InlineData("1.2.3\r\nBcc: x@example.com")]
    [InlineData("../etc")]
    [InlineData("1.2.3/4")]
    [InlineData("1.2.3\"")]
    [InlineData("1.2.3;x")]
    public void A_release_name_outside_the_plain_alphabet_is_refused(string name)
    {
        Assert.False(PanelUpdateReport.TryCreate(PanelUpdateOutcome.Applied, name, "1.2.4", out var fromReport));
        Assert.Null(fromReport);
        Assert.False(PanelUpdateReport.TryCreate(PanelUpdateOutcome.Applied, "1.2.3", name, out var toReport));
        Assert.Null(toReport);
    }

    /// <summary>A name longer than the installer allows for a release directory is refused; the longest allowed is accepted.</summary>
    [Fact]
    public void A_release_name_over_sixty_four_characters_is_refused()
    {
        Assert.True(PanelUpdateReport.TryCreate(PanelUpdateOutcome.Applied, new string('1', 64), "1.2.4", out _));
        Assert.False(PanelUpdateReport.TryCreate(PanelUpdateOutcome.Applied, new string('1', 65), "1.2.4", out _));
    }

    /// <summary>An outcome the panel does not define is refused.</summary>
    [Fact]
    public void An_undefined_outcome_is_refused()
    {
        Assert.False(PanelUpdateReport.TryCreate((PanelUpdateOutcome)99, "1.2.3", "1.2.4", out var report));
        Assert.Null(report);
    }
}

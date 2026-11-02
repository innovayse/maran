using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;

namespace Maran.Sdk.Tests.Extensions;

#pragma warning disable CS0618 // These tests exist to pin the conversion from the superseded report the closed PluginLoader still builds.
/// <summary>Behavioral contract of <see cref="CodeIntegrityReportExtensions.ToVerdict"/>: each outcome becomes its own verdict case.</summary>
public sealed class CodeIntegrityReportExtensionsTests
{
    /// <summary>The instant every report in these tests was observed.</summary>
    private static readonly DateTimeOffset Observed = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A clean report converts to the clean verdict for the same version and instant.</summary>
    [Fact]
    public void A_clean_report_converts_to_the_clean_verdict()
    {
        var report = new CodeIntegrityReport(CodeIntegrityOutcome.Clean, "1.4.2", [], null, Observed);

        Assert.Equal(new CleanCodeIntegrityVerdict("1.4.2", Observed), report.ToVerdict());
    }

    /// <summary>A drifted report converts to the drifted verdict carrying the same paths.</summary>
    [Fact]
    public void A_drifted_report_converts_to_the_drifted_verdict_with_its_paths()
    {
        var report = new CodeIntegrityReport(CodeIntegrityOutcome.Drifted, "1.4.2", ["api/Maran.Host"], null, Observed);

        var verdict = Assert.IsType<DriftedCodeIntegrityVerdict>(report.ToVerdict());

        Assert.Equal(["api/Maran.Host"], verdict.DifferingPaths);
        Assert.Equal("1.4.2", verdict.InstalledVersion);
        Assert.Equal(Observed, verdict.ObservedAt);
    }

    /// <summary>A drifted report naming nothing stays an empty drift, for the consumer to refuse rather than the converter to repair.</summary>
    [Fact]
    public void A_drifted_report_naming_nothing_stays_an_empty_drift()
    {
        var report = new CodeIntegrityReport(CodeIntegrityOutcome.Drifted, "1.4.2", [], null, Observed);

        var verdict = Assert.IsType<DriftedCodeIntegrityVerdict>(report.ToVerdict());

        Assert.Empty(verdict.DifferingPaths);
    }

    /// <summary>An unavailable report converts to the unavailable verdict carrying its reason.</summary>
    [Fact]
    public void An_unavailable_report_converts_to_the_unavailable_verdict_with_its_reason()
    {
        var report = new CodeIntegrityReport(CodeIntegrityOutcome.Unavailable, "1.4.2", [], "hash list missing", Observed);

        Assert.Equal(new UnavailableCodeIntegrityVerdict("1.4.2", "hash list missing", Observed), report.ToVerdict());
    }

    /// <summary>An unavailable report without a reason is still the unavailable case, never clean.</summary>
    [Fact]
    public void An_unavailable_report_without_a_reason_is_still_unavailable()
    {
        var report = new CodeIntegrityReport(CodeIntegrityOutcome.Unavailable, "1.4.2", [], null, Observed);

        Assert.IsType<UnavailableCodeIntegrityVerdict>(report.ToVerdict());
    }

    /// <summary>An outcome outside the three the contract defines is refused, not read as clean.</summary>
    [Fact]
    public void An_undefined_outcome_is_refused()
    {
        var report = new CodeIntegrityReport((CodeIntegrityOutcome)99, "1.4.2", [], null, Observed);

        Assert.Throws<ArgumentOutOfRangeException>(() => report.ToVerdict());
    }
}
#pragma warning restore CS0618

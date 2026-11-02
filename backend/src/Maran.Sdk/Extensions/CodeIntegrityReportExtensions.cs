using Maran.Sdk.Contracts;

namespace Maran.Sdk.Extensions;

#pragma warning disable CS0618 // The report and its outcome are superseded, but the closed PluginLoader still builds them; this file is where they are converted.
/// <summary>
/// The one conversion from the closed PluginLoader's flat <see cref="CodeIntegrityReport"/> to the
/// <see cref="CodeIntegrityVerdict"/> the panel reads. Every use of the superseded report type in
/// this repository sits at this edge, so the suppression of the obsolescence warning is written once,
/// here, instead of spreading to every consumer.
/// </summary>
public static class CodeIntegrityReportExtensions
{
    /// <summary>Converts a flat report into the verdict case its outcome names.</summary>
    /// <param name="report">The report the closed PluginLoader produced.</param>
    /// <returns>
    /// A <see cref="CleanCodeIntegrityVerdict"/>, a <see cref="DriftedCodeIntegrityVerdict"/> carrying
    /// the differing paths exactly as reported (an empty list stays empty, for the consumer to refuse),
    /// or an <see cref="UnavailableCodeIntegrityVerdict"/> carrying the reason. A report that says
    /// <see cref="CodeIntegrityOutcome.Unavailable"/> without a reason, which the superseded contract
    /// permitted, converts with an empty reason: the case is what matters, and no reason is invented.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The outcome is not one of the three the contract defines; such a report is not interpreted.
    /// </exception>
    public static CodeIntegrityVerdict ToVerdict(this CodeIntegrityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        switch (report.Outcome)
        {
            case CodeIntegrityOutcome.Clean:
            {
                return new CleanCodeIntegrityVerdict(report.InstalledVersion, report.ObservedAt);
            }

            case CodeIntegrityOutcome.Drifted:
            {
                return new DriftedCodeIntegrityVerdict(report.InstalledVersion, report.DifferingPaths, report.ObservedAt);
            }

            case CodeIntegrityOutcome.Unavailable:
            {
                return new UnavailableCodeIntegrityVerdict(
                    report.InstalledVersion,
                    report.UnavailableReason ?? string.Empty,
                    report.ObservedAt);
            }

            default:
            {
                throw new ArgumentOutOfRangeException(nameof(report), report.Outcome, "Unknown code-integrity outcome.");
            }
        }
    }
}
#pragma warning restore CS0618

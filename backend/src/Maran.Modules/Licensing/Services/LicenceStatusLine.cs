using System.Globalization;
using Maran.Modules.Licensing.Domain.Enums;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Describes the installed licence in one plain line for the <c>maran status</c> command line. Read-only
/// and decision-free: it reports what <see cref="LicenceCoverageSource"/> found, in the words the status
/// screen uses (<see cref="LicenceStatusDisplayNames"/>), and the command that prints it only prints.
/// </summary>
/// <remarks>
/// Never carries the licence text, its signature or the machine id (the same line the status DTO draws).
/// Reads the files fresh on every call, because an operator running the command wants the truth, not a
/// remembered answer.
/// </remarks>
public sealed class LicenceStatusLine
{
    /// <summary>Reads the licence and its lease.</summary>
    private readonly LicenceCoverageSource _coverageSource;

    /// <summary>The words the status screen uses.</summary>
    private readonly LicenceStatusDisplayNames _displayNames;

    /// <summary>Creates the describer.</summary>
    /// <param name="coverageSource">Reads the licence and its lease.</param>
    /// <param name="displayNames">The words the status screen uses.</param>
    public LicenceStatusLine(LicenceCoverageSource coverageSource, LicenceStatusDisplayNames displayNames)
    {
        _coverageSource = coverageSource;
        _displayNames = displayNames;
    }

    /// <summary>Reads the licence and describes it.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One line, without a line ending.</returns>
    public async Task<string> DescribeAsync(CancellationToken cancellationToken)
    {
        var coverage = await _coverageSource.ReadAsync(cancellationToken);
        var lease = _displayNames.LeaseNameFor(coverage.Lease.Kind);

        return coverage.Status switch
        {
            LicenceStatus.Valid valid =>
                $"{_displayNames.StateNameFor(LicenceStateKind.Valid)}: {valid.Licence.Tier}, "
                + $"{valid.Licence.Expiry.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}; {lease}",
            LicenceStatus.Absent => _displayNames.StateNameFor(LicenceStateKind.Absent),
            LicenceStatus.Refused refused =>
                $"{_displayNames.StateNameFor(LicenceStateKind.Refused)}: {_displayNames.ReasonNameFor(refused.Reason)}",
            _ => throw new InvalidOperationException($"Unknown LicenceStatus case: {coverage.Status}."),
        };
    }
}

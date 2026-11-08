using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Maran.Modules.Monitoring.Domain.Enums;

namespace Maran.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// What a scheduled panel update reports: how it ended and which two releases it concerned. The
/// release names are written into the audit journal and into an operator's mail, and arrive from a
/// command-line argument, so the only way to hold a report is through <see cref="TryCreate"/>, which
/// refuses a name that is not a plain release name (rules/security.md items 1 and 8).
/// </summary>
public sealed record PanelUpdateReport
{
    /// <summary>The longest release name the installer accepts as a directory name.</summary>
    private const int ReleaseNameMaxLength = 64;

    /// <summary>The allowed alphabet, anchored at both ends; <c>\z</c> and not <c>$</c>, which also matches before a final newline.</summary>
    private static readonly Regex PlainName = new(@"^[A-Za-z0-9._-]+\z", RegexOptions.CultureInvariant);

    /// <summary>How the run ended.</summary>
    public PanelUpdateOutcome Outcome { get; }

    /// <summary>The release that was running before the update.</summary>
    public string From { get; }

    /// <summary>The release the update installed or tried to install.</summary>
    public string To { get; }

    /// <summary>Creates a report whose names have already been checked.</summary>
    /// <param name="outcome">How the run ended.</param>
    /// <param name="from">The release left.</param>
    /// <param name="to">The release installed or tried.</param>
    private PanelUpdateReport(PanelUpdateOutcome outcome, string from, string to)
    {
        Outcome = outcome;
        From = from;
        To = to;
    }

    /// <summary>Builds a report, or refuses one whose outcome is unknown or whose release names are not plain.</summary>
    /// <param name="outcome">How the run ended; an undefined value is refused.</param>
    /// <param name="from">The release left; letters, digits, dot, underscore and hyphen, at most 64.</param>
    /// <param name="to">The release installed or tried; the same alphabet and bound.</param>
    /// <param name="report">The report, when the arguments were acceptable.</param>
    /// <returns>True when a report was built.</returns>
    public static bool TryCreate(
        PanelUpdateOutcome outcome,
        string from,
        string to,
        [NotNullWhen(true)] out PanelUpdateReport? report)
    {
        report = null;
        if (!Enum.IsDefined(outcome) || !IsPlainReleaseName(from) || !IsPlainReleaseName(to))
        {
            return false;
        }

        report = new PanelUpdateReport(outcome, from, to);
        return true;
    }

    /// <summary>Whether a release name holds only characters that are safe in a journal subject and a mail.</summary>
    /// <param name="name">The candidate name.</param>
    /// <returns>True for 1 to 64 letters, digits, dots, underscores and hyphens.</returns>
    private static bool IsPlainReleaseName(string name)
    {
        return name.Length is >= 1 and <= ReleaseNameMaxLength && PlainName.IsMatch(name);
    }
}

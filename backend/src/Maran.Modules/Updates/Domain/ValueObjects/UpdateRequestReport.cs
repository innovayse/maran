using System.Diagnostics.CodeAnalysis;
using Maran.Modules.Updates.Domain.Enums;
using Maran.Modules.Updates.Domain.Policies;

namespace Maran.Modules.Updates.Domain.ValueObjects;

/// <summary>
/// What the root update reports about a run an administrator requested: a word and the release names it
/// concerned. The names are written into a task's log and the audit journal and arrive from a
/// command-line argument, so the only way to hold a report is <see cref="TryCreate"/>, which refuses a
/// name that is not plain (rules/security.md items 1 and 8).
/// </summary>
public sealed record UpdateRequestReport
{
    /// <summary>How the run stands or ended.</summary>
    public PanelUpdateRunWord Word { get; }

    /// <summary>The release that was running before the update.</summary>
    public string From { get; }

    /// <summary>The release installed or tried; null only for a word that may not know it.</summary>
    public string? To { get; }

    /// <summary>Creates a report whose parts have already been checked.</summary>
    /// <param name="word">How the run stands or ended.</param>
    /// <param name="from">The release left.</param>
    /// <param name="to">The release installed or tried.</param>
    private UpdateRequestReport(PanelUpdateRunWord word, string from, string? to)
    {
        Word = word;
        From = from;
        To = to;
    }

    /// <summary>Builds a report, or refuses one whose word is undefined, whose names are not plain, or which lacks a required name.</summary>
    /// <param name="word">The word; an undefined value is refused.</param>
    /// <param name="from">The release left; letters, digits, dot, underscore and hyphen, at most 64.</param>
    /// <param name="to">The release installed or tried; required except for <see cref="PanelUpdateRunWord.Unchanged"/> and <see cref="PanelUpdateRunWord.Refused"/>.</param>
    /// <param name="report">The report, when the arguments were acceptable.</param>
    /// <returns>True when a report was built.</returns>
    public static bool TryCreate(
        PanelUpdateRunWord word,
        string from,
        string? to,
        [NotNullWhen(true)] out UpdateRequestReport? report)
    {
        report = null;
        if (!Enum.IsDefined(word) || !PanelUpdateValueRules.IsPlainReleaseName(from))
        {
            return false;
        }

        var toOptional = word is PanelUpdateRunWord.Unchanged or PanelUpdateRunWord.Refused;
        if (to is null ? !toOptional : !PanelUpdateValueRules.IsPlainReleaseName(to))
        {
            return false;
        }

        report = new UpdateRequestReport(word, from, to);
        return true;
    }
}

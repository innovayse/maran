using Maran.Modules.Cron.Domain.Enums;
using Maran.Modules.Cron.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Cron.Services;

/// <summary>Resolves the operator-facing name of a run's outcome in the current request's culture.</summary>
/// <remarks>
/// <b>The names are the backend's to produce, not the SPA's</b> (rules/architecture.md "The backend
/// owns the data, the SPA renders it"). The set is closed and every member has an entry, so a miss is
/// a defect and not a fallback case. The options endpoint draws the history filter's labels from the
/// same <c>DisplayNames</c> keys, so the word the filter offers and the word a badge prints cannot differ.
/// </remarks>
public sealed class CronRunDisplayNames
{
    /// <summary>The prefix an outcome's resx key carries.</summary>
    private const string OutcomePrefix = "CronRunOutcome";

    /// <summary>Resolves this module's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public CronRunDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Names how a run ended, as an operator reads it.</summary>
    /// <param name="outcome">The outcome to name.</param>
    /// <returns>The localized name.</returns>
    public string Of(CronRunOutcome outcome)
    {
        return _displayNames[OutcomePrefix + outcome].Value;
    }
}

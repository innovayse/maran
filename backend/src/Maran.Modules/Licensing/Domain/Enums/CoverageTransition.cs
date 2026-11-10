namespace Maran.Modules.Licensing.Domain.Enums;

/// <summary>
/// What a change in one module's licence cover means for the audit trail, as
/// <see cref="Policies.CoverageTransitionPolicy"/> classifies it. Only the changes an administrator
/// needs to read about are named; every other change is <see cref="None"/>.
/// </summary>
public enum CoverageTransition
{
    /// <summary>Nothing worth a journal entry: the cover did not change, or the change is not a lapse or a recovery.</summary>
    None,

    /// <summary>The module's cover ran out: it is now lapsed and was not lapsed when last observed.</summary>
    Lapsed,

    /// <summary>A lapsed module is covered again.</summary>
    Recovered,
}

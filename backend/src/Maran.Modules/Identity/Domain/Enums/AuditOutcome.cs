namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>
/// How a recorded action ended, as the audit journal is filtered by it. The row itself stores the
/// answer as <c>AuditEvent.Succeeded</c>; this type names the two answers so a filter states which
/// one it wants rather than passing a flag whose absence would have to mean "either".
/// </summary>
public enum AuditOutcome
{
    /// <summary>The action worked.</summary>
    Succeeded,

    /// <summary>The action was refused or failed.</summary>
    Failed,
}

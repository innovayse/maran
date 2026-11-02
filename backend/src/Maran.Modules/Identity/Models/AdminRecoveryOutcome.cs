namespace Maran.Modules.Identity.Models;

/// <summary>
/// What <see cref="Services.AdminRecoveryIssuer.IssueAsync"/> did, when it did not refuse: exactly
/// one of two cases, each its own type, so "no administrator exists yet" is a case the console must
/// name and not a <c>null</c> payload it can dereference by mistake.
/// </summary>
/// <remarks>
/// Only this assembly can derive from it, so <see cref="AdminRecoveryIssued"/> and
/// <see cref="NoAdministratorExists"/> are the whole set. A refusal is not a case here: it travels
/// as the failure of the surrounding <c>Result</c>.
/// </remarks>
public abstract record AdminRecoveryOutcome
{
    /// <summary>Closes the hierarchy to the two cases this assembly declares.</summary>
    private protected AdminRecoveryOutcome()
    {
    }
}

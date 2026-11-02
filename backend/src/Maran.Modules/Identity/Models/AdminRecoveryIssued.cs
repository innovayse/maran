namespace Maran.Modules.Identity.Models;

/// <summary>An administrator's access was recovered and a reset link was minted.</summary>
/// <param name="Recovery">The recovery, carrying the link with the plaintext token inside it.</param>
public sealed record AdminRecoveryIssued(IssuedAdminRecovery Recovery) : AdminRecoveryOutcome;

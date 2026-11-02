namespace Maran.Modules.Identity.Models;

/// <summary>
/// No administrator has ever been created, so there is nothing to recover: first-run setup is the
/// way in, and a reset token would be a key to a door that does not exist.
/// </summary>
public sealed record NoAdministratorExists : AdminRecoveryOutcome;

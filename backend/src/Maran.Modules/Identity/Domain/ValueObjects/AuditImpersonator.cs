namespace Maran.Modules.Identity.Domain.ValueObjects;

/// <summary>
/// The operator who was viewing the panel as an audit row's actor when it happened (issue #317).
/// Exists only for such a row; a row written by an actor acting as themselves has no impersonator
/// (<see cref="Entities.AuditEvent.Impersonator"/> is null), so a name never sits beside a missing id.
/// </summary>
/// <param name="OperatorId">
/// The operator's login id. Deliberately not named for the user entity it points at: the journal records
/// who it was and must outlive that login, so the column is history and not a relationship the account
/// cascade could follow.
/// </param>
/// <param name="Username">The operator's login name at the time.</param>
public sealed record AuditImpersonator(Guid OperatorId, string Username);

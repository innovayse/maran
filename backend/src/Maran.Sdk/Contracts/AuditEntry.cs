namespace Maran.Sdk.Contracts;

/// <summary>
/// One thing that happened, on its way to the panel's append-only journal (spec §10). Every module
/// records its mutations through this shape, so one screen can answer "who did what, when, from
/// where" for the whole panel.
/// </summary>
/// <remarks>
/// The record carries no free-form payload and no secret-shaped field on purpose: there is nowhere
/// for a password, a token or a recovery code to travel, so no future caller can leak one into a
/// journal that is, by design, never deleted (rules/security.md item 8).
/// </remarks>
/// <param name="ActorUserId">Who did it, or <c>null</c> when nobody could be identified — as on a failed login.</param>
/// <param name="ActorUsername">The name the actor used. It exists even when the user does not.</param>
/// <param name="Action">What was attempted; one of <see cref="AuditActions"/>.</param>
/// <param name="Subject">What it was attempted on: an account name, a session id, a username.</param>
/// <param name="IpAddress">Where the request came from.</param>
/// <param name="UserAgent">What client it came from.</param>
/// <param name="Succeeded">Whether it worked. Failures are the half of the journal worth reading.</param>
public sealed record AuditEntry(
    Guid? ActorUserId,
    string ActorUsername,
    string Action,
    string Subject,
    string IpAddress,
    string UserAgent,
    bool Succeeded);

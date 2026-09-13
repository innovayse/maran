namespace Maran.Modules.Identity.Queries.ListSessions;

/// <summary>Lists one user's live sessions.</summary>
/// <param name="UserId">
/// Whose sessions to list. Taken from the caller's own token by the controller and never from the
/// request, so there is no parameter with which to ask for somebody else's devices.
/// </param>
/// <param name="CurrentSessionId">The caller's own session, marked in the result.</param>
public sealed record ListSessionsQuery(Guid UserId, Guid CurrentSessionId);

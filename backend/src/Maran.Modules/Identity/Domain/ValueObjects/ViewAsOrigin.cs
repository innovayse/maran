namespace Maran.Modules.Identity.Domain.ValueObjects;

/// <summary>
/// Who is behind a view-as session: the operator and the operator's own session it was started
/// from. Exists only for a view-as session; an own session has no such origin, which is why
/// <see cref="Entities.Session.ViewAs"/> is null for it and not an origin of empty ids. Stored in the
/// session's <c>ActorUserId</c> and <c>ActorSessionId</c> columns, which are both null for an own session.
/// </summary>
/// <param name="ActorUserId">The operator viewing as the session's user.</param>
/// <param name="ActorSessionId">
/// The operator's own session the view-as session was started from. Its rotation family must stay
/// live for the view-as session to be accepted, so signing the operator out ends it too.
/// </param>
public sealed record ViewAsOrigin(Guid ActorUserId, Guid ActorSessionId);

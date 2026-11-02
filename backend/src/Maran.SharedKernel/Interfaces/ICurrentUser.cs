namespace Maran.SharedKernel.Interfaces;

/// <summary>The authenticated principal of the current request/message.</summary>
public interface ICurrentUser
{
    /// <summary>Panel user id.</summary>
    Guid UserId { get; }

    /// <summary>
    /// The panel user's login name, as the audit journal records it.
    /// </summary>
    /// <remarks>
    /// Empty outside a request and for an unauthenticated caller — never null, so a journal entry
    /// always has a string to carry. The journal names the actor, and an id alone is unreadable in
    /// the one screen whose whole job is answering "who did what" (spec §10).
    /// </remarks>
    string Username { get; }

    /// <summary>Owning account for Customer contexts; null for Admin.</summary>
    Guid? AccountId { get; }

    /// <summary>True for server administrators.</summary>
    bool IsAdmin { get; }

    /// <summary>
    /// The operator actually at the keyboard while they view the panel as <see cref="UserId"/>; null
    /// for every other request, in which the user is acting as themselves.
    /// </summary>
    /// <remarks>
    /// Every answer above describes the login being viewed as, because that is whose authority the
    /// request runs under — tenant scope, role, everything. This one is what keeps the audit journal
    /// truthful about who did it (issue #317). A default member, so an implementer that cannot see a
    /// token — a design-time context, a test double — reports "no actor" without being edited; the
    /// request-bound implementation overrides it.
    /// </remarks>
    Guid? ActorUserId
    {
        get
        {
            return null;
        }
    }

    /// <summary>
    /// The login name of <see cref="ActorUserId"/>; empty whenever that is null, never null itself, as
    /// <see cref="Username"/> is.
    /// </summary>
    string ActorUsername
    {
        get
        {
            return string.Empty;
        }
    }
}

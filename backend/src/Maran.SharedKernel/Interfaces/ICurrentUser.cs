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
}

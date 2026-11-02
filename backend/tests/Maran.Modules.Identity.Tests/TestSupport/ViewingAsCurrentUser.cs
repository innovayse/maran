using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>An <see cref="ICurrentUser"/> double for a request made with a view-as token.</summary>
public sealed class ViewingAsCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public Guid UserId { get; }

    /// <inheritdoc />
    public string Username { get; }

    /// <inheritdoc />
    public Guid? AccountId
    {
        get
        {
            return null;
        }
    }

    /// <inheritdoc />
    public bool IsAdmin
    {
        get
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Guid? ActorUserId { get; }

    /// <inheritdoc />
    public string ActorUsername { get; }

    /// <summary>Speaks for a customer login being viewed as by a named operator.</summary>
    /// <param name="userId">The viewed login.</param>
    /// <param name="username">The viewed login's name.</param>
    /// <param name="actorUserId">The operator viewing.</param>
    /// <param name="actorUsername">The operator's login name.</param>
    public ViewingAsCurrentUser(Guid userId, string username, Guid actorUserId, string actorUsername)
    {
        UserId = userId;
        Username = username;
        ActorUserId = actorUserId;
        ActorUsername = actorUsername;
    }
}

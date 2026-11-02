using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>An <see cref="ICurrentUser"/> double for a request made with a view-as token.</summary>
public sealed class ViewingAsCurrentUser : ICurrentUser
{
    /// <summary>The account the viewed customer acts for; a view-as session is always for a customer.</summary>
    private static readonly Guid ViewedAccountId = new("cccccccc-0000-4000-8000-000000000001");

    /// <inheritdoc />
    public Caller Caller { get; }

    /// <summary>Speaks for a customer login being viewed as by a named operator.</summary>
    /// <param name="userId">The viewed login.</param>
    /// <param name="username">The viewed login's name.</param>
    /// <param name="actorUserId">The operator viewing.</param>
    /// <param name="actorUsername">The operator's login name.</param>
    public ViewingAsCurrentUser(Guid userId, string username, Guid actorUserId, string actorUsername)
    {
        Caller = new CustomerCaller(
            userId, username, ViewedAccountId, new ViewingOperator(actorUserId, actorUsername));
    }
}

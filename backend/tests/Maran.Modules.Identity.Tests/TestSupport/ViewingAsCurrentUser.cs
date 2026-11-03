using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// An <see cref="ICurrentUser"/> double for a request made with a view-as token: a customer login
/// through the constructor, a reseller login through <see cref="ForReseller"/>.
/// </summary>
public sealed class ViewingAsCurrentUser : ICurrentUser
{
    /// <summary>The account the viewed customer acts for.</summary>
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

    /// <summary>Wraps a caller built by <see cref="ForReseller"/>.</summary>
    /// <param name="caller">The principal the token speaks for.</param>
    private ViewingAsCurrentUser(Caller caller)
    {
        Caller = caller;
    }

    /// <summary>Speaks for a reseller login being viewed as by a named operator.</summary>
    /// <param name="userId">The viewed login.</param>
    /// <param name="username">The viewed login's name.</param>
    /// <param name="resellerId">The reseller the viewed login belongs to.</param>
    /// <param name="actorUserId">The operator viewing.</param>
    /// <param name="actorUsername">The operator's login name.</param>
    /// <returns>The double.</returns>
    public static ViewingAsCurrentUser ForReseller(
        Guid userId,
        string username,
        Guid resellerId,
        Guid actorUserId,
        string actorUsername)
    {
        return new ViewingAsCurrentUser(
            new ResellerCaller(userId, username, resellerId, new ViewingOperator(actorUserId, actorUsername)));
    }
}

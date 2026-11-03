using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>A signed-in reseller, so the operations that read the caller have a reseller principal.</summary>
public sealed class StubResellerUser : ICurrentUser
{
    /// <summary>The reseller's login id.</summary>
    public Guid UserId { get; }

    /// <summary>The reseller the login belongs to.</summary>
    public Guid ResellerId { get; }

    /// <inheritdoc />
    public Caller Caller { get; }

    /// <summary>Creates the principal.</summary>
    /// <param name="userId">The login's id.</param>
    /// <param name="username">The login name.</param>
    /// <param name="resellerId">The reseller the login belongs to.</param>
    public StubResellerUser(Guid userId, string username, Guid resellerId)
    {
        UserId = userId;
        ResellerId = resellerId;
        Caller = new ResellerCaller(userId, username, resellerId);
    }
}

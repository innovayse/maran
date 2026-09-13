using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Cron.Tests.TestSupport;

/// <summary>
/// An <see cref="ICurrentUser"/> double, so a test can BE a particular tenant. This module has no
/// query filter to close over it; what reads it is the audit journal, which records this principal
/// as the actor of every entry.
/// </summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public Guid UserId { get; }

    /// <inheritdoc />
    public string Username { get; }

    /// <inheritdoc />
    public Guid? AccountId { get; }

    /// <inheritdoc />
    public bool IsAdmin { get; }

    /// <summary>Creates a principal.</summary>
    /// <param name="userId">The panel user id.</param>
    /// <param name="accountId">The owning account for a customer, or null.</param>
    /// <param name="isAdmin">Whether the principal is a server administrator.</param>
    /// <param name="username">The login name the audit journal records.</param>
    public FakeCurrentUser(Guid userId, Guid? accountId, bool isAdmin, string username = "tester")
    {
        UserId = userId;
        Username = username;
        AccountId = accountId;
        IsAdmin = isAdmin;
    }

    /// <summary>Creates a customer principal owning <paramref name="accountId"/>.</summary>
    /// <param name="accountId">The account the customer owns.</param>
    public static FakeCurrentUser Customer(Guid accountId)
    {
        return new FakeCurrentUser(Guid.NewGuid(), accountId, isAdmin: false);
    }

    /// <summary>Creates an administrator principal, which no tenant scope narrows.</summary>
    public static FakeCurrentUser Admin()
    {
        return new FakeCurrentUser(Guid.NewGuid(), null, isAdmin: true);
    }
}

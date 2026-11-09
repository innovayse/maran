using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>
/// An <see cref="ICurrentUser"/> double, so a test can BE a particular tenant. The tenant filter on
/// <c>SitesDbContext</c> closes over this, which is the whole subject of the isolation tests.
/// </summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public Caller Caller { get; }

    /// <summary>Creates a principal.</summary>
    /// <param name="userId">The panel user id.</param>
    /// <param name="accountId">The owning account for a customer, or null.</param>
    /// <param name="isAdmin">Whether the principal is a server administrator.</param>
    /// <param name="username">The login name the audit journal records.</param>
    public FakeCurrentUser(Guid userId, Guid? accountId, bool isAdmin, string username = "tester")
    {
        Caller = CallerFor(userId, accountId, isAdmin, username);
    }

    /// <summary>Creates a principal that already is a particular caller.</summary>
    /// <param name="caller">The caller this double hands out.</param>
    private FakeCurrentUser(Caller caller)
    {
        Caller = caller;
    }

    /// <summary>Creates a customer principal owning <paramref name="accountId"/>.</summary>
    /// <param name="accountId">The account the customer owns.</param>
    public static FakeCurrentUser Customer(Guid accountId)
    {
        return new FakeCurrentUser(Guid.NewGuid(), accountId, isAdmin: false);
    }

    /// <summary>Creates the principal of unattended work: nobody is signed in, so every tenant filter shows nothing.</summary>
    /// <returns>The anonymous principal.</returns>
    public static FakeCurrentUser Anonymous()
    {
        return new FakeCurrentUser(AnonymousCaller.Instance);
    }

    /// <summary>Creates an administrator principal, which no tenant filter narrows.</summary>
    /// <returns>The administrator.</returns>
    public static FakeCurrentUser Admin()
    {
        return new FakeCurrentUser(Guid.NewGuid(), null, isAdmin: true);
    }

    /// <summary>
    /// Creates a reseller principal, which owns accounts in the Accounts module and no row at all
    /// in this one, so a tenant scope must show it nothing.
    /// </summary>
    /// <param name="resellerId">The reseller the login acts for.</param>
    public static FakeCurrentUser Reseller(Guid resellerId)
    {
        return new FakeCurrentUser(new ResellerCaller(Guid.NewGuid(), "reseller", resellerId));
    }

    /// <summary>
    /// Picks the caller kind the arguments describe: an administrator, a customer acting for an
    /// account, or — for a non-administrator with no account — a customer who owns nothing, which is
    /// what a tenant filter must show no row.
    /// </summary>
    /// <param name="userId">The panel user id.</param>
    /// <param name="accountId">The owning account for a customer, or null.</param>
    /// <param name="isAdmin">Whether the principal is a server administrator.</param>
    /// <param name="username">The login name the audit journal records.</param>
    /// <returns>The caller.</returns>
    private static Caller CallerFor(Guid userId, Guid? accountId, bool isAdmin, string username)
    {
        if (isAdmin)
        {
            return new AdministratorCaller(userId, username);
        }

        return accountId is { } account
            ? new CustomerCaller(userId, username, account)
            : new AccountlessCustomerCaller(userId, username);
    }
}

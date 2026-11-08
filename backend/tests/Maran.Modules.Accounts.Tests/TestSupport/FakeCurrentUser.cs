using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// An <see cref="ICurrentUser"/> double, so a test can BE a particular tenant. The account
/// directory's tenant scope closes over this, which is the subject of its isolation tests.
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

    /// <summary>Creates a customer principal owning <paramref name="accountId"/>.</summary>
    /// <param name="accountId">The account the customer owns.</param>
    public static FakeCurrentUser Customer(Guid accountId)
    {
        return new FakeCurrentUser(Guid.NewGuid(), accountId, isAdmin: false);
    }

    /// <summary>Creates a reseller principal acting for <paramref name="resellerId"/>.</summary>
    /// <param name="resellerId">The reseller the login acts for.</param>
    /// <param name="username">The login name the audit journal records.</param>
    public static FakeCurrentUser Reseller(Guid resellerId, string username = "reseller")
    {
        return new FakeCurrentUser(new ResellerCaller(Guid.NewGuid(), username, resellerId));
    }

    /// <summary>An API key a reseller owns, or the administrator's when <paramref name="resellerId"/> is null.</summary>
    /// <param name="resellerId">The reseller that owns the key, or <c>null</c> for the administrator's.</param>
    /// <returns>The fake current user.</returns>
    public static FakeCurrentUser ApiKey(Guid? resellerId)
    {
        return new FakeCurrentUser(new ApiKeyCaller(Guid.NewGuid(), [], resellerId));
    }

    /// <summary>Creates an unattended-work principal: nobody signed in.</summary>
    public static FakeCurrentUser Anonymous()
    {
        return new FakeCurrentUser(AnonymousCaller.Instance);
    }

    /// <summary>Creates a customer who owns no account, which a tenant filter must show no row.</summary>
    public static FakeCurrentUser AccountlessCustomer()
    {
        return new FakeCurrentUser(Guid.NewGuid(), null, isAdmin: false);
    }

    /// <summary>Creates an administrator principal.</summary>
    public static FakeCurrentUser Admin()
    {
        return new FakeCurrentUser(Guid.NewGuid(), null, isAdmin: true);
    }

    /// <summary>Creates a principal that reports exactly <paramref name="caller"/>.</summary>
    /// <param name="caller">The caller every query for this principal sees.</param>
    public FakeCurrentUser(Caller caller)
    {
        Caller = caller;
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

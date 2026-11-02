using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Tasks.Tests.TestSupport;

/// <summary>
/// An <see cref="ICurrentUser"/> double, so a test can BE an administrator or a customer. The
/// tenant query filter on <c>TasksDbContext</c> closes over this, which is the whole subject
/// of the visibility tests.
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

    /// <summary>Creates a customer principal owning a fresh account no seeded task belongs to.</summary>
    /// <returns>The principal.</returns>
    public static FakeCurrentUser Customer()
    {
        return new FakeCurrentUser(Guid.NewGuid(), Guid.NewGuid(), isAdmin: false);
    }

    /// <summary>Creates a customer principal owning the given account.</summary>
    /// <param name="accountId">The account the customer owns.</param>
    /// <returns>The principal.</returns>
    public static FakeCurrentUser Customer(Guid accountId)
    {
        return new FakeCurrentUser(Guid.NewGuid(), accountId, isAdmin: false);
    }

    /// <summary>Creates an administrator principal, which the module's query filter admits.</summary>
    public static FakeCurrentUser Admin()
    {
        return new FakeCurrentUser(Guid.NewGuid(), null, isAdmin: true);
    }

    /// <summary>
    /// Picks the caller kind the arguments describe: an administrator, a customer, or — for a
    /// non-administrator that owns no account — anonymous, which owns nothing a tenant filter shows.
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

        return accountId is { } account ? new CustomerCaller(userId, username, account) : AnonymousCaller.Instance;
    }
}

using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Tasks.Tests.TestSupport;

/// <summary>
/// An <see cref="ICurrentUser"/> double, so a test can BE an administrator or a customer. The
/// administrator query filter on <c>TasksDbContext</c> closes over this, which is the whole subject
/// of the visibility tests.
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
    /// <param name="username">The login name.</param>
    public FakeCurrentUser(Guid userId, Guid? accountId, bool isAdmin, string username = "tester")
    {
        UserId = userId;
        Username = username;
        AccountId = accountId;
        IsAdmin = isAdmin;
    }

    /// <summary>Creates a customer principal, for whom this whole surface does not exist.</summary>
    public static FakeCurrentUser Customer()
    {
        return new FakeCurrentUser(Guid.NewGuid(), Guid.NewGuid(), isAdmin: false);
    }

    /// <summary>Creates an administrator principal, which the module's query filter admits.</summary>
    public static FakeCurrentUser Admin()
    {
        return new FakeCurrentUser(Guid.NewGuid(), null, isAdmin: true);
    }
}

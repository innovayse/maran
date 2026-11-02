using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>An <see cref="ICurrentUser"/> double, so a test can BE a particular principal.</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public Caller Caller { get; }

    /// <summary>Creates a principal.</summary>
    /// <param name="isAdmin">Whether the principal is a server administrator.</param>
    /// <param name="username">The login name the audit journal records.</param>
    public FakeCurrentUser(bool isAdmin, string username = "tester")
    {
        Caller = isAdmin
            ? new AdministratorCaller(Guid.NewGuid(), username)
            : new CustomerCaller(Guid.NewGuid(), username, Guid.NewGuid());
    }
}

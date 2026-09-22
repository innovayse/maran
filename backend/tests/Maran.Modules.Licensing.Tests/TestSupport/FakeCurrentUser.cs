using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>An <see cref="ICurrentUser"/> double, so a test can BE a particular principal.</summary>
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
    /// <param name="isAdmin">Whether the principal is a server administrator.</param>
    /// <param name="username">The login name the audit journal records.</param>
    public FakeCurrentUser(bool isAdmin, string username = "tester")
    {
        UserId = Guid.NewGuid();
        Username = username;
        AccountId = isAdmin ? null : Guid.NewGuid();
        IsAdmin = isAdmin;
    }
}

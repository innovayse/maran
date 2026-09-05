using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Firewall.Tests.TestSupport;

/// <summary>
/// An <see cref="ICurrentUser"/> double, so a test can be the administrator whose name the audit
/// journal records. Nothing in this module is tenant-scoped, so the account id is always absent.
/// </summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public Guid UserId { get; }

    /// <inheritdoc />
    public string Username { get; }

    /// <inheritdoc />
    public Guid? AccountId
    {
        get
        {
            return null;
        }
    }

    /// <inheritdoc />
    public bool IsAdmin
    {
        get
        {
            return true;
        }
    }

    /// <summary>Creates an administrator principal.</summary>
    /// <param name="username">The login name the audit journal records.</param>
    public FakeCurrentUser(string username = "admin")
    {
        UserId = Guid.NewGuid();
        Username = username;
    }
}

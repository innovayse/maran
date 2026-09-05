using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>An <see cref="ICurrentUser"/> double naming one signed-in administrator.</summary>
public sealed class StubCurrentUser : ICurrentUser
{
    /// <summary>Speaks for a freshly invented administrator.</summary>
    public StubCurrentUser()
        : this(Guid.NewGuid(), "admin")
    {
    }

    /// <summary>Speaks for a named administrator, so a test can make the principal be a given user.</summary>
    /// <param name="userId">The id the principal carries.</param>
    /// <param name="username">The login name the principal carries.</param>
    public StubCurrentUser(Guid userId, string username)
    {
        UserId = userId;
        Username = username;
    }

    /// <summary>The administrator this double speaks for.</summary>
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
}

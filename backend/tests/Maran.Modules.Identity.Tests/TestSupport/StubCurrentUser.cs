using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>An <see cref="ICurrentUser"/> double naming one signed-in administrator.</summary>
public sealed class StubCurrentUser : ICurrentUser
{
    /// <summary>The administrator's id; named here because tests assert the audit actor against it.</summary>
    public Guid UserId { get; }

    /// <summary>The administrator's login name; named here because tests assert the audit actor against it.</summary>
    public string Username { get; }

    /// <inheritdoc />
    public Caller Caller { get; }

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
        Caller = new AdministratorCaller(userId, username);
    }
}

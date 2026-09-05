using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Monitoring.Tests.TestSupport;

/// <summary>A signed-in administrator, or nobody, depending on what a test sets.</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    /// <summary>The panel user id recorded as an audit entry's actor.</summary>
    public Guid UserId { get; set; } = Guid.NewGuid();

    /// <summary>The name recorded as an audit entry's actor.</summary>
    public string Username { get; set; } = "admin";

    /// <summary>Always null: this module's whole surface is administrator-only, and admins have no account.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>True, matching the only role that can reach this module.</summary>
    public bool IsAdmin { get; set; } = true;
}

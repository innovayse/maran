using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.ServerMigration.Tests.TestSupport;

/// <summary>The signed-in administrator every handler test acts as.</summary>
public sealed class AdministratorUser : ICurrentUser
{
    /// <summary>The administrator's identifier.</summary>
    public static readonly Guid UserId = new("2f7a9f2a-6a1e-4f5a-9c1f-0b6c5a2e7d10");

    /// <summary>The administrator.</summary>
    public Caller Caller { get; } = new AdministratorCaller(UserId, "admin");
}

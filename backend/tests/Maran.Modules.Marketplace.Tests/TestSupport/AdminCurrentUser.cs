using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>A principal that is an administrator, the only kind of caller the marketplace serves.</summary>
public sealed class AdminCurrentUser : ICurrentUser
{
    /// <summary>The administrator's user id.</summary>
    public static readonly Guid UserId = new("2f7a9f2a-6a1e-4f5a-9c1f-0b6c5a2e7d10");

    /// <summary>The administrator.</summary>
    public Caller Caller { get; } = new AdministratorCaller(UserId, "admin");
}

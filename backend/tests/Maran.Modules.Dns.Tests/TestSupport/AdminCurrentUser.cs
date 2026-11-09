using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>A principal that is an administrator, who sees every account's zones.</summary>
public sealed class AdminCurrentUser : ICurrentUser
{
    /// <summary>The administrator.</summary>
    public Caller Caller { get; } = new AdministratorCaller(new Guid("2f7a9f2a-6a1e-4f5a-9c1f-0b6c5a2e7d10"), "admin");
}

using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Monitoring.Tests.TestSupport;

/// <summary>A signed-in administrator, the only role that can reach this module.</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public Caller Caller { get; } = new AdministratorCaller(Guid.NewGuid(), "admin");
}

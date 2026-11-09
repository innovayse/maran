using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Updates.Tests.TestSupport;

/// <summary>A signed-in administrator, the only role that can reach this module.</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    /// <summary>The administrator's id, so a test can assert the journal names them.</summary>
    public static readonly Guid AdministratorId = Guid.Parse("a0a0a0a0-0000-4000-8000-00000000a0a0");

    /// <inheritdoc />
    public Caller Caller { get; } = new AdministratorCaller(AdministratorId, "admin");
}

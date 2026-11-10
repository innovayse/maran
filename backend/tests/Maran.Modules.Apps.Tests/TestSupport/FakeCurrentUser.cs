using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Apps.Tests.TestSupport;

/// <summary>An <see cref="ICurrentUser"/> double that is a signed-in administrator, the only caller the app store serves.</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public Caller Caller { get; } = new AdministratorCaller(Guid.NewGuid(), "admin");
}

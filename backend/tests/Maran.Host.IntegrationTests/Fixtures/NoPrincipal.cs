using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The "current user" of a background service: nobody. Not an administrator and owning no account,
/// so a tenant query filter that is not bypassed returns nothing at all.
/// </summary>
public sealed class NoPrincipal : ICurrentUser
{
    /// <inheritdoc />
    public Caller Caller { get; } = AnonymousCaller.Instance;
}

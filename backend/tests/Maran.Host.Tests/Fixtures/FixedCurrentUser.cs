using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Host.Tests.Fixtures;

/// <summary>An <see cref="ICurrentUser"/> that reports one caller, whatever the request carries.</summary>
/// <remarks>
/// It lets a host test choose the caller case without minting a token: the behaviour under test is
/// what the host does for a given <see cref="Caller"/>, not how a token becomes one (that is
/// <c>HttpContextCurrentUserTests</c>).
/// </remarks>
public sealed class FixedCurrentUser : ICurrentUser
{
    /// <summary>The caller every read reports.</summary>
    public Caller Caller { get; }

    /// <summary>Creates the stand-in.</summary>
    /// <param name="caller">The caller to report.</param>
    public FixedCurrentUser(Caller caller)
    {
        Caller = caller;
    }
}

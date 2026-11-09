using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Files.Tests.TestSupport;

/// <summary>A current user fixed to the caller a test names.</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    /// <summary>Creates the double.</summary>
    /// <param name="caller">The caller every read of <see cref="Caller"/> answers.</param>
    public FakeCurrentUser(Caller caller)
    {
        Caller = caller;
    }

    /// <inheritdoc />
    public Caller Caller { get; }
}

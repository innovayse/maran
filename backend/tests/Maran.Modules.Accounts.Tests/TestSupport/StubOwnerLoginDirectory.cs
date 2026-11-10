using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// An <see cref="IOwnerLoginDirectory"/> double: it reports the clash it was given for every question
/// and records each question asked.
/// </summary>
public sealed class StubOwnerLoginDirectory : IOwnerLoginDirectory
{
    /// <summary>The clash reported for every question.</summary>
    private readonly OwnerLoginConflict _conflict;

    /// <summary>Creates a double that reports <paramref name="conflict"/>.</summary>
    /// <param name="conflict">The clash to report; none by default.</param>
    public StubOwnerLoginDirectory(OwnerLoginConflict conflict = OwnerLoginConflict.None)
    {
        _conflict = conflict;
    }

    /// <summary>The (name, e-mail) pair of every question asked, in order.</summary>
    public List<(string Username, string Email)> Asked { get; } = [];

    /// <inheritdoc />
    public Task<OwnerLoginConflict> FindConflictAsync(string username, string email, CancellationToken cancellationToken)
    {
        Asked.Add((username, email));

        return Task.FromResult(_conflict);
    }
}

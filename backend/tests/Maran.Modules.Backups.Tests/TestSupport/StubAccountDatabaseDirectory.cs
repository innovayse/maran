using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Tests.TestSupport;

/// <summary>
/// An <see cref="IAccountDatabaseDirectory"/> double holding a fixed set of names per account.
/// </summary>
/// <remarks>
/// Answers an empty list for an account it was not given, which is how the real implementation
/// answers both for an account with no databases and for one in another tenant — so a test can drive
/// the empty case without standing up the Databases module.
/// </remarks>
public sealed class StubAccountDatabaseDirectory : IAccountDatabaseDirectory
{
    /// <summary>What this directory knows, keyed by account id.</summary>
    private readonly Dictionary<Guid, IReadOnlyList<string>> _names;

    /// <summary>Creates a directory knowing one account's databases.</summary>
    /// <param name="accountId">The account the names belong to.</param>
    /// <param name="names">The fully-qualified database names.</param>
    public StubAccountDatabaseDirectory(Guid accountId, params string[] names)
    {
        _names = new Dictionary<Guid, IReadOnlyList<string>> { [accountId] = names };
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListNamesAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_names.GetValueOrDefault(accountId, []));
    }
}

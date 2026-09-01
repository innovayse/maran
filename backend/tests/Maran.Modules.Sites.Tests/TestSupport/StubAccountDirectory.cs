using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>
/// An <see cref="IAccountDirectory"/> double holding a fixed set of snapshots. Answers null for
/// anything it was not given, which is exactly how the real implementation answers for an account
/// in another tenant — so a test can drive that path without standing up the Accounts module.
/// </summary>
public sealed class StubAccountDirectory : IAccountDirectory
{
    /// <summary>What this directory knows, keyed by account id.</summary>
    private readonly Dictionary<Guid, AccountSnapshot> _snapshots;

    /// <summary>Account ids this directory was asked about, in order.</summary>
    public List<Guid> Lookups { get; } = [];

    /// <summary>Creates a directory knowing the given snapshots.</summary>
    /// <param name="snapshots">The accounts this directory can answer for.</param>
    public StubAccountDirectory(params AccountSnapshot[] snapshots)
    {
        _snapshots = snapshots.ToDictionary(snapshot =>
        {
            return snapshot.Id;
        });
    }

    /// <inheritdoc />
    public Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken)
    {
        Lookups.Add(accountId);
        return Task.FromResult(_snapshots.GetValueOrDefault(accountId));
    }
}

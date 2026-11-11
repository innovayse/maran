using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Files.Tests.TestSupport;

/// <summary>
/// An account directory that knows exactly the accounts a test gives it, so an id outside the set
/// behaves like another tenant's account: null.
/// </summary>
public sealed class StubAccountDirectory : IAccountDirectory
{
    /// <summary>The snapshots by id.</summary>
    private readonly Dictionary<Guid, AccountSnapshot> _snapshots;

    /// <summary>Creates the double.</summary>
    /// <param name="snapshots">The accounts the caller may see.</param>
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
        return Task.FromResult(_snapshots.GetValueOrDefault(accountId));
    }

    /// <inheritdoc />
    public Task<AccountSnapshot?> FindForViewAsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Files tests start no view-as session.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AccountSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<AccountSnapshot>>([.. _snapshots.Values]);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<Guid, string>> NamesOfAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Files reads no account names.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> FindIdsByNameAsync(string search, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Files searches no account names.");
    }

    /// <inheritdoc />
    public Task<AccountSnapshot?> FindForSystemAsync(Guid accountId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("These tests read no account unscoped.");
    }

    /// <inheritdoc />
    public Task<AccountSystemUser?> FindSystemUserAsync(Guid accountId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Files reads no system user unscoped.");
    }
}

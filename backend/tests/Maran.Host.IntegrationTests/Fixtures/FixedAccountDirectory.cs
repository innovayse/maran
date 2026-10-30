using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// An <see cref="IAccountDirectory"/> that knows a fixed set of accounts, for a test that needs more
/// than the one <see cref="OneAccountDirectory"/> holds.
/// </summary>
public sealed class FixedAccountDirectory : IAccountDirectory
{
    /// <summary>Every account this directory knows.</summary>
    private readonly IReadOnlyList<AccountSnapshot> _snapshots;

    /// <summary>Creates the directory.</summary>
    /// <param name="snapshots">The accounts it answers for.</param>
    public FixedAccountDirectory(params AccountSnapshot[] snapshots)
    {
        _snapshots = snapshots;
    }

    /// <inheritdoc />
    public Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_snapshots.FirstOrDefault(snapshot =>
        {
            return snapshot.Id == accountId;
        }));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AccountSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_snapshots);
    }

    /// <inheritdoc />
    /// <remarks>Answers for every id it knows; like the rest of this double it models no tenant scope.</remarks>
    public Task<IReadOnlyDictionary<Guid, string>> NamesOfAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, string> names = _snapshots
            .Where(snapshot =>
            {
                return accountIds.Contains(snapshot.Id);
            })
            .ToDictionary(
                snapshot =>
                {
                    return snapshot.Id;
                },
                snapshot =>
                {
                    return snapshot.Username;
                });
        return Task.FromResult(names);
    }

    /// <inheritdoc />
    /// <remarks>A literal, case-insensitive containment over what it knows, as the real one answers.</remarks>
    public Task<IReadOnlyList<Guid>> FindIdsByNameAsync(string search, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> ids = _snapshots
            .Where(snapshot =>
            {
                return snapshot.Username.Contains(search, StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(
                snapshot =>
                {
                    return snapshot.Username;
                },
                StringComparer.Ordinal)
            .Select(snapshot =>
            {
                return snapshot.Id;
            })
            .ToList();
        return Task.FromResult(ids);
    }
}

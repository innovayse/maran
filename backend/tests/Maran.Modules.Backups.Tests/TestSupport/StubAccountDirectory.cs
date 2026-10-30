using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Tests.TestSupport;

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

    /// <summary>Each batch of ids <see cref="NamesOfAsync"/> was asked for, in order.</summary>
    public List<IReadOnlyCollection<Guid>> NameLookups { get; } = [];

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

    /// <inheritdoc />
    public Task<IReadOnlyList<AccountSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        // Unscoped, exactly as the contract says the real one is: every snapshot this double was
        // given, with no reference to any current user. A double that quietly filtered here would
        // make an unscoped method look scoped and hide the very exposure the interface documents.
        // The module's two unattended passes — the scheduled sweep and retention — are its callers:
        // both run off the panel's own timer with no principal, which is the one situation the
        // scoped FindAsync cannot serve because it would answer null for every account, for ever.
        return Task.FromResult<IReadOnlyList<AccountSnapshot>>([.. _snapshots.Values]);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Answers only for the snapshots it was given — an id it does not know is absent, which is how
    /// the real directory answers for an account that no longer exists or belongs to another tenant.
    /// </remarks>
    public Task<IReadOnlyDictionary<Guid, string>> NamesOfAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        NameLookups.Add(accountIds);
        IReadOnlyDictionary<Guid, string> names = accountIds
            .Distinct()
            .Where(id =>
            {
                return _snapshots.ContainsKey(id);
            })
            .ToDictionary(
                id =>
                {
                    return id;
                },
                id =>
                {
                    return _snapshots[id].Username;
                });
        return Task.FromResult(names);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> FindIdsByNameAsync(string search, CancellationToken cancellationToken)
    {
        // Refused: nothing in this module searches its rows by owner name.
        throw new NotSupportedException("Backups searches no account names.");
    }
}

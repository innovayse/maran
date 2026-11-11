using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Tests.TestSupport;

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

    /// <summary>Account ids <see cref="FindSystemUserAsync"/> was asked about, in order.</summary>
    public List<Guid> SystemUserLookups { get; } = [];

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
    public Task<AccountSnapshot?> FindForSystemAsync(Guid accountId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("These tests read no account unscoped.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Answers the configured account's id and username by id alone and models nothing about scope,
    /// exactly as the contract says the real member behaves for every caller.
    /// </remarks>
    public Task<AccountSystemUser?> FindSystemUserAsync(Guid accountId, CancellationToken cancellationToken)
    {
        SystemUserLookups.Add(accountId);
        AccountSystemUser? user = _snapshots.TryGetValue(accountId, out var snapshot)
            ? new AccountSystemUser(snapshot.Id, snapshot.Username)
            : null;
        return Task.FromResult(user);
    }

    /// <inheritdoc />
    public Task<AccountSnapshot?> FindForViewAsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        // Refused rather than answered: only a view-as start asks this question, and nothing here starts one.
        throw new NotSupportedException("Ssl tests start no view-as session.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AccountSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        // Unscoped, exactly as the contract says the real one is: every snapshot this double was
        // given, with no reference to any current user. A double that quietly filtered here would
        // make an unscoped method look scoped and hide the very exposure the interface documents.
        // Nothing in this module calls it; it is implemented because the interface declares it.
        return Task.FromResult<IReadOnlyList<AccountSnapshot>>([.. _snapshots.Values]);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<Guid, string>> NamesOfAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        // Refused rather than answered: nothing in this module labels rows with account names, and
        // the tenant scope the real implementation applies is the part a double cannot honestly model.
        throw new NotSupportedException("Ssl reads no account names.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> FindIdsByNameAsync(string search, CancellationToken cancellationToken)
    {
        // Refused for the same reason as NamesOfAsync: nothing in this module searches by owner.
        throw new NotSupportedException("Ssl searches no account names.");
    }
}

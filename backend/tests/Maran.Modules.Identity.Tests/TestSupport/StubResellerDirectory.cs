using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// An <see cref="IResellerDirectory"/> answering with exactly the resellers a test registered, so the
/// two questions Identity asks of the Accounts module — what is this reseller called and is it in
/// service — have a decidable answer with no Accounts module in the process.
/// </summary>
/// <remarks>
/// <see cref="FindAsync"/> answers <c>null</c> for anything unregistered, which is the answer the
/// real directory gives for a reseller that does not exist.
/// </remarks>
public sealed class StubResellerDirectory : IResellerDirectory
{
    /// <summary>The resellers this directory knows about, by id.</summary>
    private readonly Dictionary<Guid, ResellerSnapshot> _resellers = [];

    /// <summary>Registers a reseller.</summary>
    /// <param name="resellerId">The reseller's identity.</param>
    /// <param name="name">The reseller's name.</param>
    /// <param name="state">Whether the reseller is in service or suspended.</param>
    /// <returns>The snapshot that was registered, so a test can assert against the same values.</returns>
    public ResellerSnapshot Add(Guid resellerId, string name, ResellerState state = ResellerState.Active)
    {
        var snapshot = new ResellerSnapshot(resellerId, name, state);

        _resellers[resellerId] = snapshot;
        return snapshot;
    }

    /// <inheritdoc />
    public Task<ResellerSnapshot?> FindAsync(Guid resellerId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_resellers.TryGetValue(resellerId, out var snapshot) ? snapshot : null);
    }
}

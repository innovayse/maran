using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Constants;

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

    /// <summary>The accounts the signed-in reseller owns, as the real directory would list them.</summary>
    private readonly List<Guid> _ownedAccountIds = [];

    /// <summary>Registers an account the signed-in reseller owns.</summary>
    /// <param name="accountId">The account's identity.</param>
    public void AddOwnedAccount(Guid accountId)
    {
        _ownedAccountIds.Add(accountId);
    }

    /// <summary>Registers a reseller.</summary>
    /// <param name="resellerId">The reseller's identity.</param>
    /// <param name="name">The reseller's name.</param>
    /// <param name="state">Whether the reseller is in service or suspended.</param>
    /// <param name="contactEmail">The reseller's contact address.</param>
    /// <param name="language">The reseller contact's language.</param>
    /// <returns>The snapshot that was registered, so a test can assert against the same values.</returns>
    public ResellerSnapshot Add(
        Guid resellerId,
        string name,
        ResellerState state = ResellerState.Active,
        string contactEmail = "reseller@example.com",
        string language = PanelLanguages.English)
    {
        var snapshot = new ResellerSnapshot(resellerId, name, contactEmail, state, language);

        _resellers[resellerId] = snapshot;
        return snapshot;
    }

    /// <inheritdoc />
    public Task<ResellerSnapshot?> FindAsync(Guid resellerId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_resellers.TryGetValue(resellerId, out var snapshot) ? snapshot : null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> ListOwnedAccountIdsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<Guid>>(_ownedAccountIds.ToList());
    }
}

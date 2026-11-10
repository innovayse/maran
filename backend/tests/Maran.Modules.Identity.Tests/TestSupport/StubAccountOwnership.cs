using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// An <see cref="IAccountOwnership"/> answering from the (account, reseller) pairs a test registered,
/// and from nothing else: an unregistered pair is not owned, which is also the real window's answer for an
/// account that moved or never existed.
/// </summary>
public sealed class StubAccountOwnership : IAccountOwnership
{
    /// <summary>The account to its current owning reseller.</summary>
    private readonly Dictionary<Guid, Guid> _owners = [];

    /// <summary>Makes <paramref name="resellerId"/> the owner of the account.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="resellerId">Its owning reseller.</param>
    public void Own(Guid accountId, Guid resellerId)
    {
        _owners[accountId] = resellerId;
    }

    /// <summary>Hands the account to the administrator, so no reseller owns it.</summary>
    /// <param name="accountId">The account.</param>
    public void HandToAdministrator(Guid accountId)
    {
        _owners.Remove(accountId);
    }

    /// <inheritdoc />
    public Task<bool> IsOwnedByResellerAsync(Guid accountId, Guid resellerId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_owners.TryGetValue(accountId, out var owner) && owner == resellerId);
    }
}

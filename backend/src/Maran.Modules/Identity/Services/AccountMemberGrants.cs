using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Answers the Sdk's <see cref="IAccountMemberGrants"/> from this module's memberships: the one place
/// the rest of the panel learns whether a person may use a feature of an account.
/// </summary>
/// <remarks>
/// The rule itself lives on the entity (<c>AccountMembership.Allows</c>); this service only finds the
/// row. A person with no row at all is told no — the same fail-closed answer for a stranger and for a
/// member whose access was just removed while their access token was still live.
/// </remarks>
public sealed class AccountMemberGrants : IAccountMemberGrants
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the service.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public AccountMemberGrants(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<bool> PermitsAsync(
        Guid userId,
        Guid accountId,
        AccountFeature feature,
        CancellationToken cancellationToken)
    {
        var membership = await _dbContext.AccountMemberships
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.UserId == userId && candidate.AccountId == accountId,
                cancellationToken);

        return membership is not null && membership.Allows(feature);
    }
}

using System.Diagnostics;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// The Accounts module's implementation of <see cref="IResellerDirectory"/>: the one place another
/// module may learn a reseller's name, contact address and language, whether it may act, and which
/// accounts it owns, without referencing this module
/// (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// The tenant scope is applied HERE, in the query, by comparing against the id the caller's own token
/// carries and never against anything on the request: a reseller reads only itself, an administrator
/// reads any reseller, and every other caller reads none. A reseller asking for another's id and an id
/// that does not exist both answer <c>null</c>, so the answer cannot confirm that a tenant exists.
/// Like <see cref="AccountDirectory"/>, it does not lean on the context's own query filter alone: a
/// cross-module window is the natural gap in a tenancy story, so its predicate is written out.
/// </remarks>
public sealed class ResellerDirectory : IResellerDirectory
{
    /// <summary>The Accounts module's database context; this module owns the resellers table.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The authenticated principal, whose tenant scope bounds every answer.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the directory.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public ResellerDirectory(AccountsDbContext dbContext, ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<ResellerSnapshot?> FindAsync(Guid resellerId, CancellationToken cancellationToken)
    {
        if (!Sees(resellerId))
        {
            return null;
        }

        // An expression lambda, not the braced form used elsewhere in this repository: EF Core
        // translates it into SQL, and a statement-bodied lambda cannot become an expression tree.
        return await _dbContext.Resellers
            .AsNoTracking()
            .Where(reseller => reseller.Id == resellerId)
            .Select(reseller => new ResellerSnapshot(
                reseller.Id,
                reseller.Name,
                reseller.ContactEmail,
                reseller.Status == ResellerStatus.Suspended ? ResellerState.Suspended : ResellerState.Active,
                reseller.Language))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The reseller comes from the caller's own token and the predicate is written out beside the
    /// context's query filter, as <see cref="AccountDirectory.FindForViewAsAsync"/> does for the same
    /// reason: a window another module's rows are scoped by should not rest on one mechanism alone.
    /// </remarks>
    public async Task<IReadOnlyList<Guid>> ListOwnedAccountIdsAsync(CancellationToken cancellationToken)
    {
        if (_currentUser.Caller is not ResellerCaller reseller)
        {
            return [];
        }

        var resellerId = reseller.ResellerId;
        return await _dbContext.Accounts
            .AsNoTracking()
            .Where(account => account.ResellerId == resellerId)
            .Select(account => account.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Whether the caller may see the reseller <paramref name="resellerId"/>: an administrator any
    /// reseller, a reseller only the one its own token names, and nobody else (an anonymous caller, a
    /// customer, a customer login acting for no account) none.
    /// </summary>
    /// <param name="resellerId">The reseller asked about.</param>
    /// <returns>True when the caller's tenant scope includes the reseller.</returns>
    private bool Sees(Guid resellerId)
    {
        return _currentUser.Caller switch
        {
            AdministratorCaller => true,
            ResellerCaller reseller => reseller.ResellerId == resellerId,
            CustomerCaller => false,
            AccountlessCustomerCaller => false,
            ApiKeyCaller => false,
            AnonymousCaller => false,
            _ => throw new UnreachableException("Caller is sealed to SharedKernel: six cases exist."),
        };
    }
}

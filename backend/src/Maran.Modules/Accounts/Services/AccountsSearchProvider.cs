using Maran.Modules.Accounts.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// This module's answer to the global search: hosting accounts found by name, primary domain or owner
/// e-mail, for an administrator or a reseller.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope is the context's query filter, and this class adds none.</b> The accounts an administrator
/// sees are all of them, a reseller's are the ones it owns (<see cref="AccountsDbContext"/>), so a
/// reseller searching for another reseller's account by its exact name gets nothing, indistinguishable
/// from a name that does not exist. The query contains no <c>IgnoreQueryFilters</c>.
/// </para>
/// <para>
/// <b>It answers only for an administrator or a reseller</b>, the two roles the accounts screens
/// (<c>AccountsController</c>, policy <c>AdminOrReseller</c>) belong to. A customer's context would show
/// the customer their own account row, which is not something to offer as a link to a page they cannot
/// open. This is a relevance rule and never widens anything: the filter still decides what is visible.
/// </para>
/// <para>
/// <b>The match is a literal case-insensitive substring</b>: <c>ILIKE</c> with the term escaped by
/// <see cref="LikePattern"/>, so <c>%</c> and <c>_</c> in the term match only themselves. Candidates are
/// ordered exact-match first, capped at <see cref="SearchQuery.CandidateLimit"/>, then ranked in memory by
/// <see cref="SearchRank"/>. UNOBSERVED HERE by unit tests: the in-memory provider cannot evaluate
/// <c>ILIKE</c>, so the match and the tenant scoping through it are held by the integration suite.
/// </para>
/// <para>
/// A hit's subtitle is the primary domain and the owner e-mail, both fields the account's own page shows
/// to the same two roles.
/// </para>
/// </remarks>
public sealed class AccountsSearchProvider : ISearchProvider
{
    /// <summary>The separator between the primary domain and the owner e-mail in a hit's subtitle.</summary>
    private const string SubtitleSeparator = " · ";

    /// <summary>The module's context, carrying the caller's tenant scope.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The caller, read only to decide whether the accounts screens are theirs at all.</summary>
    private readonly ICurrentUser _currentUser;

    /// <inheritdoc />
    public SearchKind Kind
    {
        get { return SearchKind.Account; }
    }

    /// <summary>Creates the provider over this module's context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public AccountsSearchProvider(AccountsDbContext dbContext, ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, CancellationToken ct)
    {
        if (_currentUser.Caller is not (AdministratorCaller or ResellerCaller))
        {
            return [];
        }

        var term = query.Term;
        var pattern = LikePattern.Contains(term);
        var exact = LikePattern.Exactly(term);

        var candidates = await _dbContext.Accounts
            .AsNoTracking()
            .Where(account =>
                EF.Functions.ILike(account.Name, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(account.PrimaryDomain, pattern, LikePattern.EscapeCharacter)
                || (account.OwnerEmail != null
                    && EF.Functions.ILike(account.OwnerEmail, pattern, LikePattern.EscapeCharacter)))
            .OrderBy(account =>
                EF.Functions.ILike(account.Name, exact, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(account.PrimaryDomain, exact, LikePattern.EscapeCharacter)
                || (account.OwnerEmail != null
                    && EF.Functions.ILike(account.OwnerEmail, exact, LikePattern.EscapeCharacter))
                    ? 0
                    : 1)
            .ThenBy(account => account.Name)
            .Take(SearchQuery.CandidateLimit)
            .Select(account => new { account.Id, account.Name, account.PrimaryDomain, account.OwnerEmail })
            .ToListAsync(ct);

        return candidates
            .Select(account =>
            {
                return new
                {
                    Account = account,
                    Rank = SearchRank.Of(query.Term, account.Name, account.PrimaryDomain, account.OwnerEmail),
                };
            })
            .OrderBy(ranked => { return ranked.Rank; })
            .ThenBy(ranked => { return ranked.Account.Name; }, StringComparer.Ordinal)
            .Take(SearchQuery.HitLimit)
            .Select(ranked =>
            {
                var account = ranked.Account;
                var subtitle = string.IsNullOrEmpty(account.OwnerEmail)
                    ? account.PrimaryDomain
                    : account.PrimaryDomain + SubtitleSeparator + account.OwnerEmail;
                return new SearchHit(SearchKind.Account, account.Id, account.Name, subtitle);
            })
            .ToList();
    }
}

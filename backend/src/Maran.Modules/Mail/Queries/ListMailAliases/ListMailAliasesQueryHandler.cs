using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Resources;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Mail.Queries.ListMailAliases;

/// <summary>
/// Handles <see cref="ListMailAliasesQuery"/> by reading one page of <c>mail.MailAliases</c> within the
/// caller's tenant scope.
/// </summary>
/// <remarks>
/// <para>
/// The tenant boundary is not a <c>Where</c> clause on the caller: the context's global query filter
/// supplies it, so this handler could not leak another account's rows even if it were rewritten carelessly
/// (spec §8). The filters below only narrow that scope, and the filter applies before the count as well as
/// before the page, so a customer's total never counts another account's rows either.
/// </para>
/// <para>
/// The agent is not asked: the rows are the panel's record of what each owner set up, and an address
/// the host holds that the panel does not is not this list's to show.
/// </para>
/// </remarks>
public sealed class ListMailAliasesQueryHandler
{
    /// <summary>The Mail module's database context, and this module's tenant boundary.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>The window onto the Accounts module's account names, tenant-scoped.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>The module's localized names, for each row's kind.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="accounts">The window onto the Accounts module's account names, tenant-scoped.</param>
    /// <param name="displayNames">The module's localized names.</param>
    public ListMailAliasesQueryHandler(
        MailDbContext dbContext,
        IAccountDirectory accounts,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _displayNames = displayNames;
    }

    /// <summary>Narrows, searches and pages the addresses, with each row's owner name and kind name.</summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<MailAliasDto>>> HandleAsync(
        ListMailAliasesQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<MailAlias> aliases = _dbContext.MailAliases
            .AsNoTracking()
            .Include(alias => alias.MailDomain)
            .Include(alias => alias.Targets);

        if (query.AccountId is { } accountId)
        {
            aliases = aliases.Where(alias => alias.AccountId == accountId);
        }

        if (!query.Domain.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter. Domain names are stored lower-case.
            var domainName = query.Domain.Value.ToLowerInvariant();
            aliases = aliases.Where(alias => alias.MailDomain.Name == domainName);
        }

        if (query.Kind is { } kind)
        {
            aliases = aliases.Where(alias => alias.Kind == kind);
        }

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the wildcards so the
            // term matches literally. One search answers for the local part, the domain or any destination.
            var pattern = LikePattern.Contains(query.Search.Value);

            // The aliases with a matching destination are found by their own query and joined in by id: a
            // statement-bodied lambda cannot become an expression tree, so the nested `Targets.Any(...)`
            // would have to be an expression lambda, which the repository's lambda rule refuses.
            var viaTarget = _dbContext.MailAliasTargets
                .Where(target => EF.Functions.ILike(target.Address, pattern, LikePattern.EscapeCharacter))
                .Select(target => target.MailAliasId);
            aliases = aliases.Where(alias =>
                EF.Functions.ILike(alias.LocalPart, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(alias.MailDomain.Name, pattern, LikePattern.EscapeCharacter)
                || viaTarget.Contains(alias.Id));
        }

        var page = await aliases.ToPagedAsync(query, ListMailAliasesSorts.Map, cancellationToken);

        // One batched, tenant-scoped lookup for every owner on the page, never one per row.
        var ownerIds = page.Items
            .Select(row =>
            {
                return row.AccountId;
            })
            .Distinct()
            .ToList();
        var ownerNames = await _accounts.NamesOfAsync(ownerIds, cancellationToken);

        return Result<PagedResult<MailAliasDto>>.Ok(page.Map(row =>
        {
            return MailAliasMapper.From(row, row.MailDomain.Name, ownerNames, _displayNames);
        }));
    }
}

using Maran.Modules.Firewall.Common;
using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Persistence;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Firewall.Queries.ListWhitelist;

/// <summary>Handles <see cref="ListWhitelistQuery"/> by reading one page of <c>firewall.WhitelistEntries</c>.</summary>
/// <remarks>
/// The search runs over the range and the note together: an operator looks for an exemption either by
/// the address they remember or by what they called it.
/// </remarks>
public sealed class ListWhitelistQueryHandler
{
    /// <summary>The Firewall module's database context.</summary>
    private readonly FirewallDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Firewall module's database context.</param>
    public ListWhitelistQueryHandler(FirewallDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Searches the ranges and their notes, and answers the requested page with the total that matched.
    /// </summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<WhitelistEntryDto>>> HandleAsync(
        ListWhitelistQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<WhitelistEntry> entries = _dbContext.WhitelistEntries.AsNoTracking();

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the
            // wildcards so the term matches literally.
            var pattern = LikePattern.Contains(query.Search.Value);
            entries = entries.Where(entry =>
                EF.Functions.ILike(entry.Cidr, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(entry.Note, pattern, LikePattern.EscapeCharacter));
        }

        var page = await entries.ToPagedAsync(query, ListWhitelistSorts.Map, cancellationToken);

        return Result<PagedResult<WhitelistEntryDto>>.Ok(page.Map(entry =>
        {
            return new WhitelistEntryDto(entry.Id, entry.Cidr, entry.Note, entry.CreatedAt);
        }));
    }
}

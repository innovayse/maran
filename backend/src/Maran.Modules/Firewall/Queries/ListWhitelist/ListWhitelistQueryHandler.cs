using Maran.Modules.Firewall.Common;
using Maran.Modules.Firewall.Persistence;

namespace Maran.Modules.Firewall.Queries.ListWhitelist;

/// <summary>Handles <see cref="ListWhitelistQuery"/> by reading <c>firewall.WhitelistEntries</c>.</summary>
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

    /// <summary>Returns the exempt ranges, oldest first.</summary>
    /// <param name="query">The (parameterless) list request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the ranges; this operation never fails.</returns>
    public async Task<Result<IReadOnlyList<WhitelistEntryDto>>> HandleAsync(
        ListWhitelistQuery query,
        CancellationToken cancellationToken)
    {
        var entries = await _dbContext.WhitelistEntries
            .AsNoTracking()
            .OrderBy(entry => entry.CreatedAt)
            .Select(entry => new WhitelistEntryDto(entry.Id, entry.Cidr, entry.Note, entry.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<WhitelistEntryDto>>.Ok(entries);
    }
}

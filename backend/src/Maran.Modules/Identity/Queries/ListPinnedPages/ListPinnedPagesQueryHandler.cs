using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Persistence;

namespace Maran.Modules.Identity.Queries.ListPinnedPages;

/// <summary>Handles <see cref="ListPinnedPagesQuery"/> by reading the caller's pins.</summary>
public sealed class ListPinnedPagesQueryHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public ListPinnedPagesQueryHandler(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Returns the caller's pins, lowest position first.</summary>
    /// <param name="query">Whose pins to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The pins and the limit; an empty list for a user who has pinned nothing.</returns>
    public async Task<Result<PinnedPagesDto>> HandleAsync(
        ListPinnedPagesQuery query,
        CancellationToken cancellationToken)
    {
        var pins = await _dbContext.PinnedPages
            .AsNoTracking()
            .Where(pin => pin.UserId == query.UserId)
            .ToListAsync(cancellationToken);

        return Result<PinnedPagesDto>.Ok(PinnedPagesMapper.From(pins));
    }
}

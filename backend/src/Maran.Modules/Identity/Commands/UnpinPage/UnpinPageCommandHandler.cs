using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Commands.UnpinPage;

/// <summary>Handles <see cref="UnpinPageCommand"/> by removing a page from the caller's own pins.</summary>
public sealed class UnpinPageCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Records the removal.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="journal">Records the removal.</param>
    public UnpinPageCommandHandler(IdentityDbContext dbContext, IdentityAuditJournal journal)
    {
        _dbContext = dbContext;
        _journal = journal;
    }

    /// <summary>Removes the page from the caller's pins.</summary>
    /// <param name="command">Which page, and who is asking.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The caller's pins after the change, or <c>PinnedPageNotFound</c> when the caller has not
    /// pinned that page. The lookup is by the caller's own id, so another user's pin of the same
    /// page is indistinguishable from none: it answers "not found" and is left alone.
    /// </returns>
    public async Task<Result<PinnedPagesDto>> HandleAsync(
        UnpinPageCommand command,
        CancellationToken cancellationToken)
    {
        var pins = await _dbContext.PinnedPages
            .Where(pin => pin.UserId == command.UserId)
            .ToListAsync(cancellationToken);

        var pin = pins.SingleOrDefault(candidate =>
        {
            return candidate.ItemKey == command.ItemKey;
        });
        if (pin is null)
        {
            return Result<PinnedPagesDto>.Fail(
                Error.Of(nameof(ErrorMessages.PinnedPageNotFound), ErrorType.NotFound));
        }

        _dbContext.PinnedPages.Remove(pin);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordIdentifiedAsync(
            command.UserId,
            AuditActions.PageUnpinned,
            command.ItemKey,
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result<PinnedPagesDto>.Ok(PinnedPagesMapper.From(pins.Where(candidate =>
        {
            return candidate != pin;
        })));
    }
}

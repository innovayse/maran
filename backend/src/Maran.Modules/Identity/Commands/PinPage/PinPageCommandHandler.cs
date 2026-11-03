using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Npgsql;

namespace Maran.Modules.Identity.Commands.PinPage;

/// <summary>Handles <see cref="PinPageCommand"/> by adding a page to the caller's own pins.</summary>
public sealed class PinPageCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The panel's clock, stamping when the page was pinned.</summary>
    private readonly IClock _clock;

    /// <summary>Records the pin.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">Records the pin.</param>
    public PinPageCommandHandler(IdentityDbContext dbContext, IClock clock, IdentityAuditJournal journal)
    {
        _dbContext = dbContext;
        _clock = clock;
        _journal = journal;
    }

    /// <summary>Pins the page for the caller, unless it is already pinned or the caller is at the limit.</summary>
    /// <param name="command">Which page, and who is asking.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The caller's pins after the change, or <c>PinnedPageAlreadyPinned</c> / <c>PinnedPageLimitReached</c>.
    /// Only the caller's own rows are ever read or written, so there is no other user's pin to
    /// answer 404 for.
    /// </returns>
    public async Task<Result<PinnedPagesDto>> HandleAsync(PinPageCommand command, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.PinnedPages
            .Where(pin => pin.UserId == command.UserId)
            .ToListAsync(cancellationToken);

        var admission = PinnedPagePolicy.Admit(existing, command.ItemKey);
        if (admission != PinAdmission.Admitted)
        {
            return Result<PinnedPagesDto>.Fail(RefusalFor(admission));
        }

        var pin = new PinnedPage(
            Guid.NewGuid(),
            command.UserId,
            command.ItemKey,
            PinnedPagePolicy.NextPosition(existing),
            _clock.UtcNow);
        _dbContext.PinnedPages.Add(pin);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent request pinned the same page between the read above and this insert; the
            // unique index (UserId, ItemKey) is what decided it, and the page IS pinned.
            return Result<PinnedPagesDto>.Fail(RefusalFor(PinAdmission.AlreadyPinned));
        }

        await _journal.RecordIdentifiedAsync(
            command.UserId,
            IdentityAuditActions.PagePinned,
            command.ItemKey,
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result<PinnedPagesDto>.Ok(PinnedPagesMapper.From(existing.Append(pin)));
    }

    /// <summary>Names the error a refused admission answers with.</summary>
    /// <param name="admission">The refusal; never <see cref="PinAdmission.Admitted"/>.</param>
    /// <returns>The conflict to return.</returns>
    private static Error RefusalFor(PinAdmission admission)
    {
        return admission == PinAdmission.AlreadyPinned
            ? Error.Of(nameof(ErrorMessages.PinnedPageAlreadyPinned), ErrorType.Conflict)
            : Error.Of(nameof(ErrorMessages.PinnedPageLimitReached), ErrorType.Conflict);
    }
}

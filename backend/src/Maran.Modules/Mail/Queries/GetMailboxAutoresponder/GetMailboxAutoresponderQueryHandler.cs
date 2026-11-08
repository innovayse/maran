using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Resources;

namespace Maran.Modules.Mail.Queries.GetMailboxAutoresponder;

/// <summary>
/// Handles <see cref="GetMailboxAutoresponderQuery"/> by reading the mailbox, then its reply, within the
/// caller's tenant scope.
/// </summary>
/// <remarks>
/// The mailbox is asked for first and the two refusals are different on purpose: a mailbox the caller may
/// not see (another account's, or none) answers <c>MailboxNotFound</c>, and a mailbox of their own with no
/// reply answers <c>MailAutoresponderNotFound</c>. The difference tells the caller nothing about anyone
/// else, because the first covers every mailbox that is not theirs.
/// </remarks>
public sealed class GetMailboxAutoresponderQueryHandler
{
    /// <summary>The Mail module's database context, and this module's tenant boundary.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    public GetMailboxAutoresponderQueryHandler(MailDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Reads the reply.</summary>
    /// <param name="query">Which mailbox.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The reply — or <c>MailboxNotFound</c> or <c>MailAutoresponderNotFound</c>.</returns>
    public async Task<Result<MailAutoresponderDto>> HandleAsync(
        GetMailboxAutoresponderQuery query,
        CancellationToken cancellationToken)
    {
        var mailboxExists = await _dbContext.Mailboxes
            .AsNoTracking()
            .AnyAsync(mailbox => mailbox.Id == query.MailboxId, cancellationToken);
        if (!mailboxExists)
        {
            return Result<MailAutoresponderDto>.Fail(
                Error.Of(nameof(ErrorMessages.MailboxNotFound), ErrorType.NotFound));
        }

        var responder = await _dbContext.MailAutoresponders
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.MailboxId == query.MailboxId, cancellationToken);
        if (responder is null)
        {
            return Result<MailAutoresponderDto>.Fail(
                Error.Of(nameof(ErrorMessages.MailAutoresponderNotFound), ErrorType.NotFound));
        }

        return Result<MailAutoresponderDto>.Ok(MailAutoresponderMapper.From(responder));
    }
}

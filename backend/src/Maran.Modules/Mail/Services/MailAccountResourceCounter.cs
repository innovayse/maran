using Maran.Modules.Mail.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Mail.Services;

/// <summary>
/// This module's answer to <see cref="IAccountResourceCounter"/>: how many mailboxes an account owns,
/// counted from this module's own table.
/// </summary>
/// <remarks>
/// The count runs through <see cref="MailDbContext"/>'s tenant query filter, so a customer's scope can only
/// ever count its own rows. No filter is bypassed. The table is the truth here, so the answer is always
/// <see cref="AccountResourceCountState.Counted"/>.
/// </remarks>
public sealed class MailAccountResourceCounter : IAccountResourceCounter
{
    /// <summary>The module's context, carrying the caller's tenant scope.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>Creates the counter over this module's context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public MailAccountResourceCounter(MailDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public AccountResourceKind Kind
    {
        get
        {
            return AccountResourceKind.Mailboxes;
        }
    }

    /// <inheritdoc />
    public async Task<AccountResourceCount> CountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var used = await _dbContext.Mailboxes
            .AsNoTracking()
            .CountAsync(row => row.AccountId == accountId, cancellationToken);

        return AccountResourceCount.Counted(AccountResourceKind.Mailboxes, used);
    }
}

using Maran.Modules.Notifications.Common;
using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Persistence;

namespace Maran.Modules.Notifications.Queries.GetAlertRecipients;

/// <summary>Handles <see cref="GetAlertRecipientsQuery"/> by reading the list in its stored order.</summary>
public sealed class GetAlertRecipientsQueryHandler
{
    /// <summary>The module's database context, which owns the recipients.</summary>
    private readonly NotificationsDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public GetAlertRecipientsQueryHandler(NotificationsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Returns the alert recipients.</summary>
    /// <param name="query">The (parameterless) read request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The addresses in list order, with the cap the list may reach; empty on a fresh panel, which is not an error.</returns>
    public async Task<Result<AlertRecipientsDto>> HandleAsync(
        GetAlertRecipientsQuery query,
        CancellationToken cancellationToken)
    {
        var addresses = await _dbContext.AlertRecipients
            .AsNoTracking()
            .OrderBy(recipient => recipient.Position)
            .Select(recipient => recipient.Address)
            .ToListAsync(cancellationToken);

        return Result<AlertRecipientsDto>.Ok(new AlertRecipientsDto(addresses, AlertRecipient.MaxCount));
    }
}

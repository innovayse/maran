using System.Globalization;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Npgsql;

namespace Maran.Modules.Identity.Commands.SaveDashboardLayout;

/// <summary>Handles <see cref="SaveDashboardLayoutCommand"/> by replacing the caller's stored layout.</summary>
public sealed class SaveDashboardLayoutCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Records the save.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="journal">Records the save.</param>
    public SaveDashboardLayoutCommandHandler(IdentityDbContext dbContext, IdentityAuditJournal journal)
    {
        _dbContext = dbContext;
        _journal = journal;
    }

    /// <summary>Stores both lists as the caller's layout, forgetting any entry that is not sent again.</summary>
    /// <param name="command">The lists, and who is asking.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// Success, or <c>DashboardLayoutSaveConflict</c> when a save by the same person landed between
    /// the read and the write. Only the caller's own rows are ever read or written, so there is no
    /// other person's layout to answer 404 for.
    /// </returns>
    public async Task<Result> HandleAsync(SaveDashboardLayoutCommand command, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.DashboardPreferences
            .Where(preference => preference.UserId == command.UserId)
            .ToListAsync(cancellationToken);

        Replace(existing, command.UserId, DashboardPreferenceKind.Widget, command.Widgets);
        Replace(existing, command.UserId, DashboardPreferenceKind.Action, command.Actions);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent save by the same person inserted one of these keys between the read above
            // and this write; the unique index (UserId, Kind, Key) decided it. Nothing was stored by
            // this request, and sending the layout again is what resolves it.
            return Result.Fail(Error.Of(nameof(ErrorMessages.DashboardLayoutSaveConflict), ErrorType.Conflict));
        }

        await _journal.RecordIdentifiedAsync(
            command.UserId,
            IdentityAuditActions.DashboardLayoutSaved,
            string.Create(
                CultureInfo.InvariantCulture,
                $"widgets={command.Widgets.Count} actions={command.Actions.Count}"),
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result.Ok();
    }

    /// <summary>
    /// Makes the stored entries of one list equal the list sent: an entry already stored is moved in
    /// place, a new one is added, and a stored one that was not sent is removed.
    /// </summary>
    /// <param name="existing">Every entry the caller has stored, of both kinds.</param>
    /// <param name="userId">The caller.</param>
    /// <param name="kind">Which list is being replaced.</param>
    /// <param name="sent">The list the caller sent, in drawing order.</param>
    private void Replace(
        List<DashboardPreference> existing,
        Guid userId,
        DashboardPreferenceKind kind,
        IReadOnlyList<DashboardLayoutEntryDto> sent)
    {
        var stored = existing
            .Where(preference =>
            {
                return preference.Kind == kind;
            })
            .ToDictionary(preference =>
            {
                return preference.Key;
            }, StringComparer.Ordinal);

        for (var position = 0; position < sent.Count; position++)
        {
            var entry = sent[position];
            if (stored.Remove(entry.Key, out var preference))
            {
                preference.Place(position, entry.Hidden);
            }
            else
            {
                _dbContext.DashboardPreferences.Add(
                    new DashboardPreference(Guid.NewGuid(), userId, kind, entry.Key, position, entry.Hidden));
            }
        }

        // What is left was stored and not sent again: the new layout replaces it.
        _dbContext.DashboardPreferences.RemoveRange(stored.Values);
    }
}

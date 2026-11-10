using Maran.Modules.Marketplace.Domain.Entities;
using Maran.Modules.Marketplace.Persistence;
using Npgsql;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// Reads and changes the marketplace switch: the one row that says whether this installation may talk to
/// the marketplace at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Off until an administrator says otherwise.</b> With no row, every read answers off; the first
/// <see cref="EnableAsync"/> creates it. There is no configuration key and no environment variable that
/// could turn it on, so a typo in <c>panel.env</c> cannot make the panel contact a second destination
/// (rules/security.md item 10).
/// </para>
/// <para>
/// <b>It does not audit.</b> Recording the change, and the password check that precedes it, belong to the
/// command that calls it, through <see cref="MarketplaceAuditJournal"/>; the store only keeps the fact.
/// </para>
/// </remarks>
public sealed class MarketplaceSettingsStore
{
    /// <summary>The module's own database context.</summary>
    private readonly MarketplaceDbContext _context;

    /// <summary>The time source.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the store.</summary>
    /// <param name="context">The module's database context.</param>
    /// <param name="clock">The time source.</param>
    public MarketplaceSettingsStore(MarketplaceDbContext context, IClock clock)
    {
        _context = context;
        _clock = clock;
    }

    /// <summary>Reads the switch.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The stored settings, or an unsaved off state when the switch was never touched.</returns>
    public async Task<MarketplaceSettings> ReadAsync(CancellationToken cancellationToken)
    {
        var stored = await _context.MarketplaceSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(settings => settings.Id == MarketplaceSettings.SingletonId, cancellationToken);
        return stored ?? MarketplaceSettings.Off();
    }

    /// <summary>Tells whether the marketplace is switched on.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>True only when an administrator enabled it and has not disabled it since.</returns>
    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken)
    {
        return (await ReadAsync(cancellationToken)).Enabled;
    }

    /// <summary>Switches the marketplace on.</summary>
    /// <param name="by">The administrator who did it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Resolves once the row is saved.</returns>
    public async Task EnableAsync(Guid by, CancellationToken cancellationToken)
    {
        await ChangeAsync(enabled: true, by, cancellationToken);
    }

    /// <summary>Switches the marketplace off. Installed modules keep running; only the outbound path closes.</summary>
    /// <param name="by">The administrator who did it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Resolves once the row is saved.</returns>
    public async Task DisableAsync(Guid by, CancellationToken cancellationToken)
    {
        await ChangeAsync(enabled: false, by, cancellationToken);
    }

    /// <summary>
    /// Writes the new state, creating the row on the first change. Two administrators switching a server
    /// that has no row yet race to create it; the loser retries once as an update of the winner's row.
    /// </summary>
    /// <param name="enabled">The state to record.</param>
    /// <param name="by">The administrator who asked.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Resolves once the row is saved.</returns>
    private async Task ChangeAsync(bool enabled, Guid by, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var settings = await _context.MarketplaceSettings
                .SingleOrDefaultAsync(row => row.Id == MarketplaceSettings.SingletonId, cancellationToken);
            if (settings is null)
            {
                settings = MarketplaceSettings.Off();
                _context.MarketplaceSettings.Add(settings);
            }

            if (enabled)
            {
                settings.Enable(_clock.UtcNow, by);
            }
            else
            {
                settings.Disable(_clock.UtcNow, by);
            }

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException exception) when (attempt == 0 && IsUniqueViolation(exception))
            {
                // The other administrator's insert won. Forget our pending row and apply the change to theirs.
                _context.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>Tells whether a failed save was the primary key refusing a second row.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns>True for SQLSTATE 23505.</returns>
    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }
}

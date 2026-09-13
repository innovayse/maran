using Maran.Modules.Firewall.Domain.Policies;
using Maran.Modules.Firewall.Persistence;
using Maran.Modules.Firewall.Resources;
using Maran.Modules.Firewall.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Firewall.Commands.RemoveWhitelistEntry;

/// <summary>
/// Handles <see cref="RemoveWhitelistEntryCommand"/>: drops one exemption and journals which range
/// it was, unless dropping it would leave the caller unexempt in the same breath.
/// </summary>
/// <remarks>
/// <para>
/// The journal records the RANGE and not the row identifier, because after the row is gone the
/// identifier means nothing to anybody. "Who stopped exempting the office, and when" is the question
/// this entry has to answer for an administrator who has just been banned.
/// </para>
/// <para>
/// <b>Why one removal is refused.</b> The installer seeds the whitelist with the address the install
/// was run from (<see cref="Seeders.WhitelistSeeder"/>), and that row is an ordinary
/// <see cref="Domain.Entities.WhitelistEntry"/> — nothing marked it, and this handler used to remove
/// it like any other. An administrator tidying the list from the very address it exempts therefore
/// revoked their own protection with one DELETE, and could not get it back automatically:
/// <see cref="Domain.Entities.WhitelistSeedRecord"/> blocks RE-seeding for its own good reasons, so
/// the whitelist stayed empty and the next mistyped password was banned on schedule.
/// </para>
/// <para>
/// <b>What is protected is not the seeded row and not "some row".</b> Marking the seeded row
/// permanent would pin whatever address the installer happened to arrive on — routinely a café
/// network or a jump host — forever, which is the exemption
/// <see cref="Domain.Entities.WhitelistSeedRecord"/> exists to let an operator revoke. Refusing the
/// LAST row protects a property nobody wants: one stale range belonging to a stranger satisfies it
/// while exempting nobody who is actually here, and it stops nothing when there are two rows and the
/// administrator deletes their own. The property worth holding is narrower and is about the person
/// pressing the button: <b>this request must not be the thing that stops exempting the address it
/// arrived from.</b> Everything else — a range covering somebody else, a range covering an address
/// the caller is not on, a range whose cover another row duplicates — is removed exactly as before.
/// </para>
/// <para>
/// So the refusal is stated against the whitelist as it WOULD BE: the caller is exempt now and would
/// not be afterwards. That makes the way out mechanical rather than a matter of persuading the
/// panel — add a range that also covers where you are, and this row stops being the only thing
/// holding you up — and it is what the error message tells the operator to do. A refusal an operator
/// cannot undo through the panel is how somebody ends up deleting the row in psql, and then no rule
/// here means anything.
/// </para>
/// <para>
/// <b>It fails open, deliberately.</b> An address the module cannot parse — a console session, a
/// caller the panel could not attribute — is treated as covered by nothing, so the removal proceeds.
/// This is a lockout guard and not an authorization gate (the gate is the administrator role on
/// <see cref="Controllers.FirewallWhitelistController"/>): refusing on an address nobody can
/// evaluate would block a legitimate edit to protect a session that does not exist.
/// </para>
/// </remarks>
public sealed class RemoveWhitelistEntryCommandHandler
{
    /// <summary>The Firewall module's database context.</summary>
    private readonly FirewallDbContext _dbContext;

    /// <summary>The one place this module asks whether an address is exempt.</summary>
    private readonly WhitelistGuard _guard;

    /// <summary>This module's audit journal.</summary>
    private readonly FirewallAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Firewall module's database context.</param>
    /// <param name="guard">The one place this module asks whether an address is exempt.</param>
    /// <param name="journal">This module's audit journal.</param>
    public RemoveWhitelistEntryCommandHandler(
        FirewallDbContext dbContext,
        WhitelistGuard guard,
        FirewallAuditJournal journal)
    {
        _dbContext = dbContext;
        _guard = guard;
        _journal = journal;
    }

    /// <summary>Removes the exemption.</summary>
    /// <param name="command">Which row to remove.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// Success; <c>WhitelistEntryNotFound</c>; or <c>WhitelistEntryProtectsCaller</c> when the
    /// removal would stop exempting the address the request arrived from.
    /// </returns>
    public async Task<Result<bool>> HandleAsync(
        RemoveWhitelistEntryCommand command,
        CancellationToken cancellationToken)
    {
        var entry = await _dbContext.WhitelistEntries
            .SingleOrDefaultAsync(row => row.Id == command.EntryId, cancellationToken);
        if (entry is null)
        {
            await _journal.RecordFailureAsync(
                AuditActions.FirewallWhitelistChanged,
                command.EntryId.ToString(),
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.WhitelistEntryNotFound), ErrorType.NotFound));
        }

        // Captured before the row goes: the range is the only thing about a removed exemption
        // anybody will later be able to search for.
        var cidr = entry.Cidr;

        if (await WouldStrandTheCallerAsync(command, cancellationToken))
        {
            await _journal.RecordFailureAsync(
                AuditActions.FirewallWhitelistChanged,
                cidr,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<bool>.Fail(
                Error.Of(nameof(ErrorMessages.WhitelistEntryProtectsCaller), ErrorType.Conflict));
        }

        _dbContext.WhitelistEntries.Remove(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.FirewallWhitelistChanged,
            cidr,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <summary>Whether this removal is the thing that would stop exempting its own caller.</summary>
    /// <param name="command">The removal, carrying the address the request arrived from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>True only when the caller is exempt now and would not be once the row is gone.</returns>
    /// <remarks>
    /// <para>
    /// Both halves are asked through <see cref="WhitelistGuard.Exempts"/>, over one snapshot, so the
    /// question "am I exempt" is answered here by the same matching rule the brute-force detector
    /// uses. Re-implementing containment for this one check is how a guard comes to protect a
    /// slightly different set of addresses from the one that is actually banned.
    /// </para>
    /// <para>
    /// <b>"One snapshot" means one request, not one moment across requests.</b> The list is read
    /// once here and both questions are asked of that in-memory copy, so nothing shifts between the
    /// two halves — but there is no transaction and no concurrency token on the row, so two
    /// simultaneous removals of two DIFFERENT rows that each cover the caller can both conclude the
    /// other still protects them, and both succeed. It needs an administrator deleting two covering
    /// rows at once, and it strands only themselves; the recovery in this refusal's own message
    /// still applies afterwards. Stated rather than fixed because a concurrency token here would be
    /// the module's only one, and the accident this guard exists for is the single careless click.
    /// </para>
    /// <para>
    /// The address is normalised the way every other address entering this module is: a caller
    /// arriving as <c>::ffff:203.0.113.7</c> is the same operator as one arriving as
    /// <c>203.0.113.7</c>, and an exemption written in the IPv4 spelling covers neither of them if
    /// the comparison is made against the mapped form.
    /// </para>
    /// </remarks>
    private async Task<bool> WouldStrandTheCallerAsync(
        RemoveWhitelistEntryCommand command,
        CancellationToken cancellationToken)
    {
        if (!IpAddressNormalizer.TryNormalize(command.IpAddress, out var caller))
        {
            return false;
        }

        var whitelist = await _guard.SnapshotAsync(cancellationToken);
        var remaining = whitelist.Where(row =>
        {
            return row.Id != command.EntryId;
        }).ToList();

        return WhitelistGuard.Exempts(whitelist, caller) && !WhitelistGuard.Exempts(remaining, caller);
    }
}

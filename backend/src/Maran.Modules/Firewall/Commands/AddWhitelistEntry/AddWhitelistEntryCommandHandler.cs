using Maran.Modules.Firewall.Common;
using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Domain.ValueObjects;
using Maran.Modules.Firewall.Persistence;
using Maran.Modules.Firewall.Resources;
using Maran.Modules.Firewall.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Firewall.Commands.AddWhitelistEntry;

/// <summary>
/// Handles <see cref="AddWhitelistEntryCommand"/>: records a range the automatic bans skip.
/// </summary>
/// <remarks>
/// The agent is not called and must not be. A whitelist row is an exemption from the PANEL's
/// brute-force banning, not a hole in the host's packet filter: the range is not allowed through
/// anything it was not already allowed through, and an administrator can still ban it deliberately.
/// Sending it to the agent would turn a "do not ban this by accident" list into an "always let this
/// in" list, which is a different and far larger promise.
/// </remarks>
public sealed class AddWhitelistEntryCommandHandler
{
    /// <summary>The Firewall module's database context.</summary>
    private readonly FirewallDbContext _dbContext;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>This module's audit journal.</summary>
    private readonly FirewallAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Firewall module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">This module's audit journal.</param>
    public AddWhitelistEntryCommandHandler(
        FirewallDbContext dbContext,
        IClock clock,
        FirewallAuditJournal journal)
    {
        _dbContext = dbContext;
        _clock = clock;
        _journal = journal;
    }

    /// <summary>Adds the range.</summary>
    /// <param name="command">The range and the note to record.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored row, or <c>WhitelistCidrTaken</c> when the range is already exempt.</returns>
    public async Task<Result<WhitelistEntryDto>> HandleAsync(
        AddWhitelistEntryCommand command,
        CancellationToken cancellationToken)
    {
        // The stored spelling, decided before anything is compared against it. Two spellings of one
        // range — 203.0.113.0/24 and 203.0.113.0/024 — passed this duplicate check as different
        // values and then collided on the column's unique index, which is a 500 for what is really
        // "you already have that range".
        var cidr = CidrRange.Canonical(command.Cidr);

        var taken = await _dbContext.WhitelistEntries
            .AnyAsync(entry => entry.Cidr == cidr, cancellationToken);
        if (taken)
        {
            await _journal.RecordFailureAsync(
                AuditActions.FirewallWhitelistChanged,
                cidr,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<WhitelistEntryDto>.Fail(Error.Of(nameof(ErrorMessages.WhitelistCidrTaken), ErrorType.Conflict));
        }

        var entry = new WhitelistEntry(Guid.NewGuid(), cidr, command.Note, _clock.UtcNow);
        _dbContext.WhitelistEntries.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AuditActions.FirewallWhitelistChanged,
            entry.Cidr,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<WhitelistEntryDto>.Ok(
            new WhitelistEntryDto(entry.Id, entry.Cidr, entry.Note, entry.CreatedAt));
    }
}

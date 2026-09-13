using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Domain.Policies;
using Maran.Modules.Firewall.Options;
using Maran.Modules.Firewall.Persistence;
using Maran.Modules.Firewall.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Firewall.Seeders;

/// <summary>
/// Puts the address the installer was run from onto the whitelist, once for the life of the server.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a day-one server needs this.</b> The brute-force detector cannot tell an administrator
/// mistyping their password from an attack, and an empty whitelist on the first day is a server
/// whose only administrator can lock themselves out of it — from a typo, with no remote way back in
/// beyond the console the installer was probably not run from. The installer therefore records the
/// address it saw the operator arrive on (<c>Firewall__SeedWhitelistCidr</c>), and this is the code
/// that honours it.
/// </para>
/// <para>
/// <b>Read once, and the record of having read it is a row of its own.</b> That is the promise
/// <c>panel.env</c> makes to an operator in as many words — "editing it afterwards changes nothing,
/// because the whitelist is panel data from then on". "While the whitelist is empty" was not that
/// promise and could not become it: an administrator who deletes the seeded row empties the
/// whitelist, so the next restart restored an exemption somebody had deliberately revoked, into an
/// append-only journal that showed the revocation and not the restoration.
/// <see cref="WhitelistSeedRecord"/> is what the gate reads instead, and an administrator cannot
/// delete it through the panel because nothing in the surface writes that table.
/// </para>
/// <para>
/// <b>The seed is translated, not refused, when it arrives in the IPv4-mapped spelling.</b> This is
/// the one boundary in the module with no human on the other side of it: a dual-stack sshd reports
/// <c>SSH_CLIENT=::ffff:203.0.113.7</c>, the installer records that, and a refusal here produces an
/// empty whitelist hours after the install transcript told the operator it had been seeded.
/// <see cref="CidrRangeNormalizer"/> carries that difference and explains it; a range typed into the
/// panel is still refused, because there a 400 reaches somebody who can retype it.
/// </para>
/// <para>
/// A value that is not a range in any spelling is logged and skipped rather than stored. A row that
/// matches no packet that ever arrives would tell its reader they were exempt while they were not.
/// It is deliberately NOT marked as seeded: the warning is worth repeating on every boot until an
/// operator fixes the value or adds a row by hand, because nothing else says the server is
/// unprotected.
/// </para>
/// </remarks>
public sealed class WhitelistSeeder
{
    /// <summary>The note recorded against the seeded row, so a later reader knows where it came from.</summary>
    public const string SeedNote = "Seeded from the address this server was installed from";

    /// <summary>
    /// The seed record's fixed identity, so the marker is one row and re-running the seeder cannot
    /// make it two.
    /// </summary>
    public static readonly Guid SeedRecordId = Guid.Parse("22222222-0000-4000-8000-000000000001");

    /// <summary>Pre-compiled log delegate for a seed that was written.</summary>
    private static readonly Action<ILogger, string, Exception?> LogSeeded =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, nameof(WhitelistSeeder)),
            "Seeded the firewall whitelist with {Cidr}, the address this server was installed from");

    /// <summary>Pre-compiled log delegate for a configured seed this panel cannot store.</summary>
    /// <remarks>
    /// It says what is true — that the value cannot be USED as a range — rather than that it is not
    /// one. The wording used to assert that a parseable range was "not an address range", which sent
    /// an operator looking for a typo in a value that had none.
    /// </remarks>
    private static readonly Action<ILogger, string, Exception?> LogUnusableSeed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, nameof(WhitelistSeeder)),
            "Firewall__SeedWhitelistCidr is '{Cidr}', which this panel cannot use as an address "
            + "range, so the firewall whitelist is still empty and nothing exempts this server's "
            + "administrator from an automatic ban");

    /// <summary>The Firewall module's database context.</summary>
    private readonly FirewallDbContext _dbContext;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>This module's audit journal.</summary>
    private readonly FirewallAuditJournal _journal;

    /// <summary>Where the outcome is reported.</summary>
    private readonly ILogger<WhitelistSeeder> _logger;

    /// <summary>Creates the seeder.</summary>
    /// <param name="dbContext">The Firewall module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="logger">Where the outcome is reported.</param>
    public WhitelistSeeder(
        FirewallDbContext dbContext,
        IClock clock,
        FirewallAuditJournal journal,
        ILogger<WhitelistSeeder> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Seeds the whitelist from <paramref name="options"/>, if there is anything to seed.</summary>
    /// <param name="options">The module's options, carrying the installer's seed.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>True when a row was written; false when there was nothing to write.</returns>
    public async Task<bool> SeedAsync(FirewallOptions options, CancellationToken cancellationToken)
    {
        var cidr = options.SeedWhitelistCidr;
        if (string.IsNullOrWhiteSpace(cidr))
        {
            // A console install genuinely has no client address. The installer says so on the way
            // out; there is nothing for this to do and nothing worth logging every boot.
            return false;
        }

        if (await _dbContext.WhitelistSeedRecords.AnyAsync(cancellationToken))
        {
            return false;
        }

        if (await _dbContext.WhitelistEntries.AnyAsync(cancellationToken))
        {
            // A whitelist somebody has already written to is theirs. The marker is deliberately not
            // written here: nothing was seeded, and claiming otherwise would hide the seed from a
            // panel whose rows were all removed before it ever ran.
            return false;
        }

        if (!CidrRangeNormalizer.TryNormalize(cidr, out var normalized))
        {
            LogUnusableSeed(_logger, cidr, null);
            return false;
        }

        var now = _clock.UtcNow;
        _dbContext.WhitelistEntries.Add(new WhitelistEntry(Guid.NewGuid(), normalized, SeedNote, now));
        _dbContext.WhitelistSeedRecords.Add(new WhitelistSeedRecord(SeedRecordId, normalized, now));
        await _dbContext.SaveChangesAsync(cancellationToken);

        // One entry, in the journal an administrator cannot delete a row out of. Creating an
        // exemption is a security decision whoever it was made by, and this one is made by the panel
        // itself with nobody signed in — so without the entry the whitelist's first row is the only
        // one in it with no history at all.
        await _journal.RecordSystemAsync(
            AuditActions.FirewallWhitelistSeeded, normalized, succeeded: true, cancellationToken);

        LogSeeded(_logger, normalized, null);
        return true;
    }
}

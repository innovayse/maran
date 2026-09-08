using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Resources;
using Maran.Modules.Backups.Services;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Backups.Tests.TestSupport;

/// <summary>
/// Builds isolated <see cref="BackupsDbContext"/> instances for a named tenant, plus the rows to
/// seed them with. Each context gets its own uniquely-named in-memory database unless a caller
/// passes a shared name, which is what an isolation test needs: two contexts, two principals, ONE
/// database, so the only thing separating the rows is the query filter under test.
/// </summary>
public static class BackupsTestContext
{
    /// <summary>The instant seeded rows are stamped with, so orderings in tests are decidable.</summary>
    public static readonly DateTimeOffset SeedInstant = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Creates a context over a fresh database, seen as <paramref name="currentUser"/>.</summary>
    /// <param name="currentUser">The principal whose tenant scope the context is bound to.</param>
    /// <param name="databaseName">The in-memory database to open; a fresh one when omitted.</param>
    /// <returns>A context bound to that principal and that database.</returns>
    public static BackupsDbContext Create(ICurrentUser currentUser, string? databaseName = null)
    {
        var builder = new DbContextOptionsBuilder<BackupsDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString());

        return new BackupsDbContext(builder.Options, currentUser);
    }

    /// <summary>The directory a healthy agent reports writing backups into.</summary>
    /// <remarks>
    /// Not a value the panel stores: it is what <c>AgentInfo.backup_root</c> says, which is the
    /// agent's own <c>AgentPaths::BACKUP_ROOT</c>. It lives here so a test can hand it to a stub
    /// agent and then assert it came back out of the listing.
    /// </remarks>
    public const string AgentBackupRoot = "/var/backups/maran";

    /// <summary>Creates a context over a fresh database that already holds the default destination.</summary>
    /// <param name="currentUser">The principal whose tenant scope the context is bound to.</param>
    /// <param name="databaseName">The in-memory database to open; a fresh one when omitted.</param>
    /// <returns>A context whose database has the one destination a booted panel reconciles.</returns>
    /// <remarks>
    /// Every operation resolves a destination before it acts, so a test database without one is a
    /// panel that refuses every backup — which is a real state and has its own tests, but is not the
    /// state most tests mean to be in. This helper puts the fixture in the state a started server is
    /// in, and the tests that want the other state use <see cref="Create"/> and seed nothing.
    /// </remarks>
    public static BackupsDbContext CreateSeeded(ICurrentUser currentUser, string? databaseName = null)
    {
        var context = Create(currentUser, databaseName);

        if (!context.BackupDestinations.Any())
        {
            context.BackupDestinations.Add(DefaultDestinationRow());
            context.SaveChanges();
        }

        return context;
    }

    /// <summary>Builds the default local destination row a booted panel reconciles.</summary>
    /// <returns>The row, with the fixed identity the seeder uses and no recorded path.</returns>
    /// <remarks>
    /// The path is empty because the panel has none to record: a local destination's directory is
    /// the agent's constant, read from the handshake when a screen shows it.
    /// </remarks>
    public static BackupDestination DefaultDestinationRow()
    {
        return new BackupDestination(
            BackupDestination.DefaultDestinationId,
            "Local storage",
            BackupDestinationKind.Local,
            string.Empty,
            isDefault: true,
            SeedInstant);
    }

    /// <summary>Builds a completed backup row for <paramref name="accountId"/>.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="id">The backup's identity, which is also the agent's backup id.</param>
    /// <param name="kind">Why the backup was taken.</param>
    /// <returns>A row in the state a finished run leaves behind.</returns>
    public static Backup CompletedRow(Guid accountId, Guid? id = null, BackupKind kind = BackupKind.Manual)
    {
        var backup = new Backup(id ?? Guid.NewGuid(), accountId, destinationId: null, kind, SeedInstant);
        backup.Completed(1024, new string('a', 64), 2, SeedInstant.AddMinutes(1));
        return backup;
    }

    /// <summary>Builds a row for a backup the agent is still making.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="id">The backup's identity.</param>
    /// <returns>A row in the state an unfinished run leaves behind.</returns>
    public static Backup RunningRow(Guid accountId, Guid? id = null)
    {
        return new Backup(id ?? Guid.NewGuid(), accountId, destinationId: null, BackupKind.Manual, SeedInstant);
    }

    /// <summary>Builds the failure-name resolver over this module's REAL resource files.</summary>
    /// <returns>The resolver, reading <c>Resources/DisplayNames*.resx</c> as it does in the panel.</returns>
    /// <remarks>
    /// A stub localizer would prove only that a handler calls something; the thing worth checking is
    /// that a failure code a backup row can actually record HAS an entry under this key scheme, and
    /// that is only observable against the real resource files (rules/testing.md "A check must be
    /// able to observe what it reports on").
    /// </remarks>
    public static BackupFailureDisplayNames FailureNames()
    {
        return new BackupFailureDisplayNames(new StringLocalizer<DisplayNames>(LocalizerFactory()));
    }

    /// <summary>Builds the destination-name resolver over this module's REAL resource files.</summary>
    /// <returns>The resolver, reading <c>Resources/DisplayNames*.resx</c> as it does in the panel.</returns>
    public static BackupDestinationDisplayNames DestinationNames()
    {
        return new BackupDestinationDisplayNames(new StringLocalizer<DisplayNames>(LocalizerFactory()));
    }

    /// <summary>The localizer factory both resolvers are built over.</summary>
    /// <returns>A factory reading the module assembly's embedded resources.</returns>
    private static ResourceManagerStringLocalizerFactory LocalizerFactory()
    {
        return new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);
    }
}

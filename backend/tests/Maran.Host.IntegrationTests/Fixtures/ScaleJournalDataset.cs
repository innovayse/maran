using System.Security.Cryptography;
using System.Text;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Domain.Enums;
using Maran.Modules.Firewall.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The journal half of the scale dataset (<see cref="ScaleDataset"/>): the rows that grow with the
/// accounts rather than describing them — backups, audit events, tasks — plus the host-wide lists an
/// administrator pages through: backup destinations, the administrator's sessions, bans and the
/// whitelist. Same reproducibility rule as its other half: ids from <see cref="ScaleDataset.IdOf"/>,
/// instants from <see cref="ScaleDataset.Epoch"/>, nothing from a clock or a random source.
/// </summary>
public static class ScaleJournalDataset
{
    /// <summary>How many backups each account holds: one completed, one failed, one scheduled and completed.</summary>
    public const int BackupsPerAccount = 3;

    /// <summary>How many audit events each account's activity contributed to the journal.</summary>
    public const int AuditEventsPerAccount = 10;

    /// <summary>How many tasks each account's operations recorded.</summary>
    public const int TasksPerAccount = 5;

    /// <summary>How many backup destinations the host records.</summary>
    public const int DestinationCount = 20;

    /// <summary>How many live sessions the administrator holds.</summary>
    public const int SessionCount = 300;

    /// <summary>How many ban episodes the firewall has recorded.</summary>
    public const int BanCount = 1_000;

    /// <summary>How many whitelist entries the firewall holds.</summary>
    public const int WhitelistCount = 200;

    /// <summary>The audit actions the journal is spread across, so the action filter selects a subset.</summary>
    private static readonly string[] AuditActions =
        ["LoginSucceeded", "LoginFailed", "AccountCreated", "SiteCreated", "DatabaseCreated"];

    /// <summary>The task kinds the tasks are spread across, so the kind filter selects a subset.</summary>
    private static readonly string[] Kinds =
        [TaskKinds.BackupCreate, TaskKinds.CertificateIssue, TaskKinds.CertificateRenewal, TaskKinds.PhpVersionInstall];

    /// <summary>Writes the journal half of the dataset into an already migrated, already seeded panel.</summary>
    /// <param name="services">A scope's provider, outside any request, so no tenant filter applies.</param>
    /// <param name="administratorId">The administrator whose sessions are seeded.</param>
    /// <returns>A task that completes when every row is committed.</returns>
    public static async Task SeedAsync(IServiceProvider services, Guid administratorId)
    {
        await SeedBackupsAsync(services.GetRequiredService<BackupsDbContext>());
        await SeedIdentityAsync(services.GetRequiredService<IdentityDbContext>(), administratorId);
        await SeedTasksAsync(services.GetRequiredService<TasksDbContext>());
        await SeedFirewallAsync(services.GetRequiredService<FirewallDbContext>());
    }

    /// <summary>Stores the destinations, then each account's three backups in their final states.</summary>
    /// <param name="context">The Backups module's context.</param>
    /// <returns>A task that completes when the rows are committed.</returns>
    private static async Task SeedBackupsAsync(BackupsDbContext context)
    {
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        for (var index = 0; index < DestinationCount; index++)
        {
            context.BackupDestinations.Add(new BackupDestination(
                ScaleDataset.IdOf(8, index), $"Destination {index:D2}", BackupDestinationKind.Local,
                $"/var/backups/scale/{index:D2}", isDefault: false, ScaleDataset.Epoch.AddMinutes(index)));
        }

        for (var index = 0; index < ScaleDataset.AccountCount * BackupsPerAccount; index++)
        {
            var accountId = ScaleDataset.IdOf(2, index / BackupsPerAccount);
            var startedAt = ScaleDataset.Epoch.AddMinutes(index);
            var kind = index % BackupsPerAccount == 2 ? BackupKind.Scheduled : BackupKind.Manual;
            var backup = new Backup(ScaleDataset.IdOf(9, index), accountId, null, kind, startedAt);
            if (index % BackupsPerAccount == 1)
            {
                backup.Failed("AgentSystemFailure", startedAt.AddMinutes(1));
            }
            else
            {
                backup.Completed(1_048_576L * (index % 500 + 1), new string('a', 64), 2, startedAt.AddMinutes(1));
            }

            context.Backups.Add(backup);
        }

        await context.SaveChangesAsync();
    }

    /// <summary>Stores the audit journal and the administrator's live sessions.</summary>
    /// <param name="context">The Identity module's context.</param>
    /// <param name="administratorId">The administrator whose sessions are stored.</param>
    /// <returns>A task that completes when the rows are committed.</returns>
    private static async Task SeedIdentityAsync(IdentityDbContext context, Guid administratorId)
    {
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        for (var index = 0; index < ScaleDataset.AccountCount * AuditEventsPerAccount; index++)
        {
            var account = ScaleDataset.AccountName(index / AuditEventsPerAccount);
            var action = AuditActions[index % AuditActions.Length];
            context.AuditEvents.Add(new AuditEvent(
                ScaleDataset.IdOf(10, index), ScaleDataset.Epoch.AddMinutes(index), null, account, action, account,
                $"192.0.2.{index % 250 + 1}", "scale-dataset", action != "LoginFailed", null));
        }

        for (var index = 0; index < SessionCount; index++)
        {
            // A token hash has the stored length of a real one: base64 of a SHA-256 digest.
            var tokenHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes($"scale-session-{index}")));
            var issuedAt = ScaleDataset.Epoch.AddMinutes(index);
            context.Sessions.Add(new Session(
                ScaleDataset.IdOf(11, index), administratorId, ScaleDataset.IdOf(12, index), tokenHash, issuedAt,
                issuedAt.AddYears(100), $"198.51.100.{index % 250 + 1}", "scale-dataset"));
        }

        await context.SaveChangesAsync();
    }

    /// <summary>Stores each account's tasks, every fifth one failed and the rest completed.</summary>
    /// <param name="context">The Tasks module's context.</param>
    /// <returns>A task that completes when the rows are committed.</returns>
    private static async Task SeedTasksAsync(TasksDbContext context)
    {
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        for (var index = 0; index < ScaleDataset.AccountCount * TasksPerAccount; index++)
        {
            var accountIndex = index / TasksPerAccount;
            var startedAt = ScaleDataset.Epoch.AddMinutes(index);
            var task = new PanelTask(
                ScaleDataset.IdOf(13, index), Kinds[index % Kinds.Length],
                $"{ScaleDataset.AccountName(accountIndex)}.example.com", ScaleDataset.IdOf(2, accountIndex),
                TaskAudience.AccountOwner, null, startedAt);
            if (index % TasksPerAccount == 0)
            {
                task.Fail("AgentSystemFailure", startedAt.AddMinutes(1));
            }
            else
            {
                task.Complete(startedAt.AddMinutes(1));
            }

            context.PanelTasks.Add(task);
        }

        await context.SaveChangesAsync();
    }

    /// <summary>Stores the ban episodes and the whitelist.</summary>
    /// <param name="context">The Firewall module's context.</param>
    /// <returns>A task that completes when the rows are committed.</returns>
    /// <remarks>Bans never expire, so the list's "active" reading holds every one whatever day the suite runs.</remarks>
    private static async Task SeedFirewallAsync(FirewallDbContext context)
    {
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        for (var index = 0; index < BanCount; index++)
        {
            context.BanEpisodes.Add(new BanEpisode(
                ScaleDataset.IdOf(14, index), $"203.0.{index / 250}.{index % 250 + 1}", BanReason.Manual, null, 0,
                ScaleDataset.Epoch.AddMinutes(index), null));
        }

        for (var index = 0; index < WhitelistCount; index++)
        {
            context.WhitelistEntries.Add(new WhitelistEntry(
                ScaleDataset.IdOf(15, index), $"10.{index / 250}.{index % 250}.0/24", $"Office {index:D3}",
                ScaleDataset.Epoch.AddMinutes(index)));
        }

        await context.SaveChangesAsync();
    }
}

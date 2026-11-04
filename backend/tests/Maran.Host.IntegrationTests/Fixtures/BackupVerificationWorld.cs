using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Domain.ValueObjects;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Notifications.Interfaces;
using Maran.Modules.Tasks.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wolverine;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The panel the backup verification tests run against: the real Host on real PostgreSQL with the
/// agent's backup seam scripted, two accounts (<c>acme</c> and <c>beta</c>) each with an owner
/// login (<c>owner-a</c> and <c>owner-b</c>), one administrator (<c>admin</c>) and the server's default
/// local destination.
/// </summary>
/// <remarks>
/// The recipient of operator alerts is a choice of the test, not of this world: with one the alert
/// becomes a mail request the recording mailer sees, without one the real directory answers nobody
/// and the skipped path is the one under test.
/// </remarks>
public static class BackupVerificationWorld
{
    /// <summary>How long a test waits for something that happens on the bus or the sweep to be visible.</summary>
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    /// <summary>Boots the panel and seeds the world described on the type.</summary>
    /// <param name="database">The test's own database.</param>
    /// <param name="agent">The scripted agent backup seam.</param>
    /// <param name="mailer">The mail server's double.</param>
    /// <param name="alertRecipient">Whether operator alerts have an address to go to.</param>
    /// <param name="clock">A clock the test moves, or <c>null</c> for the real one.</param>
    /// <returns>The booted factory, which the caller disposes.</returns>
    public static async Task<WebApplicationFactory<Program>> BootAsync(
        TestDatabase database,
        ScriptedVerificationAgentBackupClient agent,
        RecordingMailer mailer,
        bool alertRecipient,
        AdvancingClock? clock = null)
    {
        var factory = EndpointHarness.CreateFactory(
            database,
            services =>
            {
                services.RemoveAll<IAgentBackupClient>();
                services.AddSingleton<IAgentBackupClient>(agent);
                services.AddSingleton<IMailer>(mailer);

                if (alertRecipient)
                {
                    services.RemoveAll<IAlertRecipientDirectory>();
                    services.AddSingleton<IAlertRecipientDirectory, FixedAlertRecipientDirectory>();
                }

                if (clock is not null)
                {
                    services.RemoveAll<IClock>();
                    services.AddSingleton<IClock>(clock);
                }
            });

        await EndpointHarness.MigrateAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabasesDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<TasksDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<BackupsDbContext>().Database.MigrateAsync();
        }

        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, acme) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        var (_, beta) = await EndpointHarness.SeedAccountAsync(factory, "beta");
        await EndpointHarness.SeedCustomerAsync(factory, "owner-a", acme);
        await EndpointHarness.SeedCustomerAsync(factory, "owner-b", beta);
        await EnsureDefaultDestinationAsync(factory);

        return factory;
    }

    /// <summary>Reads an account's identifier.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountName">The account's system user name.</param>
    /// <returns>The account's identifier.</returns>
    public static Task<Guid> AccountIdAsync(WebApplicationFactory<Program> factory, string accountName)
    {
        return SubUserTeamWorld.AccountIdAsync(factory, accountName);
    }

    /// <summary>Stores one finished manual backup for an account, in the state a run leaves behind.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountName">The account that owns it.</param>
    /// <returns>The backup's identifier.</returns>
    public static async Task<Guid> SeedCompletedBackupAsync(
        WebApplicationFactory<Program> factory,
        string accountName)
    {
        var accountId = await AccountIdAsync(factory, accountName);

        using var scope = factory.Services.CreateScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var backup = new Backup(Guid.NewGuid(), accountId, destinationId: null, BackupKind.Manual, now);
        backup.Completed(4096, new string('a', 64), 1, now);
        backups.Backups.Add(backup);
        await backups.SaveChangesAsync();

        return backup.Id;
    }

    /// <summary>Stores an enabled daily schedule covering one account, as if enabled at a given moment.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountName">The account the schedule is an override for.</param>
    /// <param name="destinationId">The destination it names, or <c>null</c> for the default.</param>
    /// <param name="hourUtc">The hour of day, in UTC, the schedule names.</param>
    /// <param name="enabledAt">The moment the schedule was switched on.</param>
    /// <returns>The schedule's identifier.</returns>
    public static async Task<Guid> SeedDailyScheduleAsync(
        WebApplicationFactory<Program> factory,
        string accountName,
        Guid? destinationId,
        int hourUtc,
        DateTimeOffset enabledAt)
    {
        var accountId = await AccountIdAsync(factory, accountName);

        using var scope = factory.Services.CreateScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();

        var schedule = new BackupSchedule(Guid.NewGuid(), accountId, destinationId, new DailyBackupCadence(hourUtc), 7);
        schedule.Reconfigure(destinationId, new DailyBackupCadence(hourUtc), 7, enabled: true, enabledAt);
        backups.BackupSchedules.Add(schedule);
        await backups.SaveChangesAsync();

        return schedule.Id;
    }

    /// <summary>Stores one remote destination, which this agent does not serve.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The destination's identifier.</returns>
    public static async Task<Guid> SeedRemoteDestinationAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var destination = new BackupDestination(
            Guid.NewGuid(), "Off site", BackupDestinationKind.S3, string.Empty, isDefault: false, now);
        backups.BackupDestinations.Add(destination);
        await backups.SaveChangesAsync();

        return destination.Id;
    }

    /// <summary>Removes every destination, so the server has nowhere to put a backup that names none.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The task that removes them.</returns>
    public static async Task RemoveEveryDestinationAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();

        backups.BackupDestinations.RemoveRange(await backups.BackupDestinations.ToListAsync());
        await backups.SaveChangesAsync();
    }

    /// <summary>Reads every backup row the host holds, with the tenant filter bypassed.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>Every row.</returns>
    public static async Task<IReadOnlyList<Backup>> BackupsAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();

        // A test bypasses the filter to see every tenant's rows; banning it here would put a
        // suppression on the assertions that verify the rule (rules/csharp.md).
        return await backups.Backups.IgnoreQueryFilters().AsNoTracking().ToListAsync();
    }

    /// <summary>Reads the journal entries of the named actions, oldest first.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="actions">The action names to keep.</param>
    /// <returns>The matching entries.</returns>
    public static Task<IReadOnlyList<AuditEvent>> AuditEntriesAsync(
        WebApplicationFactory<Program> factory,
        params string[] actions)
    {
        return SubUserTeamWorld.AuditEntriesAsync(factory, actions);
    }

    /// <summary>Waits until the journal holds at least <paramref name="count"/> entries of an action.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="action">The action to wait for.</param>
    /// <param name="count">How many entries are expected at least.</param>
    /// <returns>The entries, oldest first.</returns>
    public static async Task<IReadOnlyList<AuditEvent>> WaitForAuditAsync(
        WebApplicationFactory<Program> factory,
        string action,
        int count)
    {
        var waited = Stopwatch.StartNew();
        IReadOnlyList<AuditEvent> entries = [];

        while (waited.Elapsed < Wait)
        {
            entries = await AuditEntriesAsync(factory, action);
            if (entries.Count >= count)
            {
                break;
            }

            await Task.Delay(50);
        }

        return entries;
    }

    /// <summary>Waits until the recording mailer holds at least <paramref name="count"/> messages.</summary>
    /// <param name="mailer">The mail server's double.</param>
    /// <param name="count">How many messages are expected at least.</param>
    /// <returns>Everything the mailer was asked to send.</returns>
    public static async Task<IReadOnlyList<SentMail>> WaitForMailAsync(RecordingMailer mailer, int count)
    {
        var waited = Stopwatch.StartNew();

        while (mailer.Sent.Count < count && waited.Elapsed < Wait)
        {
            await Task.Delay(50);
        }

        return mailer.Sent;
    }

    /// <summary>Reports what the mailer was asked to send once the mail path has proved it is alive.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="mailer">The mail server's double.</param>
    /// <returns>Everything the mailer was asked to send, except the marker this method sends itself.</returns>
    /// <remarks>
    /// A negative needs a positive control to mean anything: this publishes a request of its own to
    /// the bus after the work under test has finished and waits for THAT to arrive, so an empty answer
    /// is "the path works and nothing else came", not "the path is dead". It does not prove ordering —
    /// local queues run handlers in parallel — and so is the weaker half of a no-mail assertion; the
    /// stronger half is the journal, because the alert's entry is written before the request is
    /// published and a test reads it with no race at all.
    /// </remarks>
    public static async Task<IReadOnlyList<SentMail>> DrainedMailAsync(
        WebApplicationFactory<Program> factory,
        RecordingMailer mailer)
    {
        const string Marker = "marker@example.com";

        using (var scope = factory.Services.CreateScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.PublishAsync(new SendMailRequested(Marker, "drain", "drain"));
        }

        await SubUserTeamWorld.MailToAsync(mailer, Marker);

        return mailer.Sent.Where(mail => { return mail.Recipient != Marker; }).ToList();
    }

    /// <summary>Waits until a backup's verification state is not "not verified", then returns the row.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="backupId">The backup to watch.</param>
    /// <returns>The row as the database holds it, whether or not it ever left the initial state.</returns>
    public static async Task<Backup> WaitForVerificationAsync(WebApplicationFactory<Program> factory, Guid backupId)
    {
        var waited = Stopwatch.StartNew();
        Backup row;

        do
        {
            row = (await BackupsAsync(factory)).Single(candidate => { return candidate.Id == backupId; });
            if (row.VerificationState != BackupVerificationState.NotVerified)
            {
                break;
            }

            await Task.Delay(50);
        }
        while (waited.Elapsed < Wait);

        return row;
    }

    /// <summary>Starts a manual backup of an account through the HTTP surface, as its owner.</summary>
    /// <param name="client">A client signed in as a login that may back the account up.</param>
    /// <param name="accountId">The account to back up.</param>
    /// <returns>The identifier of the backup the panel created.</returns>
    public static async Task<Guid> CreateManualBackupAsync(HttpClient client, Guid accountId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/backups", new { accountId });
        response.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Waits until the scripted agent has been asked to verify <paramref name="count"/> times.</summary>
    /// <param name="agent">The scripted agent backup seam.</param>
    /// <param name="count">How many verification questions are expected at least.</param>
    /// <returns>The task that completes when the agent has been asked, or when the wait is over.</returns>
    public static async Task WaitForAgentVerificationsAsync(ScriptedVerificationAgentBackupClient agent, int count)
    {
        var waited = Stopwatch.StartNew();

        while (agent.VerificationCount < count && waited.Elapsed < Wait)
        {
            await Task.Delay(50);
        }
    }

    /// <summary>Reads one backup through the HTTP surface.</summary>
    /// <param name="client">A client allowed to read the backup.</param>
    /// <param name="backupId">The backup to read.</param>
    /// <param name="language">The <c>Accept-Language</c> to ask in, or <c>null</c> for the default.</param>
    /// <returns>The parsed body, which the caller disposes.</returns>
    public static async Task<JsonDocument> GetBackupAsync(HttpClient client, Guid backupId, string? language = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/backups/{backupId}");
        if (language is not null)
        {
            request.Headers.Add("Accept-Language", language);
        }

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Guarantees exactly one default local destination exists on the host.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The task that seeds it if the start-up seeder did not.</returns>
    private static async Task EnsureDefaultDestinationAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var destinations = await backups.BackupDestinations.ToListAsync();
        if (!destinations.Any(row => { return row.IsDefault; }))
        {
            backups.BackupDestinations.Add(new BackupDestination(
                Guid.NewGuid(), "Local storage", BackupDestinationKind.Local, string.Empty, isDefault: true, now));
            await backups.SaveChangesAsync();
        }
    }
}

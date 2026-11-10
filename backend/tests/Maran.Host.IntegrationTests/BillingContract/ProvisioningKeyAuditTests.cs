using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.IntegrationTests.BillingContract;

/// <summary>
/// What the journal holds after a reseller's key writes through the reseller API (issue #7): for each of the
/// five mutations the call itself (route template, status, the key as actor) and the domain event the handler
/// writes (the key as actor, the account it concerns), a refusal as a failure, a retried create once, and a
/// termination's final backup; and in no entry, anywhere, the key.
/// </summary>
/// <remarks>
/// UNOBSERVED HERE: the audit hash chain and the export of the journal, which have their own tests; and the
/// ordering of the two entries of one call, which is not promised.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ProvisioningKeyAuditTests : IAsyncLifetime
{
    /// <summary>The first part of every route template of the reseller API as the journal records it.</summary>
    private const string TemplatePrefix = "api/v1/reseller-api/accounts";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ProvisioningKeyAuditTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>The name a key is journalled under: the prefix <c>api-key:</c> and the key's id.</summary>
    private static string ActorOf(ApiKeyIssued key)
    {
        return "api-key:" + key.Id.ToString("N");
    }

    /// <summary>A creation body.</summary>
    private static object CreateBody(BillingContractWorld world, string name)
    {
        return new
        {
            Name = name,
            PrimaryDomain = $"{name}.example.com",
            PlanId = world.World.Alpha.PlanId,
            OwnerEmail = "owner@example.com",
        };
    }

    /// <summary>Reads every journal entry there is, oldest first.</summary>
    private static async Task<IReadOnlyList<AuditEvent>> EveryEntryAsync(BillingContractWorld world)
    {
        return await world.World.IdentityAsync(async identity =>
        {
            return await identity.AuditEvents.AsNoTracking().OrderBy(entry => entry.OccurredAt).ToListAsync();
        });
    }

    /// <summary>Finds the one call entry of a method, a route tail and a status.</summary>
    private static AuditEvent CallOf(IEnumerable<AuditEvent> entries, string method, string tail, int status)
    {
        return Assert.Single(
            entries.Where(entry => { return entry.Action == IdentityAuditActions.ApiKeyCall; }),
            entry =>
            {
                return entry.Subject.StartsWith($"{method} {TemplatePrefix}", StringComparison.Ordinal)
                    && entry.Subject.EndsWith($"{tail} {status}", StringComparison.Ordinal);
            });
    }

    /// <summary>Reads the id of the account a successful creation answered with.</summary>
    private static async Task<Guid> IdOfAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// A creation, a suspension, a reactivation, a plan change and a termination by a key are each journalled as
    /// a call by the key and as the event the handler writes by the key, with the account it concerns; a
    /// termination also takes and journals the final backup; and no entry holds the key.
    /// </summary>
    [Fact]
    public async Task Each_mutation_by_a_key_is_journalled_as_a_call_and_as_a_domain_event_by_the_key()
    {
        await using var world = await BillingContractWorld.BootAsync(_pg);
        var actor = ActorOf(world.Full);
        var secret = world.Full.Secret;

        var accountId = await IdOfAsync(
            await world.SendAsync(HttpMethod.Post, "/accounts", secret, "audit-create", CreateBody(world, "auditone")));
        var path = $"/accounts/{accountId}";
        Assert.Equal(HttpStatusCode.OK, (await world.SendAsync(HttpMethod.Post, path + "/suspend", secret, "audit-suspend")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await world.SendAsync(HttpMethod.Post, path + "/reactivate", secret, "audit-reactivate")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await world.SendAsync(HttpMethod.Put, path + "/plan", secret, "audit-plan", new { PlanId = world.SmallPlanId })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await world.SendAsync(HttpMethod.Delete, path, secret, "audit-delete")).StatusCode);

        var entries = await EveryEntryAsync(world);
        var calls = new[]
        {
            CallOf(entries, "POST", string.Empty, 201),
            CallOf(entries, "POST", "/suspend", 200),
            CallOf(entries, "POST", "/reactivate", 200),
            CallOf(entries, "PUT", "/plan", 200),
            CallOf(entries, "DELETE", string.Empty, 200),
        };
        Assert.Equal(calls.Length, entries.Count(entry => { return entry.Action == IdentityAuditActions.ApiKeyCall; }));
        foreach (var call in calls)
        {
            Assert.Equal(actor, call.ActorUsername);
            Assert.True(call.Succeeded);
            Assert.DoesNotContain(accountId.ToString(), call.Subject, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var action in new[]
        {
            AccountAuditActions.AccountCreated,
            AccountAuditActions.AccountSuspended,
            AccountAuditActions.AccountReactivated,
            AccountAuditActions.AccountPlanChanged,
            AccountAuditActions.AccountDeleted,
        })
        {
            var domain = Assert.Single(entries, entry => { return entry.Action == action; });
            Assert.Equal(actor, domain.ActorUsername);
            Assert.True(domain.Succeeded, action);

            // The account was deleted last, and deleting an account pseudonymises the subject of every
            // entry that carries its id; the deletion entry carries none, so it alone still names the account.
            if (action == AccountAuditActions.AccountDeleted)
            {
                Assert.Contains("auditone", domain.Subject, StringComparison.Ordinal);
            }
            else
            {
                Assert.StartsWith(AuditPseudonym.AccountSubjectPrefix, domain.Subject, StringComparison.Ordinal);
            }
        }


        // Deleting the account replaced its id on every entry that carried it with one random stand-in, so
        // the entries still belong together and no longer name the account.
        var stampedWith = entries
            .Where(entry => { return entry.Action != AccountAuditActions.AccountDeleted && entry.AccountId is not null; })
            .Select(entry => { return entry.AccountId; })
            .Distinct()
            .ToList();
        var standIn = Assert.Single(stampedWith);
        Assert.NotEqual(accountId, standIn);
        var finalBackup = Assert.Single(entries, entry => { return entry.Action == AccountAuditActions.FinalBackupTaken; });
        Assert.True(finalBackup.Succeeded);

        // Its subject is the account's name, and it stays: a key carries no account id, so the account rule
        // does not reach the entry, and no login is recorded on it, so the person rule does not either (rows
        // are matched by an identity recorded on them, never by the text of their subject, issue #464). Like
        // the deletion entry, it is the name left to search for once the deletion has finished.
        Assert.Equal("auditone", finalBackup.Subject);
        Assert.Contains(world.Backup.Creates, create => { return create.AccountUsername == "auditone"; });

        foreach (var entry in entries)
        {
            foreach (var text in new[] { entry.Subject, entry.ActorUsername, entry.IpAddress, entry.UserAgent })
            {
                Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// A refused write is journalled as a failure by the key, both as the call (with its refusing status) and as
    /// the event that did not take effect, and changes nothing.
    /// </summary>
    [Fact]
    public async Task A_refused_write_by_a_key_is_journalled_as_a_failure()
    {
        await using var world = await BillingContractWorld.BootAsync(_pg);
        var actor = ActorOf(world.Full);
        var unknown = Guid.NewGuid();

        using var taken = await world.SendAsync(
            HttpMethod.Post, "/accounts", world.Full.Secret, "audit-taken", CreateBody(world, world.World.Alpha.AccountName));
        using var missing = await world.SendAsync(
            HttpMethod.Post, $"/accounts/{unknown}/suspend", world.Full.Secret, "audit-missing");

        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var entries = await EveryEntryAsync(world);
        var takenCall = CallOf(entries, "POST", string.Empty, 409);
        var missingCall = CallOf(entries, "POST", "/suspend", 404);
        var createFailure = Assert.Single(entries, entry => { return entry.Action == AccountAuditActions.AccountCreated; });
        var suspendFailure = Assert.Single(entries, entry => { return entry.Action == AccountAuditActions.AccountSuspended; });

        foreach (var entry in new[] { takenCall, missingCall, createFailure, suspendFailure })
        {
            Assert.Equal(actor, entry.ActorUsername);
            Assert.False(entry.Succeeded);
        }

        Assert.Equal(world.World.Alpha.AccountName, createFailure.Subject);
        Assert.Equal(unknown.ToString("D"), suspendFailure.Subject);
        Assert.Empty(world.Agent.Created);
    }

    /// <summary>A create retried with the same key answers the first answer again and creates and journals once.</summary>
    [Fact]
    public async Task A_create_retried_with_the_same_key_creates_and_journals_once()
    {
        await using var world = await BillingContractWorld.BootAsync(_pg);
        var body = CreateBody(world, "retryone");

        using var first = await world.SendAsync(HttpMethod.Post, "/accounts", world.Full.Secret, "audit-retry", body);
        using var second = await world.SendAsync(HttpMethod.Post, "/accounts", world.Full.Secret, "audit-retry", body);

        var firstId = await IdOfAsync(first);
        Assert.Equal(firstId, await IdOfAsync(second));
        Assert.True(second.Headers.Contains(IdempotencyHeaders.Replayed));
        Assert.False(first.Headers.Contains(IdempotencyHeaders.Replayed));
        Assert.Equal(["retryone"], world.Agent.Created);
        Assert.Single((await EveryEntryAsync(world)), entry => { return entry.Action == AccountAuditActions.AccountCreated; });
    }

    /// <summary>
    /// A termination whose final backup fails is refused and leaves the account, the host and the stored row as
    /// they were, journalled as a failed deletion and a failed backup; once the backup works the same call
    /// succeeds. The refusal is a server error (500 <c>FinalBackupFailed</c>), not a conflict.
    /// </summary>
    [Fact]
    public async Task A_failed_final_backup_refuses_the_termination_and_leaves_the_account()
    {
        await using var world = await BillingContractWorld.BootAsync(_pg);
        var actor = ActorOf(world.Full);
        var path = $"/accounts/{world.World.Alpha.AccountId}";
        world.Backup.CreateFailureCode = "AgentSystemFailure";

        using var refused = await world.SendAsync(HttpMethod.Delete, path, world.Full.Secret, "audit-terminate-1");

        Assert.Equal(HttpStatusCode.InternalServerError, refused.StatusCode);
        Assert.Equal("FinalBackupFailed", await ProblemDocument.CodeAsync(refused));
        Assert.Empty(world.Agent.Deleted);
        Assert.NotNull(await world.StoredAccountAsync(world.World.Alpha.AccountId));
        var entries = await EveryEntryAsync(world);
        var deletion = Assert.Single(entries, entry => { return entry.Action == AccountAuditActions.AccountDeleted; });
        var backups = entries.Where(entry => { return entry.Action == AccountAuditActions.FinalBackupTaken; }).ToList();
        Assert.False(deletion.Succeeded);

        // The backup service and the deletion each record the failed backup, so there is at least one, never a success.
        Assert.NotEmpty(backups);
        Assert.All(backups, backup => { Assert.False(backup.Succeeded); });
        Assert.Equal(actor, deletion.ActorUsername);

        world.Backup.CreateFailureCode = null;
        using var terminated = await world.SendAsync(HttpMethod.Delete, path, world.Full.Secret, "audit-terminate-2");

        Assert.Equal(HttpStatusCode.OK, terminated.StatusCode);
        Assert.Equal([world.World.Alpha.AccountName], world.Agent.Deleted);
        Assert.Null(await world.StoredAccountAsync(world.World.Alpha.AccountId));
    }
}

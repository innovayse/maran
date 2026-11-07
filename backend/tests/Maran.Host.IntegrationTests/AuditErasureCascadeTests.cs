using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// What deleting an account through its real endpoint does to the audit journal, over the composed
/// panel and real PostgreSQL: the deleted login and the account stop being named in any column, the
/// people who stay keep theirs, the erasure is summarised, and the residue audit still answers clean.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it is an integration test.</b> The erasure is a subscriber of the deletion announcement, run
/// inline on the real bus, between the Accounts module's handler and its residue audit, and it
/// changes a column the audit counts (<c>AuditEvent.AccountId</c>). Only the composed panel shows that
/// the order holds: that the account's journal rows are gone from the audit's view BEFORE it looks, so
/// the deletion is not refused over its own history.
/// </para>
/// <para>
/// <b>The search is shown to work before it is trusted.</b> Every assertion that an identifier is gone
/// follows an assertion that the same search finds it in the journal as seeded (rules/testing.md, the
/// positive control). The search reads every identifying column of every row as one text, so a column
/// that was forgotten cannot hide an original.
/// </para>
/// <para>
/// <b>What it does not show.</b> That the erasure and the removal roll back together: the failure that
/// would show it is not manufacturable through the endpoint without breaking something the deletion
/// needs first. Rows that the other nine modules' journals write without an account id are not matched
/// by the account rule by design, and none are planted here.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class AuditErasureCascadeTests : IAsyncLifetime
{
    /// <summary>The account under test; deliberately not a substring of any login or address planted below.</summary>
    private const string AccountName = "acmeco";

    /// <summary>The login that owns the account and is deleted with it.</summary>
    private const string OwnerName = "acme-owner";

    /// <summary>The owner's e-mail address, as the harness builds it.</summary>
    private const string OwnerEmail = "acme-owner@example.com";

    /// <summary>An address only the owner's planted row carries.</summary>
    private const string PlantedAddress = "198.51.100.77";

    /// <summary>A user agent only the owner's planted row carries.</summary>
    private const string PlantedAgent = "planted-browser/4";

    /// <summary>The user agent the owner's real sign-in is made with, so a real row carries it too.</summary>
    private const string SignInAgent = "erasure-probe/9.9";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AuditErasureCascadeTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Deleting an account leaves no original account id, user id, login name, e-mail address, address or
    /// user agent of the deleted login anywhere in the journal.
    /// </summary>
    [Fact]
    public async Task Deleting_an_account_leaves_no_original_identifier_in_the_journal()
    {
        var agent = new StubAgentAccountsClient();
        await using var factory = CreateFactory(agent);
        var world = await SeedAsync(factory);
        var needles = Originals(world);

        // The probe's positive control: the very search that must come back empty finds every original
        // while the journal still holds them.
        var before = await JournalAsync(factory);
        foreach (var needle in needles)
        {
            Assert.True(Mentions(before, needle), $"the search could not find '{needle}' in the seeded journal");
        }

        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var response = await admin.DeleteAsync($"/api/v1/accounts/{world.AccountId}?skipFinalBackup=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([AccountName], agent.Deleted);
        var after = await JournalAsync(factory);
        foreach (var needle in needles)
        {
            Assert.False(Mentions(after, needle), $"'{needle}' is still in the journal after the deletion");
        }
    }

    /// <summary>The erasure is summarised by the panel itself, with a pseudonym and a count and nothing that names the login.</summary>
    [Fact]
    public async Task Deleting_an_account_writes_the_erasure_summaries()
    {
        await using var factory = CreateFactory(new StubAgentAccountsClient());
        var world = await SeedAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.DeleteAsync($"/api/v1/accounts/{world.AccountId}?skipFinalBackup=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summaries = (await JournalAsync(factory))
            .Where(row => { return row.Action == IdentityAuditActions.PersonalDataErased; })
            .ToList();
        Assert.Equal(2, summaries.Count);
        Assert.All(summaries, summary =>
        {
            Assert.Equal("maran-identity", summary.ActorUsername);
            Assert.Null(summary.ActorUserId);
            Assert.True(summary.Succeeded);
            Assert.Matches(new Regex("^erased-(user|account)-[0-9a-f]{8} rows=[1-9][0-9]*$"), summary.Subject);
        });
        Assert.Single(summaries, summary => { return summary.Subject.StartsWith("erased-user-", StringComparison.Ordinal); });
        Assert.Single(summaries, summary => { return summary.Subject.StartsWith("erased-account-", StringComparison.Ordinal); });
    }

    /// <summary>The tombstone keeps the account name, carries no account id, and the administrator keeps their own rows.</summary>
    [Fact]
    public async Task The_tombstone_keeps_the_account_name_and_the_administrator_keeps_their_rows()
    {
        await using var factory = CreateFactory(new StubAgentAccountsClient());
        var world = await SeedAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.DeleteAsync($"/api/v1/accounts/{world.AccountId}?skipFinalBackup=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await JournalAsync(factory);
        var tombstone = Assert.Single(rows, row => { return row.Action == AccountAuditActions.AccountDeleted; });
        Assert.Equal(AccountName, tombstone.Subject);
        Assert.Null(tombstone.AccountId);
        Assert.True(tombstone.Succeeded);
        Assert.Equal("admin", tombstone.ActorUsername);
        var signIn = Assert.Single(rows, row =>
        {
            return row.Action == IdentityAuditActions.LoginSucceeded && row.ActorUserId == world.AdministratorId;
        });
        Assert.Equal("admin", signIn.ActorUsername);
        Assert.Null(signIn.ErasedAt);
    }

    /// <summary>The login is gone, the residue audit answers clean for the account, and no journal row carries it any more.</summary>
    [Fact]
    public async Task The_residue_audit_still_answers_clean_after_the_erasure()
    {
        await using var factory = CreateFactory(new StubAgentAccountsClient());
        var world = await SeedAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.DeleteAsync($"/api/v1/accounts/{world.AccountId}?skipFinalBackup=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .Users.CountAsync(user => user.Username == OwnerName));
        var residue = await scope.ServiceProvider.GetRequiredService<IAccountResidueAuditor>()
            .FindResidueAsync(world.AccountId, CancellationToken.None);
        Assert.Empty(residue.Rows);
        Assert.Empty(residue.Unchecked);
        Assert.DoesNotContain(
            await JournalAsync(factory), row => { return row.AccountId == world.AccountId; });
    }

    /// <summary>Boots the host against this class's PostgreSQL with the agent's account operations stubbed.</summary>
    /// <param name="agent">The stand-in for the agent's account operations.</param>
    /// <returns>The factory, which the caller disposes.</returns>
    private WebApplicationFactory<Program> CreateFactory(IAgentAccountsClient agent)
    {
        return EndpointHarness.CreateFactory(_pg, services =>
        {
            services.RemoveAll<IAgentAccountsClient>();
            services.AddSingleton(agent);

            // The deletion is asked to skip the final backup, but the module still resolves the agent's
            // backup client; the stand-in keeps the composed panel from reaching a socket that is not there.
            services.RemoveAll<IAgentBackupClient>();
            services.AddSingleton<IAgentBackupClient>(new StubAgentBackupClient());
        });
    }

    /// <summary>The original identifiers of the deleted login and the account that must not survive.</summary>
    /// <param name="world">The seeded identities.</param>
    /// <returns>Ids, names, an address and two user agents.</returns>
    private static string[] Originals(SeededWorld world)
    {
        return
        [
            world.AccountId.ToString("D"),
            world.OwnerId.ToString("D"),
            OwnerName,
            OwnerEmail,
            PlantedAddress,
            PlantedAgent,
            SignInAgent,
        ];
    }

    /// <summary>Whether <paramref name="needle"/> appears, in any identifying column, of any row.</summary>
    /// <param name="rows">The journal.</param>
    /// <param name="needle">The original identifier.</param>
    /// <returns><c>true</c> when it is found, case-insensitively.</returns>
    private static bool Mentions(IEnumerable<AuditEvent> rows, string needle)
    {
        return rows.Any(row =>
        {
            var text = string.Join(
                '\n',
                row.ActorUserId?.ToString("D") ?? string.Empty,
                row.ActorUsername,
                row.Subject,
                row.IpAddress,
                row.UserAgent,
                row.AccountId?.ToString("D") ?? string.Empty,
                row.Impersonator?.OperatorId.ToString("D") ?? string.Empty,
                row.Impersonator?.Username ?? string.Empty);

            return text.Contains(needle, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>Reads the whole journal, untracked.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>Every journal row.</returns>
    private static async Task<List<AuditEvent>> JournalAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .AuditEvents.AsNoTracking().ToListAsync();
    }

    /// <summary>
    /// Migrates every module, stores an administrator, an account with its owner's login, signs the
    /// owner in over the real endpoint, and plants three journal rows the sign-in does not produce.
    /// </summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The identities the assertions refer to.</returns>
    /// <remarks>
    /// The sign-ins are real so that real rows exist with real columns; the planted rows cover what
    /// signing in cannot: a row that carries the account, a row by the administrator that names the
    /// owner's address as its subject, and the owner's address and agent in columns a test server leaves
    /// empty.
    /// </remarks>
    private static async Task<SeededWorld> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using (var migrating = factory.Services.CreateScope())
        {
            await ModuleSchemas.MigrateAsync(migrating.ServiceProvider);
        }

        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, AccountName);
        await EndpointHarness.SeedCustomerAsync(factory, OwnerName, accountId);

        using (var owner = factory.CreateClient())
        {
            owner.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("erasure-probe", "9.9"));
            var signedIn = await owner.PostAsJsonAsync(
                "/api/v1/auth/login", new { Username = OwnerName, Password = EndpointHarness.Password });
            Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        }

        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var ownerId = (await identity.Users.SingleAsync(user => user.Username == OwnerName)).Id;
        var administratorId = (await identity.Users.SingleAsync(user => user.Username == "admin")).Id;
        var at = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        identity.AuditEvents.AddRange(
            new AuditEvent(
                Guid.NewGuid(), at, ownerId, OwnerName, IdentityAuditActions.LoginSucceeded, "planted",
                PlantedAddress, PlantedAgent, succeeded: true, correlationId: null, accountId: accountId),
            new AuditEvent(
                Guid.NewGuid(), at, administratorId, "admin", IdentityAuditActions.SubUserInvited, OwnerEmail,
                string.Empty, string.Empty, succeeded: true, correlationId: null),
            new AuditEvent(
                Guid.NewGuid(), at, administratorId, "admin", AccountAuditActions.AccountSuspended, AccountName,
                string.Empty, string.Empty, succeeded: true, correlationId: null, accountId: accountId));
        await identity.SaveChangesAsync();

        return new SeededWorld(accountId, ownerId, administratorId);
    }

    /// <summary>The identities a test refers to.</summary>
    /// <param name="AccountId">The account that is deleted.</param>
    /// <param name="OwnerId">The login that owns it and is deleted with it.</param>
    /// <param name="AdministratorId">The administrator who deletes it and who stays.</param>
    private sealed record SeededWorld(Guid AccountId, Guid OwnerId, Guid AdministratorId);
}

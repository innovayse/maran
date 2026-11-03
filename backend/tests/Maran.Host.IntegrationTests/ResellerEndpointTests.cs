using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// An administrator manages resellers over real HTTP against a real PostgreSQL (issue #179): what a
/// creation stores, journals and mails, what a refused one leaves behind, what the list and the detail
/// say about what each reseller owns, what an envelope change does, and what a reseller reads about
/// itself.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class ResellerEndpointTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ResellerEndpointTests(PostgresFixture postgres)
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

    /// <summary>
    /// An administrator who creates a reseller gets 201 and its location; the reseller is stored in its
    /// contact's language with its envelope, its login is created and invited, the invitation reaches
    /// the contact's address with a token in it, and both the creation and the invitation are journalled.
    /// </summary>
    [Fact]
    public async Task An_administrator_creates_a_reseller_which_is_stored_journalled_and_invited_by_mail()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");

        var response = await admin.PostAsJsonAsync(
            "/api/v1/resellers", ResellerRequestBodies.Reseller("charlie", language: "ru"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/resellers/{id}", response.Headers.Location?.ToString());
        Assert.Equal("charlie", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("active", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("accountCount").GetInt32());

        var stored = await world.AccountsAsync(async accounts =>
        {
            return await accounts.Resellers.AsNoTracking().SingleAsync(reseller => reseller.Id == id);
        });
        Assert.Equal("charlie@example.com", stored.ContactEmail);
        Assert.Equal("ru", stored.Language);
        Assert.Equal(ResellerStatus.Active, stored.Status);
        Assert.Equal(3, stored.Envelope.MaxAccounts);
        Assert.Equal(15_360, stored.Envelope.DiskPoolMb);

        var login = await world.IdentityAsync(async identity =>
        {
            return await identity.Users.AsNoTracking().SingleAsync(user => user.ResellerId == id);
        });
        Assert.Equal(UserRole.Reseller, login.Role);
        Assert.Equal("charlie", login.Username);
        Assert.Equal("charlie@example.com", login.Email);
        Assert.True(login.HasPendingInvitation);

        var mail = await SubUserTeamWorld.MailToAsync(world.Mailer, "charlie@example.com");
        Assert.False(string.IsNullOrWhiteSpace(SubUserTeamWorld.TokenIn(mail)));

        var created = Assert.Single(await world.AuditEntriesAsync(AccountAuditActions.ResellerCreated));
        Assert.Equal(("admin", "charlie", true), (created.ActorUsername, created.Subject, created.Succeeded));
        var invited = Assert.Single(await world.AuditEntriesAsync(IdentityAuditActions.ResellerInvited));
        Assert.Equal(("charlie", true), (invited.Subject, invited.Succeeded));
    }

    /// <summary>
    /// A name another reseller or an account already holds is refused with 409 <c>ResellerNameTaken</c>,
    /// in both cases with the same answer, so the refusal does not say which kind of tenant holds it;
    /// nothing is stored and the refusal is journalled.
    /// </summary>
    /// <param name="name">A name a reseller (<c>alpha</c>) or an account (<c>house</c>) holds.</param>
    [Theory]
    [InlineData("alpha")]
    [InlineData("house")]
    public async Task A_reseller_name_that_is_taken_is_refused_with_conflict_whoever_holds_it(string name)
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");

        var response = await admin.PostAsJsonAsync("/api/v1/resellers", ResellerRequestBodies.Reseller(name));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ResellerNameTaken", await ProblemDocument.CodeAsync(response));
        Assert.Equal(2, await world.AccountsAsync(async accounts => { return await accounts.Resellers.CountAsync(); }));
        var refused = Assert.Single(await world.AuditEntriesAsync(AccountAuditActions.ResellerCreated));
        Assert.Equal(("admin", name, false), (refused.ActorUsername, refused.Subject, refused.Succeeded));
    }

    /// <summary>
    /// A creation the validator refuses answers 400 with the code of the figure that failed and stores
    /// nothing: a name outside the alphabet, a reserved name, an unshipped language, a negative figure.
    /// </summary>
    /// <param name="name">The reseller's name.</param>
    /// <param name="language">The contact's language.</param>
    /// <param name="maxAccounts">The envelope's account count.</param>
    /// <param name="code">The refusal expected.</param>
    [Theory]
    [InlineData("Bad-Name", "en", 3, "ResellerNameInvalid")]
    [InlineData("ab", "en", 3, "ResellerNameInvalid")]
    [InlineData("root", "en", 3, "ResellerNameInvalid")]
    [InlineData("charlie", "de", 3, "AccountOwnerLanguageUnsupported")]
    [InlineData("charlie", "en", -1, "ResellerEnvelopeInvalid")]
    public async Task A_creation_the_validator_refuses_answers_bad_request_and_stores_nothing(
        string name,
        string language,
        int maxAccounts,
        string code)
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");

        var response = await admin.PostAsJsonAsync(
            "/api/v1/resellers",
            ResellerRequestBodies.Reseller(name, language, new ResellerEnvelopeSpec(MaxAccounts: maxAccounts)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await ProblemDocument.CodeAsync(response));
        Assert.Equal(2, await world.AccountsAsync(async accounts => { return await accounts.Resellers.CountAsync(); }));
        Assert.Empty(await world.AuditEntriesAsync(AccountAuditActions.ResellerCreated));
    }

    /// <summary>
    /// The list and the detail say what each reseller owns — accounts, plans and the disk the accounts
    /// are on — and its envelope; search and the state filter narrow the list on the server, the picker
    /// lists names, and a reseller that does not exist is 404 <c>ResellerNotFound</c>.
    /// </summary>
    [Fact]
    public async Task The_list_and_detail_state_what_each_reseller_owns_and_an_unknown_one_is_not_found()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");

        using var list = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/resellers"));
        Assert.Equal(2, list.RootElement.GetProperty("total").GetInt32());
        var alpha = list.RootElement.GetProperty("items").EnumerateArray()
            .Single(row => { return row.GetProperty("name").GetString() == "alpha"; });
        Assert.Equal(world.Alpha.ResellerId, alpha.GetProperty("id").GetGuid());
        Assert.Equal(1, alpha.GetProperty("accountCount").GetInt32());
        Assert.Equal(1, alpha.GetProperty("planCount").GetInt32());
        Assert.Equal(5_120, alpha.GetProperty("diskAllocatedMb").GetInt64());
        Assert.Equal("active", alpha.GetProperty("status").GetString());

        using var searched = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/resellers?search=alp"));
        Assert.Equal(
            ["alpha"],
            searched.RootElement.GetProperty("items").EnumerateArray().Select(row =>
            {
                return row.GetProperty("name").GetString();
            }));
        using var suspended = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/resellers?status=suspended"));
        Assert.Equal(0, suspended.RootElement.GetProperty("total").GetInt32());
        using var choices = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/resellers/choices"));
        Assert.Equal(
            ["alpha", "bravo"],
            choices.RootElement.GetProperty("items").EnumerateArray().Select(row =>
            {
                return row.GetProperty("name").GetString();
            }).Order());

        using var detail = JsonDocument.Parse(await admin.GetStringAsync($"/api/v1/resellers/{world.Alpha.ResellerId}"));
        var envelope = detail.RootElement.GetProperty("envelope");
        Assert.Equal(3, envelope.GetProperty("maxAccounts").GetInt32());
        Assert.Equal(15_360, envelope.GetProperty("diskPoolMb").GetInt64());
        Assert.Equal(
            ResellerEnvelopeSpec.SoldFeatureNames.Order(),
            envelope.GetProperty("features").EnumerateArray().Select(feature =>
            {
                return feature.GetString()!;
            }).Order());

        var unknown = await admin.GetAsync($"/api/v1/resellers/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("ResellerNotFound", await ProblemDocument.CodeAsync(unknown));
    }

    /// <summary>
    /// An administrator replaces a reseller's envelope and the change is stored and journalled. Lowering
    /// it below what the reseller already owns is accepted and edits nothing the reseller already has.
    /// </summary>
    [Fact]
    public async Task Changing_an_envelope_is_stored_and_journalled_and_may_sit_below_what_the_reseller_owns()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");
        var path = $"/api/v1/resellers/{world.Alpha.ResellerId}/envelope";

        var raised = await admin.PutAsJsonAsync(path, new { Envelope = new ResellerEnvelopeSpec(MaxAccounts: 7).ToBody() });
        var lowered = await admin.PutAsJsonAsync(
            path,
            new { Envelope = new ResellerEnvelopeSpec(MaxAccounts: 0, DiskPoolMb: 0, MaxPlans: 0).ToBody() });

        Assert.Equal(HttpStatusCode.OK, raised.StatusCode);
        using var raisedBody = JsonDocument.Parse(await raised.Content.ReadAsStringAsync());
        Assert.Equal(7, raisedBody.RootElement.GetProperty("envelope").GetProperty("maxAccounts").GetInt32());
        Assert.Equal(HttpStatusCode.OK, lowered.StatusCode);
        var stored = await world.AccountsAsync(async accounts =>
        {
            return await accounts.Resellers.AsNoTracking().SingleAsync(reseller => reseller.Id == world.Alpha.ResellerId);
        });
        Assert.Equal((0, 0L, 0), (stored.Envelope.MaxAccounts, stored.Envelope.DiskPoolMb, stored.Envelope.MaxPlans));
        Assert.Equal(1, await world.AccountsAsync(async accounts =>
        {
            return await accounts.Accounts.CountAsync(account => account.ResellerId == world.Alpha.ResellerId);
        }));
        var entries = await world.AuditEntriesAsync(AccountAuditActions.ResellerEnvelopeChanged);
        Assert.Equal(
            [("admin", "alpha", true), ("admin", "alpha", true)],
            entries.Select(entry => { return (entry.ActorUsername, entry.Subject, entry.Succeeded); }));
    }

    /// <summary>
    /// Changing the envelope of a reseller that does not exist answers 404 <c>ResellerNotFound</c> and
    /// the refusal is journalled under the identifier the caller supplied.
    /// </summary>
    [Fact]
    public async Task Changing_the_envelope_of_an_unknown_reseller_is_not_found_and_journalled()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");
        var unknown = Guid.NewGuid();

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/resellers/{unknown}/envelope", new { Envelope = new ResellerEnvelopeSpec().ToBody() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ResellerNotFound", await ProblemDocument.CodeAsync(response));
        var refused = Assert.Single(await world.AuditEntriesAsync(AccountAuditActions.ResellerEnvelopeChanged));
        Assert.False(refused.Succeeded);
        Assert.Contains(unknown.ToString(), refused.Subject, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A reseller reads its own standing — name, state, what it owns and the envelope it was sold — from
    /// <c>/api/v1/reseller/me</c>; an administrator is refused that route with 403 and an anonymous caller
    /// with 401.
    /// </summary>
    [Fact]
    public async Task A_reseller_reads_its_own_standing_and_an_administrator_is_refused_that_route()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        using var admin = await world.SignInAsync("admin");
        using var anonymous = world.Factory.CreateClient();

        using var me = JsonDocument.Parse(await alpha.GetStringAsync("/api/v1/reseller/me"));

        Assert.Equal(world.Alpha.ResellerId, me.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("alpha", me.RootElement.GetProperty("name").GetString());
        Assert.Equal(1, me.RootElement.GetProperty("accountCount").GetInt32());
        Assert.Equal(1, me.RootElement.GetProperty("planCount").GetInt32());
        Assert.Equal(5_120, me.RootElement.GetProperty("diskAllocatedMb").GetInt64());
        Assert.Equal(3, me.RootElement.GetProperty("envelope").GetProperty("maxAccounts").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/reseller/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/reseller/me")).StatusCode);
    }
}

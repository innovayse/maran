using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// An administrator renames a reseller and changes its contact over real HTTP against a real
/// PostgreSQL (issue #373): the row and the login it signs in with move together, a name any tenant
/// holds is refused, a refused change leaves both untouched, and every outcome is journalled.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class ResellerUpdateEndpointTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ResellerUpdateEndpointTests(PostgresFixture postgres)
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
    /// A change answers 200 with the reseller as it now stands, stores the new name, address and
    /// language, moves the reseller's login to the new name and address, and is journalled under the
    /// name the reseller had.
    /// </summary>
    [Fact]
    public async Task Renaming_a_reseller_moves_its_row_and_its_login_and_is_journalled()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/resellers/{world.Alpha.ResellerId}",
            new { Name = "alpha2", ContactEmail = "alpha2@example.com", Language = "ru" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("alpha2", body.RootElement.GetProperty("name").GetString());
        var stored = await world.AccountsAsync(async accounts =>
        {
            return await accounts.Resellers.IgnoreQueryFilters().AsNoTracking().SingleAsync(reseller => reseller.Id == world.Alpha.ResellerId);
        });
        Assert.Equal(("alpha2", "alpha2@example.com", "ru"), (stored.Name, stored.ContactEmail, stored.Language));
        var login = await world.IdentityAsync(async identity =>
        {
            return await identity.Users.AsNoTracking().SingleAsync(user => user.ResellerId == world.Alpha.ResellerId);
        });
        Assert.Equal(("alpha2", "alpha2@example.com"), (login.Username, login.Email));
        var entry = Assert.Single(await world.AuditEntriesAsync(AccountAuditActions.ResellerUpdated));
        Assert.Equal(("admin", "alpha", true), (entry.ActorUsername, entry.Subject, entry.Succeeded));
    }

    /// <summary>
    /// A name an account holds is refused with 409 <c>ResellerNameTaken</c>; the row and the login keep
    /// their name and the refusal is journalled.
    /// </summary>
    [Fact]
    public async Task A_name_an_account_holds_is_refused_and_changes_neither_the_row_nor_the_login()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/resellers/{world.Alpha.ResellerId}",
            new { Name = "house", ContactEmail = "alpha@example.com", Language = "en" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ResellerNameTaken", await ProblemDocument.CodeAsync(response));
        var stored = await world.AccountsAsync(async accounts =>
        {
            return await accounts.Resellers.IgnoreQueryFilters().AsNoTracking().SingleAsync(reseller => reseller.Id == world.Alpha.ResellerId);
        });
        Assert.Equal("alpha", stored.Name);
        var login = await world.IdentityAsync(async identity =>
        {
            return await identity.Users.AsNoTracking().SingleAsync(user => user.ResellerId == world.Alpha.ResellerId);
        });
        Assert.Equal("alpha", login.Username);
        var refused = Assert.Single(await world.AuditEntriesAsync(AccountAuditActions.ResellerUpdated));
        Assert.False(refused.Succeeded);
    }

    /// <summary>A change the validator refuses answers 400 with the code of the field that failed and stores nothing.</summary>
    /// <param name="name">The new name.</param>
    /// <param name="language">The new language.</param>
    /// <param name="code">The code the refusal carries.</param>
    [Theory]
    [InlineData("Bad Name", "en", "ResellerNameInvalid")]
    [InlineData("root", "en", "ResellerNameInvalid")]
    [InlineData("alpha2", "xx", "AccountOwnerLanguageUnsupported")]
    public async Task A_change_the_validator_refuses_answers_bad_request_and_stores_nothing(
        string name,
        string language,
        string code)
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/resellers/{world.Alpha.ResellerId}",
            new { Name = name, ContactEmail = "alpha@example.com", Language = language });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await ProblemDocument.CodeAsync(response));
        Assert.Empty(await world.AuditEntriesAsync(AccountAuditActions.ResellerUpdated));
    }

    /// <summary>Changing a reseller that does not exist answers 404 <c>ResellerNotFound</c> and journals the refusal.</summary>
    [Fact]
    public async Task Changing_an_unknown_reseller_is_not_found_and_journalled()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/resellers/{Guid.NewGuid()}",
            new { Name = "alpha2", ContactEmail = "alpha2@example.com", Language = "en" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ResellerNotFound", await ProblemDocument.CodeAsync(response));
        Assert.False(Assert.Single(await world.AuditEntriesAsync(AccountAuditActions.ResellerUpdated)).Succeeded);
    }
}

using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The owner-language list over real HTTP against a real PostgreSQL (issue #330): the create-account
/// form offers exactly the languages the create endpoint accepts, named in themselves, and the list
/// is the administrator's to read.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class AccountOwnerLanguagesEndpointTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AccountOwnerLanguagesEndpointTests(PostgresFixture postgres)
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

    /// <summary>The administrator reads every shipped language, each named in itself; a customer is refused.</summary>
    [Fact]
    public async Task The_owner_language_list_names_every_shipped_language_for_the_administrator_only()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        await EndpointHarness.SeedCustomerAsync(factory, "olive", accountId);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var customer = await EndpointHarness.SignInAsync(factory, "olive");

        var listed = await admin.GetAsync("/api/v1/accounts/owner-languages");
        var refused = await customer.GetAsync("/api/v1/accounts/owner-languages");

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using var body = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
        var rows = body.RootElement.EnumerateArray().ToList();
        Assert.Equal(
            ["en", "ru", "hy"],
            rows.Select(row =>
            {
                return row.GetProperty("language").GetString();
            }));
        Assert.Equal(
            ["English", "Русский", "Հայերեն"],
            rows.Select(row =>
            {
                return row.GetProperty("languageDisplayName").GetString();
            }));
    }
}

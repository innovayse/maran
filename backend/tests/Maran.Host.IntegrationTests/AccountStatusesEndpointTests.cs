using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The account-state list over real HTTP against a real PostgreSQL (issue #330): the accounts list's
/// filter, its chips and every badge print the names the panel produces, so the list is readable by
/// any signed-in caller (a customer's own account page prints its state too) and by nobody else.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class AccountStatusesEndpointTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AccountStatusesEndpointTests(PostgresFixture postgres)
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
    /// The administrator and a customer each read both states, named; a caller who is not signed in is
    /// refused. The set is a closed vocabulary tied to no row, so there is no other account's row to
    /// hide (rules/security.md item 6).
    /// </summary>
    [Fact]
    public async Task The_account_status_list_names_both_states_for_any_signed_in_caller_and_no_one_else()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        await EndpointHarness.SeedCustomerAsync(factory, "olive", accountId);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var customer = await EndpointHarness.SignInAsync(factory, "olive");
        using var anonymous = factory.CreateClient();

        var listed = await admin.GetAsync("/api/v1/accounts/statuses");
        var listedForCustomer = await customer.GetAsync("/api/v1/accounts/statuses");
        var refused = await anonymous.GetAsync("/api/v1/accounts/statuses");

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listedForCustomer.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        foreach (var response in new[] { listed, listedForCustomer })
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var rows = body.RootElement.EnumerateArray().ToList();
            Assert.Equal(
                ["active", "suspended"],
                rows.Select(row =>
                {
                    return row.GetProperty("status").GetString();
                }));
            Assert.Equal(
                ["Active", "Suspended"],
                rows.Select(row =>
                {
                    return row.GetProperty("statusName").GetString();
                }));
        }
    }
}

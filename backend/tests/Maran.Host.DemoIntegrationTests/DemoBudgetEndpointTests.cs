using System.Net;
using System.Text.Json;
using Maran.Host.DemoIntegrationTests.Fixtures;

namespace Maran.Host.DemoIntegrationTests;

/// <summary>The mutation budgets are enforced over HTTP, and getting in is never counted.</summary>
[Collection(DemoSharedDatabase.Name)]
public sealed class DemoBudgetEndpointTests
{
    /// <summary>An allowed, counted mutation that needs no body.</summary>
    private const string CountedRoute = "/api/v1/notifications/feed/dismiss-all";

    /// <summary>The shared server.</summary>
    private readonly DemoPostgres _postgres;

    /// <summary>Creates the class over the shared server.</summary>
    /// <param name="postgres">The shared server.</param>
    public DemoBudgetEndpointTests(DemoPostgres postgres)
    {
        _postgres = postgres;
    }

    /// <summary>Builds small budgets for a host.</summary>
    /// <param name="global">The global budget.</param>
    /// <param name="perVisitor">Mutations per visitor window.</param>
    /// <returns>The settings.</returns>
    private static Dictionary<string, string> Budgets(int global, int perVisitor)
    {
        return new Dictionary<string, string>
        {
            ["Demo:GlobalMutationBudget"] = global.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Demo:VisitorMutationsPerWindow"] = perVisitor.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Demo:VisitorWindowSeconds"] = "3600",
        };
    }

    /// <summary>A visitor past their own rate gets 429 with Retry-After; their earlier calls ran.</summary>
    [Fact]
    public async Task A_visitor_over_their_rate_is_answered_429_with_retry_after()
    {
        await using var demo = await SeededDemo.StartAsync(_postgres, Budgets(global: 100, perVisitor: 2));
        using var visitor = await demo.SignInAsync("198.51.100.1");

        var first = await visitor.PostAsync(CountedRoute, null);
        var second = await visitor.PostAsync(CountedRoute, null);
        var third = await visitor.PostAsync(CountedRoute, null);

        Assert.True(first.IsSuccessStatusCode);
        Assert.True(second.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.True(third.Headers.RetryAfter is not null);
    }

    /// <summary>When the whole budget is spent another visitor is answered 409, and sign-in still works.</summary>
    [Fact]
    public async Task A_spent_global_budget_refuses_everyone_but_never_sign_in()
    {
        await using var demo = await SeededDemo.StartAsync(_postgres, Budgets(global: 2, perVisitor: 2));
        using var spender = await demo.SignInAsync("198.51.100.1");
        await spender.PostAsync(CountedRoute, null);
        await spender.PostAsync(CountedRoute, null);

        using var latecomer = await demo.SignInAsync("198.51.100.2");
        var refused = await latecomer.PostAsync(CountedRoute, null);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Equal("DemoBudgetExhausted", body.RootElement.GetProperty("code").GetString());
    }
}

using System.Net;
using System.Text.Json;
using Maran.Host.Demo.Guard;
using Maran.Host.Demo.Options;
using Maran.Host.Demo.Services;
using Maran.Host.Demo.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maran.Host.Demo.Tests.Guard;

/// <summary>The gate end to end over a bare HTTP context: what is answered, journaled and let through.</summary>
public sealed class DemoWriteGuardMiddlewareTests
{
    /// <summary>The address every test request comes from, standing in for a pseudonym.</summary>
    private static readonly IPAddress Visitor = IPAddress.Parse("2001:db8::1");

    /// <summary>One wired middleware and what it recorded.</summary>
    private sealed class Rig
    {
        /// <summary>The audit entries written.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>Whether the rest of the pipeline ran.</summary>
        public bool NextRan { get; private set; }

        /// <summary>The middleware under test.</summary>
        public DemoWriteGuardMiddleware Middleware { get; }

        /// <summary>The services a request resolves from.</summary>
        public IServiceProvider Services { get; }

        /// <summary>Wires the middleware over the planted table.</summary>
        /// <param name="options">The settings.</param>
        public Rig(DemoOptions options)
        {
            var wrapped = new OptionsWrapper<DemoOptions>(options);
            Services = new ServiceCollection().AddLogging().AddControllers().Services.BuildServiceProvider();
            Middleware = new DemoWriteGuardMiddleware(
                _ =>
                {
                    NextRan = true;
                    return Task.CompletedTask;
                },
                new DemoWriteGuard(DemoEndpoints.PlantedPolicy, new DemoProtectedResources()),
                new DemoMutationBudget(wrapped),
                new DemoSignInBudget(wrapped, new FixedClock()),
                new DemoAuditAllowance(wrapped),
                new FixedClock());
        }

        /// <summary>Sends one request through the middleware.</summary>
        /// <param name="method">The HTTP method.</param>
        /// <param name="action">The action method of the planted controller it reaches.</param>
        /// <returns>The context, with the response body ready to read.</returns>
        public async Task<DefaultHttpContext> SendAsync(string method, string action)
        {
            var context = new DefaultHttpContext { RequestServices = Services };
            context.Request.Method = method;
            context.Connection.RemoteIpAddress = Visitor;
            context.Response.Body = new MemoryStream();
            context.SetEndpoint(DemoEndpoints.ForAction(typeof(PlantedDemoController), action));

            await Middleware.InvokeAsync(context, new DemoAuditJournal(Audit, new AdministratorUser()));
            context.Response.Body.Position = 0;
            return context;
        }
    }

    /// <summary>Reads the JSON body of a response.</summary>
    /// <param name="context">The finished context.</param>
    /// <returns>The parsed body.</returns>
    private static async Task<JsonElement> BodyAsync(DefaultHttpContext context)
    {
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return document.RootElement.Clone();
    }

    /// <summary>A refused action answers 403 with its code and area, runs nothing and is journaled.</summary>
    [Fact]
    public async Task A_refused_action_is_answered_403_with_its_area_and_journaled()
    {
        var rig = new Rig(new DemoOptions());

        var context = await rig.SendAsync("POST", nameof(PlantedDemoController.RefusedAsync));
        var body = await BodyAsync(context);

        Assert.Equal(403, context.Response.StatusCode);
        Assert.Equal("DemoActionRefused", body.GetProperty("code").GetString());
        Assert.Equal("smtp", body.GetProperty("refusal").GetProperty("area").GetString());
        Assert.False(rig.NextRan);
        var entry = Assert.Single(rig.Audit.Entries);
        Assert.Equal("DemoActionRefused", entry.Action);
        Assert.Equal("PlantedDemoController.RefusedAsync (smtp)", entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>An allowed action runs and writes nothing to the journal.</summary>
    [Fact]
    public async Task An_allowed_action_runs_and_writes_no_journal_entry()
    {
        var rig = new Rig(new DemoOptions());

        var context = await rig.SendAsync("POST", nameof(PlantedDemoController.CountedAsync));

        Assert.True(rig.NextRan);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Empty(rig.Audit.Entries);
    }

    /// <summary>A read of a refused action is not judged.</summary>
    [Fact]
    public async Task A_read_of_a_refused_action_runs()
    {
        var rig = new Rig(new DemoOptions());

        await rig.SendAsync("GET", nameof(PlantedDemoController.RefusedAsync));

        Assert.True(rig.NextRan);
    }

    /// <summary>A refusal spends no budget: refusing a hundred times leaves the whole budget.</summary>
    [Fact]
    public async Task Refusals_spend_no_budget()
    {
        var rig = new Rig(new DemoOptions { GlobalMutationBudget = 1, VisitorMutationsPerWindow = 1 });

        for (var i = 0; i < 100; i++)
        {
            await rig.SendAsync("POST", nameof(PlantedDemoController.RefusedAsync));
        }

        await rig.SendAsync("POST", nameof(PlantedDemoController.CountedAsync));

        Assert.True(rig.NextRan);
    }

    /// <summary>Refusals are journaled only up to the allowance, but are all refused.</summary>
    [Fact]
    public async Task Refusals_beyond_the_audit_allowance_are_refused_but_not_journaled()
    {
        var rig = new Rig(new DemoOptions { AuditedRefusalsPerWindow = 3 });

        DefaultHttpContext? last = null;
        for (var i = 0; i < 10; i++)
        {
            last = await rig.SendAsync("POST", nameof(PlantedDemoController.RefusedAsync));
        }

        Assert.Equal(3, rig.Audit.Entries.Count);
        Assert.Equal(403, last!.Response.StatusCode);
    }

    /// <summary>A visitor over their rate gets 429 with Retry-After and the action does not run.</summary>
    [Fact]
    public async Task A_visitor_over_their_rate_is_answered_429_with_retry_after()
    {
        var rig = new Rig(new DemoOptions { VisitorMutationsPerWindow = 1, VisitorWindowSeconds = 60 });
        await rig.SendAsync("POST", nameof(PlantedDemoController.CountedAsync));

        var second = await rig.SendAsync("POST", nameof(PlantedDemoController.CountedAsync));

        Assert.Equal(429, second.Response.StatusCode);
        Assert.Equal("60", second.Response.Headers.RetryAfter.ToString());
        Assert.Equal("HostRateLimited", (await BodyAsync(second)).GetProperty("code").GetString());

        // The Host's one writer answers, so the media type is the product's, not a second spelling of it.
        Assert.StartsWith("application/problem+json", second.Response.ContentType);
    }

    /// <summary>The spent budget answers 409, and the exhaustion is journaled once however often it is hit.</summary>
    [Fact]
    public async Task A_spent_budget_is_answered_409_and_journaled_once()
    {
        var rig = new Rig(new DemoOptions { GlobalMutationBudget = 1, VisitorMutationsPerWindow = 1 });
        await rig.SendAsync("POST", nameof(PlantedDemoController.CountedAsync));

        DefaultHttpContext? last = null;
        for (var i = 0; i < 5; i++)
        {
            last = new DefaultHttpContext { RequestServices = rig.Services };
            last.Request.Method = "POST";
            last.Connection.RemoteIpAddress = IPAddress.Parse($"2001:db8::{10 + i}");
            last.Response.Body = new MemoryStream();
            last.SetEndpoint(
                DemoEndpoints.ForAction(typeof(PlantedDemoController), nameof(PlantedDemoController.CountedAsync)));
            await rig.Middleware.InvokeAsync(last, new DemoAuditJournal(rig.Audit, new AdministratorUser()));
        }

        last!.Response.Body.Position = 0;
        Assert.Equal(409, last.Response.StatusCode);
        Assert.Equal("DemoBudgetExhausted", (await BodyAsync(last)).GetProperty("code").GetString());
        var entry = Assert.Single(rig.Audit.Entries);
        Assert.Equal("DemoBudgetExhausted", entry.Action);
    }

    /// <summary>An uncounted action never draws on the budget, however often it is called.</summary>
    [Fact]
    public async Task An_uncounted_action_never_exhausts_the_budget()
    {
        var rig = new Rig(new DemoOptions { GlobalMutationBudget = 1, VisitorMutationsPerWindow = 1 });

        for (var i = 0; i < 20; i++)
        {
            await rig.SendAsync("POST", nameof(PlantedDemoController.OpenAsync));
        }

        var counted = await rig.SendAsync("POST", nameof(PlantedDemoController.CountedAsync));

        Assert.Equal(200, counted.Response.StatusCode);
    }

    /// <summary>
    /// Sign-ins are bounded apart from the mutation budget: a visitor over their allowance is answered 429 and the
    /// action does not run, while a counted mutation by the same visitor still runs (F-6).
    /// </summary>
    [Fact]
    public async Task A_visitor_over_their_sign_in_allowance_is_answered_429_and_their_mutations_still_run()
    {
        var rig = new Rig(new DemoOptions { VisitorSignInsPerWindow = 2, GlobalSignInsPerWindow = 100 });
        await rig.SendAsync("POST", nameof(PlantedDemoController.SignInAsync));
        await rig.SendAsync("POST", nameof(PlantedDemoController.SignInAsync));

        var third = await rig.SendAsync("POST", nameof(PlantedDemoController.SignInAsync));
        var counted = await rig.SendAsync("POST", nameof(PlantedDemoController.CountedAsync));

        Assert.Equal(429, third.Response.StatusCode);
        Assert.Equal("HostRateLimited", (await BodyAsync(third)).GetProperty("code").GetString());
        Assert.Equal(200, counted.Response.StatusCode);
    }

    /// <summary>The control: within the allowance a sign-in runs and spends nothing of the mutation budget.</summary>
    [Fact]
    public async Task A_sign_in_within_the_allowance_runs_and_spends_no_mutation_budget()
    {
        var rig = new Rig(new DemoOptions { GlobalMutationBudget = 1, VisitorMutationsPerWindow = 1 });

        var signIn = await rig.SendAsync("POST", nameof(PlantedDemoController.SignInAsync));
        var counted = await rig.SendAsync("POST", nameof(PlantedDemoController.CountedAsync));

        Assert.Equal(200, signIn.Response.StatusCode);
        Assert.Equal(200, counted.Response.StatusCode);
    }
}

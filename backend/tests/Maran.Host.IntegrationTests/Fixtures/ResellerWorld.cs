using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The panel the reseller endpoint tests run against, booted and seeded by
/// <see cref="ResellerFixtureBuilder"/>: an administrator, two resellers that each own a plan and an
/// account on it, and an administrator-owned plan and account beside them.
/// </summary>
/// <remarks>
/// <para>
/// Every login has the password <see cref="EndpointHarness.Password"/> and the address
/// <c>&lt;username&gt;@example.com</c>. The administrator is <c>admin</c>; the resellers are
/// <see cref="Alpha"/> (<c>alpha</c>, plan <c>AlphaPlan</c>, account <c>alphaone</c>) and
/// <see cref="Bravo"/> (<c>bravo</c>, plan <c>BravoPlan</c>, account <c>bravoone</c>); the
/// administrator's own are the plan <see cref="HousePlanName"/> and the account
/// <see cref="HouseAccountName"/>. No customer login exists unless a test seeds one.
/// </para>
/// <para>
/// Reads go through the module contexts of a fresh scope. That scope has no request, so the caller is
/// anonymous and the reseller filters do not narrow it: a test reads what is stored, not what a
/// reseller may see.
/// </para>
/// </remarks>
public sealed class ResellerWorld : IAsyncDisposable
{
    /// <summary>The name of the plan the administrator owns.</summary>
    public const string HousePlanName = "HousePlan";

    /// <summary>The name of the account the administrator owns.</summary>
    public const string HouseAccountName = "house";

    /// <summary>The booted host.</summary>
    public WebApplicationFactory<Program> Factory { get; }

    /// <summary>The mail server's double, which holds every message the panel sent.</summary>
    public RecordingMailer Mailer { get; }

    /// <summary>The agent double the panel's account operations were pointed at.</summary>
    public ResellerAgentAccountsClient Agent { get; }

    /// <summary>The first reseller.</summary>
    public ResellerSeed Alpha { get; }

    /// <summary>The second reseller.</summary>
    public ResellerSeed Bravo { get; }

    /// <summary>The id of the plan the administrator owns.</summary>
    public Guid HousePlanId { get; }

    /// <summary>The id of the account the administrator owns, which is on <see cref="HousePlanId"/>.</summary>
    public Guid HouseAccountId { get; }

    /// <summary>Wraps a booted, seeded host.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="mailer">The mail server's double.</param>
    /// <param name="agent">The agent double.</param>
    /// <param name="alpha">The first reseller.</param>
    /// <param name="bravo">The second reseller.</param>
    /// <param name="housePlanId">The administrator's plan.</param>
    /// <param name="houseAccountId">The administrator's account.</param>
    public ResellerWorld(
        WebApplicationFactory<Program> factory,
        RecordingMailer mailer,
        ResellerAgentAccountsClient agent,
        ResellerSeed alpha,
        ResellerSeed bravo,
        Guid housePlanId,
        Guid houseAccountId)
    {
        Factory = factory;
        Mailer = mailer;
        Agent = agent;
        Alpha = alpha;
        Bravo = bravo;
        HousePlanId = housePlanId;
        HouseAccountId = houseAccountId;
    }

    /// <summary>Finds a seeded reseller by name.</summary>
    /// <param name="name">Either <c>alpha</c> or <c>bravo</c>.</param>
    /// <returns>That reseller's seed.</returns>
    public ResellerSeed Reseller(string name)
    {
        return name switch
        {
            "alpha" => Alpha,
            "bravo" => Bravo,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "The world seeds alpha and bravo."),
        };
    }

    /// <summary>Signs the named login in and returns a client carrying its access token.</summary>
    /// <param name="username">The login name.</param>
    /// <returns>A client the caller disposes.</returns>
    public Task<HttpClient> SignInAsync(string username)
    {
        return EndpointHarness.SignInAsync(Factory, username);
    }

    /// <summary>Runs a read or a write against the Accounts context of a fresh, unnarrowed scope.</summary>
    /// <typeparam name="T">What the work returns.</typeparam>
    /// <param name="work">The work, given the context.</param>
    /// <returns>What the work returned.</returns>
    public async Task<T> AccountsAsync<T>(Func<AccountsDbContext, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AccountsDbContext>());
    }

    /// <summary>Runs a read or a write against the Identity context of a fresh scope.</summary>
    /// <typeparam name="T">What the work returns.</typeparam>
    /// <param name="work">The work, given the context.</param>
    /// <returns>What the work returned.</returns>
    public async Task<T> IdentityAsync<T>(Func<IdentityDbContext, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<IdentityDbContext>());
    }

    /// <summary>Reads every journal entry whose action is one of <paramref name="actions"/>, oldest first.</summary>
    /// <param name="actions">The action names to keep.</param>
    /// <returns>The matching entries.</returns>
    public Task<IReadOnlyList<AuditEvent>> AuditEntriesAsync(params string[] actions)
    {
        return SubUserTeamWorld.AuditEntriesAsync(Factory, actions);
    }

    /// <summary>Releases the host.</summary>
    /// <returns>A task that completes when the host is disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
    }
}

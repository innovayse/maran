using Maran.Agent.Client.Interfaces;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Notifications.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Boots the panel and seeds the world the reseller endpoint tests share (<see cref="ResellerWorld"/>),
/// letting a test choose only what it has to: the agent double, alpha's envelope and whether every
/// module's schema exists.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reseller sign-in is not the thing under test here, so the second factor is off.</b> A reseller's
/// two-factor enrolment is mandatory by default, and a token issued before enrolment is refused
/// everywhere except the enrolment endpoints. The builder stores a security policy that does not force
/// it, so a seeded reseller signs in to a working token; the forced-enrolment behaviour has its own
/// tests in the Identity area.
/// </para>
/// <para>
/// A reseller's login is stored directly (an invited login, then activated with the shared password)
/// rather than through the invitation link, because the invitation flow has tests of its own and a
/// link round-trip per world would be paid by every test in this area.
/// </para>
/// </remarks>
public sealed class ResellerFixtureBuilder
{
    /// <summary>The panel address configured so an invitation mail carries a link with a token in it.</summary>
    private const string PanelUrl = "https://panel.example.com";

    /// <summary>The test's own database.</summary>
    private readonly TestDatabase _database;

    /// <summary>The agent double the panel's account operations are pointed at.</summary>
    private ResellerAgentAccountsClient _agent = new();

    /// <summary>What alpha was sold.</summary>
    private ResellerEnvelopeSpec _alphaEnvelope = new();

    /// <summary>Whether every module's schema is created, not only the ones the account surface needs.</summary>
    private bool _everyModule;

    /// <summary>Starts a builder against one test's database.</summary>
    /// <param name="database">The test's own database.</param>
    public ResellerFixtureBuilder(TestDatabase database)
    {
        _database = database;
    }

    /// <summary>Points the panel's account operations at <paramref name="agent"/>.</summary>
    /// <param name="agent">The agent double to use.</param>
    /// <returns>This builder.</returns>
    public ResellerFixtureBuilder WithAgent(ResellerAgentAccountsClient agent)
    {
        _agent = agent;
        return this;
    }

    /// <summary>Sells alpha <paramref name="envelope"/> instead of the default envelope.</summary>
    /// <param name="envelope">What alpha is sold.</param>
    /// <returns>This builder.</returns>
    public ResellerFixtureBuilder WithAlphaEnvelope(ResellerEnvelopeSpec envelope)
    {
        _alphaEnvelope = envelope;
        return this;
    }

    /// <summary>
    /// Creates every module's schema and stands in the cron agent, for a test that reads what the
    /// resource counters of other modules answer.
    /// </summary>
    /// <returns>This builder.</returns>
    public ResellerFixtureBuilder WithEveryModuleSchema()
    {
        _everyModule = true;
        return this;
    }

    /// <summary>Boots the panel, applies the migrations and seeds the world.</summary>
    /// <returns>The world, which the caller disposes.</returns>
    public async Task<ResellerWorld> BuildAsync()
    {
        var mailer = new RecordingMailer(TimeSpan.Zero);
        var factory = EndpointHarness.CreateFactory(
                _database,
                services =>
                {
                    services.AddSingleton<IMailer>(mailer);
                    services.RemoveAll<IAgentAccountsClient>();
                    services.AddSingleton<IAgentAccountsClient>(_agent);
                    if (_everyModule)
                    {
                        services.AddSingleton<IAgentCronClient>(new StubAgentCronClient());
                    }
                })
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("PasswordReset:PanelUrl", PanelUrl);

                // A test makes dozens of requests as one login; the limit is not what it is about.
                builder.UseSetting("RateLimiting:ApiPermitLimit", "100000");
            });

        await MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        await SeedPolicyAsync(factory);
        var (housePlanId, houseAccountId) = await SeedHouseAsync(factory);
        var alpha = await SeedResellerAsync(factory, "alpha", _alphaEnvelope);
        var bravo = await SeedResellerAsync(factory, "bravo", new ResellerEnvelopeSpec());

        return new ResellerWorld(factory, mailer, _agent, alpha, bravo, housePlanId, houseAccountId);
    }

    /// <summary>Creates the schemas the world needs.</summary>
    /// <param name="factory">The booted host.</param>
    private async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        if (!_everyModule)
        {
            await EndpointHarness.MigrateAsync(factory);
            return;
        }

        using var scope = factory.Services.CreateScope();
        await ModuleSchemas.MigrateAsync(scope.ServiceProvider);
    }

    /// <summary>Stores a security policy that does not force a second factor on anyone.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task SeedPolicyAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        identity.SecurityPolicies.Add(new SecurityPolicy(
            SecurityPolicy.DefaultMinimumPasswordLength,
            forceTwoFactorForAdmins: false,
            forceTwoFactorForResellers: false,
            SecurityPolicy.DefaultMaxFailedLoginAttempts,
            SecurityPolicy.DefaultLockoutMinutes,
            now));
        await identity.SaveChangesAsync();
    }

    /// <summary>Stores the administrator's own plan and account.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The plan's id and the account's id.</returns>
    private static async Task<(Guid PlanId, Guid AccountId)> SeedHouseAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var plan = Plan.Define(Guid.NewGuid(), ResellerWorld.HousePlanName, 5_120, 5, 2, 3, 1, 2, 4);
        var account = new Account(
            Guid.NewGuid(),
            ResellerWorld.HouseAccountName,
            $"{ResellerWorld.HouseAccountName}.example.com",
            plan.Id,
            now,
            ownerEmail: $"{ResellerWorld.HouseAccountName}@example.com");
        accounts.Plans.Add(plan);
        accounts.Accounts.Add(account);
        await accounts.SaveChangesAsync();

        return (plan.Id, account.Id);
    }

    /// <summary>Stores a reseller, its active login, one plan it owns and one account on that plan.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="name">The reseller's name, also its login name; the plan is <c>&lt;Name&gt;Plan</c>, the account <c>&lt;name&gt;one</c>.</param>
    /// <param name="envelope">What the reseller is sold.</param>
    /// <returns>The seed that names what was stored.</returns>
    private static async Task<ResellerSeed> SeedResellerAsync(
        WebApplicationFactory<Program> factory,
        string name,
        ResellerEnvelopeSpec envelope)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planName = $"{char.ToUpperInvariant(name[0])}{name[1..]}Plan";
        var accountName = $"{name}one";
        var reseller = new Reseller(Guid.NewGuid(), name, $"{name}@example.com", "en", envelope.ToDomain(), now);
        var plan = Plan.Define(
            Guid.NewGuid(),
            planName,
            5_120,
            5,
            2,
            3,
            1,
            2,
            4,
            ResellerEnvelopeSpec.SoldFeatures,
            resellerId: reseller.Id);
        var account = new Account(
            Guid.NewGuid(),
            accountName,
            $"{accountName}.example.com",
            plan.Id,
            now,
            ownerEmail: $"{accountName}@example.com",
            resellerId: reseller.Id);
        accounts.Resellers.Add(reseller);
        accounts.Plans.Add(plan);
        accounts.Accounts.Add(account);
        await accounts.SaveChangesAsync();

        var login = User.InviteReseller(Guid.NewGuid(), name, $"{name}@example.com", reseller.Id, now);
        login.Activate(hasher.Hash(EndpointHarness.Password));
        identity.Users.Add(login);
        await identity.SaveChangesAsync();

        return new ResellerSeed(reseller.Id, login.Id, name, plan.Id, planName, account.Id, accountName);
    }
}

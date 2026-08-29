using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Modules.Accounts.Domain;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.CreateAccount;

/// <summary>
/// Behavioral contract of <see cref="CreateAccountCommandHandler"/>. Runs against a real
/// <see cref="AccountsDbContext"/> backed by the EF Core InMemory provider — the handler's own
/// dependency, not a hand-rolled repository double — so the query logic (name/domain uniqueness
/// checks) is exercised as written; each test gets its own uniquely-named database so tests never
/// share state (rules/testing.md "Determinism").
/// </summary>
public sealed class CreateAccountCommandHandlerTests
{
    /// <summary>Builds a fresh, isolated in-memory <see cref="AccountsDbContext"/>.</summary>
    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    [Fact]
    public async Task Creating_account_with_an_unused_name_and_domain_inserts_the_row()
    {
        await using var dbContext = CreateDbContext();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var handler = new CreateAccountCommandHandler(dbContext, clock);
        var planId = Guid.NewGuid();

        var result = await handler.Handle(new CreateAccountCommand("acme", "acme.example.com", planId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("acme", result.Value.Name);
        Assert.Equal("acme.example.com", result.Value.PrimaryDomain);
        Assert.Equal(planId, result.Value.PlanId);
        Assert.Equal(AccountStatus.Active, result.Value.Status);
        Assert.Equal(clock.UtcNow, result.Value.CreatedAt);

        var stored = await dbContext.Accounts.SingleAsync();
        Assert.Equal("acme", stored.Name);
    }

    [Fact]
    public async Task Creating_account_with_a_taken_name_returns_the_typed_name_taken_error_without_throwing()
    {
        await using var dbContext = CreateDbContext();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        dbContext.Accounts.Add(new Account(Guid.NewGuid(), "acme", "existing.example.com", Guid.NewGuid(), clock.UtcNow));
        await dbContext.SaveChangesAsync();
        var handler = new CreateAccountCommandHandler(dbContext, clock);

        var result = await handler.Handle(
            new CreateAccountCommand("acme", "different.example.com", Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNameTaken", result.Error!.Code);
    }

    [Fact]
    public async Task Creating_account_with_a_taken_domain_returns_the_typed_domain_taken_error_without_throwing()
    {
        await using var dbContext = CreateDbContext();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        dbContext.Accounts.Add(new Account(Guid.NewGuid(), "existing", "acme.example.com", Guid.NewGuid(), clock.UtcNow));
        await dbContext.SaveChangesAsync();
        var handler = new CreateAccountCommandHandler(dbContext, clock);

        var result = await handler.Handle(
            new CreateAccountCommand("newname", "acme.example.com", Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountDomainTaken", result.Error!.Code);
    }

    [Fact]
    public void Handler_is_constructed_only_from_the_database_context_and_the_clock_never_an_agent_seam()
    {
        // Spec §8 / the module's own doc comments are explicit that row creation must not touch
        // the agent: no Accounts agent operations exist yet. Asserted here through the handler's
        // public constructor shape (its only public surface for dependencies), so this test would
        // fail the moment an agent dependency is added without a corresponding spec change.
        var constructor = Assert.Single(typeof(CreateAccountCommandHandler).GetConstructors());
        var parameterTypes = constructor.GetParameters().Select(p =>
        {
            return p.ParameterType;
        }).ToArray();

        Assert.Equal([typeof(AccountsDbContext), typeof(IClock)], parameterTypes);
    }
}

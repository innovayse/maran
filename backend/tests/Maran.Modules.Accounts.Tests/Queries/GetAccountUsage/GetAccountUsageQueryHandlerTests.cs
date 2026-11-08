using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Queries.GetAccountUsage;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Queries.GetAccountUsage;

/// <summary>
/// Behavioral contract of <see cref="GetAccountUsageQueryHandler"/>: the account's state, plan and disk
/// are read for the caller's own accounts only; a count is never a zero the caller was not given; and a
/// disk that could not be measured is the agent's typed failure, not a number.
/// </summary>
public sealed class GetAccountUsageQueryHandlerTests
{
    /// <summary>A key's own account is read with its state, plan, disk beside the quota, and every count unavailable.</summary>
    [Fact]
    public async Task A_keys_own_account_is_read_with_disk_and_every_count_unavailable()
    {
        var world = await TwoResellerWorld.SeedAsync();
        var agent = new RecordingAgentAccountsClient();
        agent.UsedBytes["alphaacct"] = 4_096;
        var sites = new StubAccountResourceCounter(AccountResourceCount.Counted(AccountResourceKind.Sites, 4));

        var result = await HandleAsync(world, FakeCurrentUser.ApiKey(world.Alpha.Id), world.AlphaAccount.Id, agent, sites);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(world.AlphaAccount.Id, result.Value.AccountId);
        Assert.Equal(AccountStatus.Active, result.Value.Status);
        Assert.Equal(world.AlphaPlan.Id, result.Value.PlanId);
        Assert.Equal(new AccountDiskUsageDto(4_096, 1_000UL * 1024 * 1024), result.Value.Disk);
        var row = Assert.Single(result.Value.Resources);
        Assert.Equal(new UnavailableResourceCountDto(), row.Count);
        Assert.Empty(sites.Asked);
        Assert.Equal(["usage:alphaacct"], agent.Calls);
    }

    /// <summary>The inverse control: an administrator asking about the same account is given the counts.</summary>
    [Fact]
    public async Task An_administrator_asking_about_the_same_account_is_given_the_counts()
    {
        var world = await TwoResellerWorld.SeedAsync();
        var sites = new StubAccountResourceCounter(AccountResourceCount.Counted(AccountResourceKind.Sites, 4));

        var result = await HandleAsync(
            world, FakeCurrentUser.Admin(), world.AlphaAccount.Id, new RecordingAgentAccountsClient(), sites);

        var row = Assert.Single(result.Value.Resources);
        Assert.Equal(new CountedResourceCountDto(4), row.Count);
    }

    /// <summary>Another reseller's account is not found, exactly like an id that does not exist, and the agent is never asked.</summary>
    [Fact]
    public async Task Another_resellers_account_is_not_found_and_the_agent_is_never_asked()
    {
        var world = await TwoResellerWorld.SeedAsync();
        var agent = new RecordingAgentAccountsClient();
        var sites = new StubAccountResourceCounter(AccountResourceCount.Counted(AccountResourceKind.Sites, 4));
        var key = FakeCurrentUser.ApiKey(world.Alpha.Id);

        var foreign = await HandleAsync(world, key, world.BetaAccount.Id, agent, sites);
        var unknown = await HandleAsync(world, key, Guid.NewGuid(), agent, sites);

        Assert.Equal("AccountNotFound", foreign.Error!.Code);
        Assert.Equal(ErrorType.NotFound, foreign.Error.Type);
        Assert.Equal(unknown.Error!.Code, foreign.Error.Code);
        Assert.Empty(agent.Calls);
        Assert.Empty(sites.Asked);
    }

    /// <summary>A key the administrator owns sees no account at all.</summary>
    [Fact]
    public async Task A_key_the_administrator_owns_sees_no_account()
    {
        var world = await TwoResellerWorld.SeedAsync();
        var sites = new StubAccountResourceCounter(AccountResourceCount.Counted(AccountResourceKind.Sites, 4));

        var result = await HandleAsync(
            world, FakeCurrentUser.ApiKey(null), world.AdminAccount.Id, new RecordingAgentAccountsClient(), sites);

        Assert.Equal("AccountNotFound", result.Error!.Code);
    }

    /// <summary>A disk that could not be measured is the agent's typed failure and never a usage of zero.</summary>
    [Fact]
    public async Task A_disk_that_could_not_be_measured_is_the_agents_typed_failure()
    {
        var world = await TwoResellerWorld.SeedAsync();
        var failure = Error.Of("AgentUnavailable", ErrorType.Unavailable);
        var sites = new StubAccountResourceCounter(AccountResourceCount.Counted(AccountResourceKind.Sites, 4));

        var result = await HandleAsync(
            world, FakeCurrentUser.ApiKey(world.Alpha.Id), world.AlphaAccount.Id, new RecordingAgentAccountsClient(failure), sites);

        Assert.False(result.IsSuccess);
        Assert.Equal(failure, result.Error);
    }

    /// <summary>Runs the handler over a context filtered for the caller.</summary>
    /// <param name="world">The seeded world.</param>
    /// <param name="caller">The caller the context and the scope are built for.</param>
    /// <param name="accountId">The account to read.</param>
    /// <param name="agent">The agent double that measures the disk.</param>
    /// <param name="counter">The one counter registered for this test.</param>
    /// <returns>The handler's answer.</returns>
    private static async Task<Result<AccountUsageDto>> HandleAsync(
        TwoResellerWorld world,
        ICurrentUser caller,
        Guid accountId,
        RecordingAgentAccountsClient agent,
        StubAccountResourceCounter counter)
    {
        await using var dbContext = world.ContextFor(caller);
        var summary = new AccountResourceSummaryReader(dbContext, new ResellerScope(dbContext, caller), [counter]);
        return await new GetAccountUsageQueryHandler(dbContext, agent, summary, new StubDisplayNames())
            .HandleAsync(new GetAccountUsageQuery(accountId), CancellationToken.None);
    }
}

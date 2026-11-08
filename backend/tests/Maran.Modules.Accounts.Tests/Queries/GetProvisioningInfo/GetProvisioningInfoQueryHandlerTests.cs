using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Queries.GetProvisioningInfo;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Queries.GetProvisioningInfo;

/// <summary>
/// Behavioral contract of <see cref="GetProvisioningInfoQueryHandler"/>: a key's reseller reads its own
/// envelope and use beside the API revision and nobody else's, and a caller that acts for no reseller is
/// answered as not found.
/// </summary>
public sealed class GetProvisioningInfoQueryHandlerTests
{
    /// <summary>A key reads its own reseller's use and envelope, and the revision of the API.</summary>
    [Fact]
    public async Task A_key_reads_its_own_resellers_use_envelope_and_the_api_revision()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var result = await HandleAsync(world, FakeCurrentUser.ApiKey(world.Alpha.Id));

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(GetProvisioningInfoQueryHandler.ApiRevision, result.Value.ApiRevision);
        Assert.Equal(1, result.Value.AccountCount);
        Assert.Equal(1, result.Value.PlanCount);
        Assert.Equal(1_000, result.Value.DiskAllocatedMb);
        Assert.Equal(world.Alpha.Envelope.MaxAccounts, result.Value.Envelope.MaxAccounts);
    }

    /// <summary>The two resellers read different figures: the answer is for the key's reseller and no other.</summary>
    [Fact]
    public async Task The_answer_is_for_the_keys_reseller_and_no_other()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var alpha = await HandleAsync(world, FakeCurrentUser.ApiKey(world.Alpha.Id));
        var beta = await HandleAsync(world, FakeCurrentUser.ApiKey(world.Beta.Id));

        Assert.Equal(1_000, alpha.Value.DiskAllocatedMb);
        Assert.Equal(2_000, beta.Value.DiskAllocatedMb);
    }

    /// <summary>A key the administrator owns, and an administrator, act for no reseller and are answered as not found.</summary>
    [Fact]
    public async Task A_caller_that_acts_for_no_reseller_is_answered_as_not_found()
    {
        var world = await TwoResellerWorld.SeedAsync();

        var key = await HandleAsync(world, FakeCurrentUser.ApiKey(null));
        var administrator = await HandleAsync(world, FakeCurrentUser.Admin());

        Assert.Equal("ResellerNotFound", key.Error!.Code);
        Assert.Equal(ErrorType.NotFound, key.Error.Type);
        Assert.Equal("ResellerNotFound", administrator.Error!.Code);
    }

    /// <summary>The answer has no member that could carry a panel version or a figure of the host.</summary>
    [Fact]
    public void The_answer_has_no_member_for_the_panel_version_or_the_host()
    {
        var members = typeof(ProvisioningInfoDto).GetProperties().Select(property =>
        {
            return property.Name;
        }).ToList();

        Assert.Equal(["ApiRevision", "AccountCount", "PlanCount", "DiskAllocatedMb", "Envelope"], members);
    }

    /// <summary>Runs the handler over a context filtered for the caller.</summary>
    /// <param name="world">The seeded world.</param>
    /// <param name="caller">The caller the context and the scope are built for.</param>
    /// <returns>The handler's answer.</returns>
    private static async Task<Result<ProvisioningInfoDto>> HandleAsync(TwoResellerWorld world, ICurrentUser caller)
    {
        await using var dbContext = world.ContextFor(caller);
        return await new GetProvisioningInfoQueryHandler(
                dbContext, new ResellerScope(dbContext, caller), new ResellerUsageReader(dbContext))
            .HandleAsync(new GetProvisioningInfoQuery(), CancellationToken.None);
    }
}
